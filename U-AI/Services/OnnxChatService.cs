using System.Threading.Channels;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace UAI.Services;

public readonly record struct GenEvent(string? Text = null, bool Done = false, string? Error = null)
{
    public static GenEvent Of(string t) => new(t);
    public static GenEvent End() => new(Done: true);
    public static GenEvent Fail(string e) => new(Error: e, Done: true);
}

/// <summary>
/// Owns the single ONNX model instance for the whole process.
///
/// Two constraints shape this class:
///  - The host has 0.1 CPU, so decode is memory-bandwidth bound and gains nothing
///    from threads. One generation at a time is enforced by a 1-slot gate; running
///    two would halve each one's speed and double KV memory.
///  - Generator.GenerateNextToken() is a blocking native call. Running it on a
///    thread-pool thread would starve request handling, so each generation gets a
///    dedicated background thread and streams tokens back over a typed channel.
/// </summary>
public sealed class OnnxChatService : IDisposable
{
    private readonly ILogger<OnnxChatService> _log;
    private readonly string _modelPath;
    private readonly int _threads;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _loadLock = new();

    private Model? _model;
    private Tokenizer? _tokenizer;
    private volatile bool _loadFailed;
    private string? _loadError;
    private int _waiters;

    public OnnxChatService(IConfiguration config, ILogger<OnnxChatService> log)
    {
        _log = log;
        _modelPath = ResolveModelPath(config);
        _threads = int.TryParse(config["Model:Threads"], out var t) && t > 0 ? t : 1;
    }

    public bool IsReady => _model is not null;
    public bool LoadFailed => _loadFailed;
    public string? LoadError => _loadError;
    public int Waiters => Volatile.Read(ref _waiters);
    public string ModelPath => _modelPath;

    private static string ResolveModelPath(IConfiguration config)
    {
        var configured = config["Model:Path"];
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        foreach (var c in new[]
                 {
                     Path.Combine(Directory.GetCurrentDirectory(), "model"),
                     Path.Combine(AppContext.BaseDirectory, "model"),
                 })
        {
            if (File.Exists(Path.Combine(c, "genai_config.json")))
                return Path.GetFullPath(c);
        }

        return Path.GetFullPath(configured ?? "model");
    }

    /// <summary>Loads the model if it is not already up. Safe to call concurrently.</summary>
    public bool EnsureLoaded()
    {
        if (_model is not null) return true;
        if (_loadFailed) return false;

        lock (_loadLock)
        {
            if (_model is not null) return true;
            if (_loadFailed) return false;

            if (!File.Exists(Path.Combine(_modelPath, "genai_config.json")))
            {
                _loadFailed = true;
                _loadError = $"genai_config.json not found in '{_modelPath}'. Set Model:Path.";
                _log.LogError("Model not found at {Path}", _modelPath);
                return false;
            }

            // Must be set before the native library initialises.
            Environment.SetEnvironmentVariable("OMP_NUM_THREADS", _threads.ToString());

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var m = new Model(_modelPath);
                _tokenizer = new Tokenizer(m);
                _model = m;
                _log.LogInformation("Model loaded from {Path} in {Ms} ms", _modelPath, sw.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                _loadError = ex.Message;
                _log.LogError(ex, "Model load failed from {Path}", _modelPath);
                return false;
            }
        }
    }

    /// <summary>
    /// Streams a completion token by token. <paramref name="onEvent"/> is invoked once
    /// per token and once more with a terminal event.
    /// </summary>
    public async Task GenerateAsync(
        string prompt,
        int maxNewTokens,
        Func<GenEvent, Task> onEvent,
        CancellationToken ct)
    {
        if (!EnsureLoaded())
            throw new InvalidOperationException(_loadError ?? "Model unavailable");

        var model = _model!;
        var tokenizer = _tokenizer!;

        Interlocked.Increment(ref _waiters);
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _waiters);
            throw;
        }

        try
        {
            var channel = Channel.CreateBounded<GenEvent>(new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
            var stop = new CancellationTokenSource();

            var worker = new Thread(() =>
            {
                TokenizerStream? stream = null;
                Generator? gen = null;
                Sequences? seqs = null;
                try
                {
                    stream = tokenizer.CreateStream();

                    var p = new GeneratorParams(model);
                    p.SetSearchOption("max_length", (double)maxNewTokens);
                    p.SetSearchOption("do_sample", true);
                    p.SetSearchOption("temperature", 0.8);
                    p.SetSearchOption("top_p", 0.95);

                    gen = new Generator(model, p);
                    seqs = tokenizer.Encode(prompt);
                    gen.AppendTokenSequences(seqs);

                    while (!gen.IsDone() && !stop.IsCancellationRequested)
                    {
                        gen.GenerateNextToken();

                        // GetNextTokens returns a ReadOnlySpan: consume it immediately.
                        var toks = gen.GetNextTokens();
                        if (toks.Length == 0) break;

                        var text = stream.Decode(toks[0]);
                        if (string.IsNullOrEmpty(text)) continue;

                        channel.Writer.WriteAsync(GenEvent.Of(text), stop.Token)
                                  .AsTask().GetAwaiter().GetResult();
                    }

                    channel.Writer.TryWrite(GenEvent.End());
                }
                catch (Exception ex)
                {
                    channel.Writer.TryWrite(GenEvent.Fail(ex.Message));
                }
                finally
                {
                    try { seqs?.Dispose(); gen?.Dispose(); stream?.Dispose(); } catch { /* native teardown */ }
                    channel.Writer.TryComplete();
                }
            })
            { IsBackground = true, Name = "onnx-gen" };

            worker.Start();

            try
            {
                await foreach (var ev in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await onEvent(ev).ConfigureAwait(false);
                    if (ev.Done) break;
                }
            }
            catch (OperationCanceledException)
            {
                stop.Cancel();
                while (worker.IsAlive && !worker.Join(100))
                {
                    while (channel.Reader.TryRead(out _)) { }   // drain so the writer unblocks
                }
                throw;
            }
            finally
            {
                stop.Cancel();
            }
        }
        finally
        {
            _gate.Release();
            Interlocked.Decrement(ref _waiters);
        }
    }

    public void Dispose()
    {
        _tokenizer?.Dispose();
        _model?.Dispose();
        _gate.Dispose();
    }
}
