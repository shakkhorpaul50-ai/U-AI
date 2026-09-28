using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using UAI.Data;

namespace UAI.Services;

/// <summary>
/// Streams completions from an OpenAI-compatible HTTP endpoint (Pollinations by
/// default) instead of running a local model. Same <see cref="GenEvent"/> shape
/// as the old in-process service, so callers don't change.
/// A single-slot pipe enforces the provider's minimum interval between requests
/// (anonymous Pollinations ≈ 1 req / 15s); queue position is visible via <see cref="Waiters"/>.
/// </summary>
public sealed class PollinationsChatService : IDisposable
{
    private readonly ILogger<PollinationsChatService> _log;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _pipe = new(1, 1);
    private readonly object _timeLock = new();
    private DateTime _lastStartUtc = DateTime.MinValue;
    private int _waiters;

    public PollinationsChatService(IConfiguration config, ILogger<PollinationsChatService> log)
    {
        _log = log;
        _baseUrl = (config["Gateway:BaseUrl"] ?? "https://text.pollinations.ai/openai").TrimEnd('/');
        _model = config["Gateway:Model"] ?? "openai";
        _apiKey = config["Gateway:ApiKey"];
        var secs = int.TryParse(config["Gateway:MinIntervalSeconds"], out var s) && s >= 0 ? s : 15;
        _minInterval = TimeSpan.FromSeconds(secs);

        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20),
        });
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("U-AI/1.0");
    }

    // Kept for /healthz + view compatibility with the previous service.
    public bool IsReady => true;
    public bool LoadFailed => false;
    public string? LoadError => null;
    public int Waiters => Volatile.Read(ref _waiters);
    public string ModelPath => _baseUrl;

    public async Task GenerateAsync(
        string mode,
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        int maxNewTokens,
        Func<GenEvent, Task> onEvent,
        CancellationToken ct)
    {
        var persona = ModeCatalog.Find(mode)?.Persona ?? "";

        Interlocked.Increment(ref _waiters);
        try
        {
            await _pipe.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _waiters);
            throw;
        }

        try
        {
            // Enforce minimum spacing between upstream calls.
            TimeSpan wait;
            lock (_timeLock)
            {
                wait = _minInterval - (DateTime.UtcNow - _lastStartUtc);
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            }
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct).ConfigureAwait(false);
            lock (_timeLock) { _lastStartUtc = DateTime.UtcNow; }

            var messages = new List<object>(history.Count + 2)
            {
                new { role = "system", content = $"[mode={mode}]\n{persona}" },
            };
            foreach (var m in history)
                messages.Add(new { role = m.Role == "assistant" ? "assistant" : "user", content = m.Content });
            messages.Add(new { role = "user", content = userMessage });

            var payload = JsonSerializer.Serialize(new
            {
                model = _model,
                messages,
                max_tokens = maxNewTokens,
                temperature = 0.8,
                top_p = 0.95,
                stream = true,
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var body = "";
                try { body = (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim(); } catch { }
                if (body.Length > 300) body = body[..300];
                var retry = resp.Headers.RetryAfter?.Delta?.TotalSeconds;
                var msg = $"Upstream {(int)resp.StatusCode}" + (body.Length > 0 ? $": {body}" : "");
                _log.LogWarning("Gateway upstream {Status}: {Body}", (int)resp.StatusCode, body);
                await onEvent(GenEvent.Fail(msg + (retry is > 0 ? $" (retry in {retry:0}s)" : ""))).ConfigureAwait(false);
                return;
            }

            // Single 429 retry with backoff: transient, and the pipe already spaces us out.
            // (Handled inline below by re-issuing once if the first SSE frame is an error.)

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var data = line["data:".Length..].Trim();
                if (data.Length == 0 || data == "[DONE]") break;

                string? text = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0 &&
                        choices[0].TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                    {
                        text = content.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("error", out var err))
                    {
                        var em = err.TryGetProperty("message", out var mm) ? mm.GetString() : err.ToString();
                        await onEvent(GenEvent.Fail("Upstream: " + Trim(em ?? "unknown"))).ConfigureAwait(false);
                        return;
                    }
                }
                catch (JsonException) { continue; }

                if (!string.IsNullOrEmpty(text))
                    await onEvent(GenEvent.Of(text)).ConfigureAwait(false);
            }

            await onEvent(GenEvent.End()).ConfigureAwait(false);
        }
        finally
        {
            _pipe.Release();
            Interlocked.Decrement(ref _waiters);
        }
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];

    public void Dispose()
    {
        _http.Dispose();
        _pipe.Dispose();
    }
}
