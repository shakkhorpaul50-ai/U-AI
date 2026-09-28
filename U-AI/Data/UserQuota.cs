namespace UAI.Data;

/// <summary>
/// Per-user daily quota override. NULL DailyLimit (or missing row) = global default.
/// </summary>
public sealed class UserQuota
{
    public string UserId { get; set; } = "";
    public int? DailyLimit { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser User { get; set; } = null!;
}

/// <summary>Billable upstream calls per user per UTC day. Only this table gates quota.</summary>
public sealed class UserUsage
{
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
    public int Count { get; set; }

    public AppUser User { get; set; } = null!;
}
