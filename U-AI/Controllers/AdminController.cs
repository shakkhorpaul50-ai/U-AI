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
    private bool IsAdmin(AppUser u)
    {
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
        public int UsedToday { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var me = await users.GetUserAsync(User);
        if (me is null || !IsAdmin(me)) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await db.Users
            .OrderBy(u => u.Email)
            .Select(u => new Row
            {
                UserId = u.Id,
                Email = u.Email ?? "",
                DailyLimit = db.UserQuotas.Where(q => q.UserId == u.Id).Select(q => q.DailyLimit).FirstOrDefault(),
                UsedToday = db.UserUsages.Where(x => x.UserId == u.Id && x.Date == today).Select(x => x.Count).FirstOrDefault(),
            })
            .ToListAsync(ct);

        ViewData["DefaultDaily"] = quota.DefaultDaily;
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLimit([FromForm] string userId, [FromForm] int? dailyLimit)
    {
        var me = await users.GetUserAsync(User);
        if (me is null || !IsAdmin(me)) return Forbid();

        var row = await db.UserQuotas.FirstOrDefaultAsync(q => q.UserId == userId);
        if (dailyLimit is null or <= 0)
        {
            if (row is not null) db.UserQuotas.Remove(row);
        }
        else
        {
            if (row is null)
                db.UserQuotas.Add(new UserQuota { UserId = userId, DailyLimit = dailyLimit });
            else
            {
                row.DailyLimit = dailyLimit;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Users));
    }
}
