namespace UAI.Data;

public sealed class ChatMessage
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ChatSession Session { get; set; } = null!;
}
