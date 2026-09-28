using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using UAI.Data;

namespace UAI.Services;

/// <summary>
/// Two-layer per-user fairness:
///  - Daily quota in Postgres (atomic increment-and-check, survives restarts).
///    Only billable upstream calls count; cache hits and deterministic answers don't.
///  - Burst cap in memory (sliding 60s window). Stops one user occupying the
///    shared upstream pipe in real time. Resets on restart — harmless.
/// Individual overrides live in UserQuota; NULL DailyLimit means global default.
/// </summary>
public sealed class QuotaService
{
    private readonly AppDbContext _db;
    private readonly int _defaultDaily;
    private readonly int _defaultPerMinute;
    private readonly ConcurrentDictionary<string, List<DateTime>> _bursts = new();
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(60);

    public QuotaService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _defaultDaily = int.TryParse(config["Quota:DefaultDaily"], out var d) && d > 0 ? d : 50;
        _defaultPerMinute = int.TryParse(config["Quota:DefaultPerMinute"], out var m) && m > 0 ? m : 3;
    }

    public int DefaultDaily => _defaultDaily;

    public async Task<int> GetLimitAsync(string userId, CancellationToken ct = default)
    {
        var row = await _db.UserQuotas.AsNoTracking()
            .FirstOrDefaultAsync(q => q.UserId == userId, ct).ConfigureAwait(false);
        return row?.DailyLimit is > 0 ? row.DailyLimit.Value : _defaultDaily;
    }

    /// <summary>Sliding-window burst check. Returns false when over the per-minute cap.</summary>
    public bool CheckBurst(string userId)
    {
        var now = DateTime.UtcNow;
        var list = _bursts.GetOrAdd(userId, _ => []);
        lock (list)
        {
            list.RemoveAll(t => now - t > BurstWindow);
            if (list.Count >= _defaultPerMinute) return false;
            list.Add(now);
            return true;
        }
    }

    /// <summary>
    /// Atomically reserves one daily unit. Returns (allowed, limit).
    /// Single statement, so concurrent requests can't race past the cap.
    /// </summary>
    public async Task<(bool allowed, int limit)> TryReserveAsync(string userId, CancellationToken ct = default)
    {
        var limit = await GetLimitAsync(userId, ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var count = await _db.Database.SqlQuery<int>(
                $"INSERT INTO \"UserUsage\" (\"UserId\", \"Date\", \"Count\") VALUES ({userId}, {today}, 1) ON CONFLICT (\"UserId\", \"Date\") DO UPDATE SET \"Count\" = \"UserUsage\".\"Count\" + 1 RETURNING \"Count\"")
            .SingleAsync(ct).ConfigureAwait(false);

        if (count > limit)
        {
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE \"UserUsage\" SET \"Count\" = GREATEST(\"Count\" - 1, 0) WHERE \"UserId\" = {0} AND \"Date\" = {1}",
                [userId, today]).ConfigureAwait(false);
            return (false, limit);
        }
        return (true, limit);
    }

    /// <summary>Refunds one unit (upstream failed before producing anything).</summary>
    public async Task ReleaseAsync(string userId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE \"UserUsage\" SET \"Count\" = GREATEST(\"Count\" - 1, 0) WHERE \"UserId\" = {0} AND \"Date\" = {1}",
                [userId, today]).ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    public async Task<int> RemainingAsync(string userId, CancellationToken ct = default)
    {
        var limit = await GetLimitAsync(userId, ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var used = await _db.UserUsages.AsNoTracking()
            .Where(u => u.UserId == userId && u.Date == today)
            .Select(u => u.Count)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return Math.Max(0, limit - used);
    }
}
