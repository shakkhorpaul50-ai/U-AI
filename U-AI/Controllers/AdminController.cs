using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UAI.Data;
using UAI.Services;

namespace UAI.Controllers;

/// <summary>
/// Per-user quota administration. Gated to emails in ADMIN_EMAILS (comma-separated).
/// If ADMIN_EMAILS is unset, nobody can access this controller.
/// </summary>
[Authorize]
public sealed class AdminController(
    AppDbContext db,
    UserManager<AppUser> users,
    QuotaService quota,
    IConfiguration config) : Controller
{
    /// <summary>Built-in owner. Always admin, no env var needed.</summary>
    private static readonly string[] BuiltInAdmins = ["shakkhorpaul50@gmail.com"];

    private bool IsAdmin(AppUser u)
    {
        if (BuiltInAdmins.Contains(u.Email ?? "", StringComparer.OrdinalIgnoreCase))
            return true;
        var raw = config["Admin:Emails"] ?? Environment.GetEnvironmentVariable("ADMIN_EMAILS") ?? "";
        var allowed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Length > 0 &&
               allowed.Contains(u.Email ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public sealed class Row
    {
        public string UserId { get; set; } = "";
        public string Email { get; set; } = "";
        public int? DailyLimit { get; set; }
        public int? HourlyLimit { get; set; }
        public int UsedToday { get; set; }
        public int UsedHour { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var me = await users.GetUserAsync(User);
        if (me is null || !IsAdmin(me)) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var rows = await db.Users
            .OrderBy(u => u.Email)
            .Select(u => new Row
            {
                UserId = u.Id,
                Email = u.Email ?? "",
                DailyLimit = db.UserQuotas.Where(q => q.UserId == u.Id).Select(q => q.DailyLimit).FirstOrDefault(),
                HourlyLimit = db.UserQuotas.Where(q => q.UserId == u.Id).Select(q => q.HourlyLimit).FirstOrDefault(),
                UsedToday = db.UserUsages.Where(x => x.UserId == u.Id && x.Date == today).Select(x => x.Count).FirstOrDefault(),
                UsedHour = db.UserHourlyUsages.Where(x => x.UserId == u.Id && x.Hour == hour).Select(x => x.Count).FirstOrDefault(),
            })
            .ToListAsync(ct);

        ViewData["DefaultDaily"] = quota.DefaultDaily;
        ViewData["DefaultHourly"] = quota.DefaultHourly;
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLimit([FromForm] string userId, [FromForm] int? dailyLimit, [FromForm] int? hourlyLimit)
    {
        var me = await users.GetUserAsync(User);
        if (me is null || !IsAdmin(me)) return Forbid();

        var row = await db.UserQuotas.FirstOrDefaultAsync(q => q.UserId == userId);
        var d = dailyLimit is > 0 ? dailyLimit : null;
        var h = hourlyLimit is > 0 ? hourlyLimit : null;
        if (d is null && h is null)
        {
            if (row is not null) db.UserQuotas.Remove(row);
        }
        else
        {
            if (row is null)
                db.UserQuotas.Add(new UserQuota { UserId = userId, DailyLimit = d, HourlyLimit = h });
            else
            {
                row.DailyLimit = d;
                row.HourlyLimit = h;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Users));
    }
}
