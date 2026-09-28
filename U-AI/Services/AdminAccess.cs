namespace UAI.Services;

/// <summary>
/// Single source of truth for admin access. Used by AdminController (enforcement)
/// and ChatController (showing the nav link). The owner address is built in so
/// the panel works with zero configuration; ADMIN_EMAILS / Admin:Emails adds more.
/// </summary>
public static class AdminAccess
{
    public static readonly string[] BuiltInAdmins = ["shakkhorpaul50@gmail.com"];

    public static bool IsAdmin(IConfiguration config, string? email)
    {
        if (!string.IsNullOrWhiteSpace(email) &&
            BuiltInAdmins.Contains(email, StringComparer.OrdinalIgnoreCase))
            return true;

        var raw = config["Admin:Emails"] ?? Environment.GetEnvironmentVariable("ADMIN_EMAILS") ?? "";
        var allowed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Length > 0 &&
               !string.IsNullOrWhiteSpace(email) &&
               allowed.Contains(email, StringComparer.OrdinalIgnoreCase);
    }
}
