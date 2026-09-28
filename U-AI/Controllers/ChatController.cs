using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UAI.Data;
using UAI.Models;
using UAI.Services;

namespace UAI.Controllers;

[Authorize]
public sealed class ChatController(
    AppDbContext db,
    UserManager<AppUser> users,
    PollinationsChatService ai,
    QuotaService quota,
    ILogger<ChatController> log) : Controller
{
    private const int MaxNewTokens = 512;

    [HttpGet]
    public async Task<IActionResult> Index(long? sessionId, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        var sessions = await db.ChatSessions
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(50)
            .Select(s => new SessionSummary(s.Id, s.Title, s.Mode, s.UpdatedAt))
            .ToListAsync(ct);

        var turns = new List<TurnDto>();
        var activeId = 0L;
        var activeMode = "english_chat";

        var target = sessionId is > 0
            ? sessions.FirstOrDefault(s => s.Id == sessionId)
            : sessions.FirstOrDefault();

        if (target is not null)
        {
            activeId = target.Id;
            activeMode = ModeCatalog.IsValid(target.Mode) ? target.Mode : "english_chat";
            turns = await db.ChatMessages
                .Where(m => m.SessionId == target.Id)
                .OrderBy(m => m.Id)
                .Select(m => new TurnDto(m.Id, m.Role, m.Content))
                .ToListAsync(ct);
        }

        ViewData["Sessions"] = sessions;
        ViewData["Turns"] = turns;
        ViewData["ActiveId"] = activeId;
        ViewData["ActiveMode"] = activeMode;
        ViewData["Busy"] = ai.Waiters;
        try { ViewData["Remaining"] = await quota.RemainingAsync(user.Id, ct); }
        catch { ViewData["Remaining"] = -1; }
        return View(ModeCatalog.All);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewSession([FromForm] string mode)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        var s = new ChatSession
        {
            UserId = user.Id,
            Mode = ModeCatalog.IsValid(mode) ? mode : "english_chat",
            Title = "New chat",
        };
        db.ChatSessions.Add(s);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { sessionId = s.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSession(long id)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        var s = await db.ChatSessions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, HttpContext.RequestAborted);
        if (s is not null)
        {
            db.ChatSessions.Remove(s);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    public sealed class SendRequest
    {
        public long SessionId { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Streams a reply as Server-Sent Events. Inference runs behind the gateway,
    /// so concurrent requests proxy independently; per-user quota guards the pipe.
    /// </summary>
    [HttpPost]
    public async Task Send([FromBody] SendRequest req, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) { Response.StatusCode = StatusCodes.Status401Unauthorized; return; }

        req.Message = (req.Message ?? "").Trim();
        if (req.Message.Length == 0) { Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        if (req.Message.Length > 4000) req.Message = req.Message[..4000];

        var session = await db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == req.SessionId && s.UserId == user.Id, ct);
        if (session is null) { Response.StatusCode = StatusCodes.Status404NotFound; return; }

        var history = await db.ChatMessages
            .Where(m => m.SessionId == session.Id)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        // Persist the user turn first so it survives an aborted generation.
        var userMsg = new ChatMessage { SessionId = session.Id, Role = "user", Content = req.Message };
        db.ChatMessages.Add(userMsg);

        if (history.Count == 0)
            session.Title = req.Message.Length > 60 ? req.Message[..60] : req.Message;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.Body.FlushAsync(ct);

        var sb = new StringBuilder();

        async Task Emit(string json)
        {
            var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
            await Response.Body.WriteAsync(bytes, ct);
            await Response.Body.FlushAsync(ct);
        }

        await Emit(JsonSerializer.Serialize(new { t = "queued", waiting = ai.Waiters }));

        // Identity questions never reach the model: answered instantly and
        // identically in every mode, with zero inference cost.
        if (CreatorAnswers.TryMatch(req.Message, out var canned) && canned is not null)
        {
            var cannedSb = new StringBuilder();
            try
            {
                foreach (var word in canned.Split(' '))
                {
                    ct.ThrowIfCancellationRequested();
                    var piece = word + " ";
                    cannedSb.Append(piece);
                    await Emit(JsonSerializer.Serialize(new { t = "token", v = piece }));
                    await Task.Delay(12, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped mid-reply: keep the partial text below.
            }

            var cannedText = cannedSb.ToString().Trim();
            if (cannedText.Length > 0)
            {
                db.ChatMessages.Add(new ChatMessage
                {
                    SessionId = session.Id,
                    Role = "assistant",
                    Content = cannedText,
                });
                session.UpdatedAt = DateTimeOffset.UtcNow;
                try { await db.SaveChangesAsync(CancellationToken.None); } catch { /* best effort */ }
            }

            await Emit(JsonSerializer.Serialize(new { t = "done", saved = cannedText.Length > 0 }));
            return;
        }

        // Fairness gates: burst first (cheap), then a billable daily unit.
        // Cached and deterministic answers never reach here, so they never count.
        // Both gates fail OPEN on infrastructure errors: a missing quota table
        // must never take chat offline.
        if (!quota.CheckBurst(user.Id))
        {
            await Emit(JsonSerializer.Serialize(new { t = "limited", v = "Slow down — one message every 20 seconds." }));
            await Emit(JsonSerializer.Serialize(new { t = "done", saved = false }));
            return;
        }

        var quotaEnforced = true;
        try
        {
            var (allowedHourly, hourlyLimit) = await quota.TryReserveHourlyAsync(user.Id, ct);
            if (!allowedHourly)
            {
                await Emit(JsonSerializer.Serialize(new { t = "limited", v = $"Hourly limit reached ({hourlyLimit}/hour). Try again next hour." }));
                await Emit(JsonSerializer.Serialize(new { t = "done", saved = false }));
                return;
            }

            var (allowed, limit) = await quota.TryReserveAsync(user.Id, ct);
            if (!allowed)
            {
                await quota.ReleaseHourlyAsync(user.Id);
                await Emit(JsonSerializer.Serialize(new { t = "limited", v = $"Daily limit reached ({limit}/day). Back tomorrow 00:00 UTC." }));
                await Emit(JsonSerializer.Serialize(new { t = "done", saved = false }));
                return;
            }
        }
        catch (Exception ex)
        {
            quotaEnforced = false;
            log.LogWarning(ex, "Quota check failed open for user {UserId}", user.Id);
        }

        var reserved = true;
        try
        {
            await ai.GenerateAsync(session.Mode, history, req.Message, MaxNewTokens, async ev =>
            {
                if (ev.Text is { Length: > 0 })
                {
                    sb.Append(ev.Text);
                    await Emit(JsonSerializer.Serialize(new { t = "token", v = ev.Text }));
                }
                else if (ev.Error is { Length: > 0 })
                {
                    await Emit(JsonSerializer.Serialize(new { t = "error", v = Trim(ev.Error) }));
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Client navigated away or pressed stop. Keep whatever was produced.
        }
        catch (Exception ex)
        {
            await Emit(JsonSerializer.Serialize(new { t = "error", v = "Generation failed: " + Trim(ex.Message) }));
        }
        finally
        {
            // Refund both reservations when nothing was produced (provider error
            // before first token). Partial streams stay billed.
            if (sb.Length == 0 && reserved && quotaEnforced)
            {
                reserved = false;
                await quota.ReleaseAsync(user.Id);
                await quota.ReleaseHourlyAsync(user.Id);
            }
        }

        var text = sb.ToString().Trim();
        if (text.Length > 0)
        {
            db.ChatMessages.Add(new ChatMessage
            {
                SessionId = session.Id,
                Role = "assistant",
                Content = text,
            });
            session.UpdatedAt = DateTimeOffset.UtcNow;
            try { await db.SaveChangesAsync(CancellationToken.None); } catch { /* best effort */ }
        }

        await Emit(JsonSerializer.Serialize(new { t = "done", saved = text.Length > 0 }));
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];
}
