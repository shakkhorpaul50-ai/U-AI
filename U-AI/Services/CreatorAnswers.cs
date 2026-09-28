namespace UAI.Services;

/// <summary>
/// Deterministic answers that bypass the model entirely: instant, no CPU spent,
/// and identical across all five modes. The 135M model is unreliable at
/// following "say who made you" instructions, so identity questions are
/// answered here instead of in any persona.
/// </summary>
public static class CreatorAnswers
{
    public const string Reply =
        "I was created by Shakkhor Paul.\n\n" +
        "GitHub: https://github.com/shakkhorpaul50-ai\n" +
        "Facebook: https://www.facebook.com/profile.php?id=100023479221437";

    private static readonly string[] Triggers =
    [
        "who created you", "who create you", "who is your creator",
        "who made you", "who built you", "who developed you",
        "who were you created by", "your creator", "your developer",
        "your maker", "your owner", "who owns you",
        "who programmed you", "who coded you",
        "who is your builder", "your builder", "who is your maker",
        "who made this app", "who built this app", "who developed this app",
        "who created this app", "who designed this app",
        "who made this website", "who made this site",
        "who made this chatbot", "who made this bot",
        "who is the developer", "who is the creator", "who is the builder",
        "created this app", "made this app",
        "who created this ai", "who made this ai", "who built this ai",
        "who developed this ai", "who designed this ai",
        "who created this", "who made this", "who built this",
        "who developed this", "who designed this",
        "who invented you", "who designed you", "who trained you",
        "who owns this", "who runs this", "who is behind this",
        "who is your father",
        "shakkhor",
    ];

    public static bool TryMatch(string message, out string? reply)
    {
        reply = null;
        if (string.IsNullOrWhiteSpace(message)) return false;

        var norm = string.Join(" ", new string(message
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : ' ')
                .ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        foreach (var t in Triggers)
        {
            if (norm.Contains(t, StringComparison.Ordinal))
            {
                reply = Reply;
                return true;
            }
        }
        return false;
    }
}
