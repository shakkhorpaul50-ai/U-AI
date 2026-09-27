namespace UAI.Data;

public sealed class ChatSession
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "New chat";
    /// <summary>One of the keys in <see cref="UAI.Services.ModeCatalog"/>.</summary>
    public string Mode { get; set; } = "english_chat";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser User { get; set; } = null!;
    public List<ChatMessage> Messages { get; set; } = [];
}
