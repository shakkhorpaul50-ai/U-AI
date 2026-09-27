using UAI.Data;

namespace UAI.Services;

/// <summary>
/// Builds ChatML prompts that match the template the checkpoint was trained on.
/// Trims old turns to keep the whole sequence inside the context window.
/// </summary>
public static class PromptBuilder
{
    public const int ContextWindow = 1024;   // tokens. Chosen for the 0.1 CPU speed budget.
    public const int ReserveForOutput = 200; // tokens reserved for the reply.

    private const string ImStart = "<|im_start|>";
    private const string ImEnd = "<|im_end|>";

    /// <summary>Average characters per token for this tokenizer; close enough for trimming.</summary>
    private const double CharsPerToken = 3.4;

    public static string System(string mode) =>
        $"{ImStart}system\n[mode={mode}]\n{ModeCatalog.Find(mode)?.Persona ?? ""}{ImEnd}\n";

    /// <summary>
    /// Renders the conversation as a generation prompt. Newest turns win when the
    /// budget is tight, because dropping the newest turn is what users notice.
    /// </summary>
    public static string Build(string mode, IReadOnlyList<ChatMessage> history, string pendingUserMessage)
    {
        int budget = (int)((ContextWindow - ReserveForOutput) * CharsPerToken);

        string sys = System(mode);
        string head = sys + $"{ImStart}user\n{pendingUserMessage}{ImEnd}\n{ImStart}assistant\n";

        var turns = new System.Text.StringBuilder();
        int used = head.Length;

        for (int i = history.Count - 1; i >= 0; i--)
        {
            var m = history[i];
            string role = m.Role == "assistant" ? "assistant" : "user";
            string piece = $"{ImStart}{role}\n{m.Content}{ImEnd}\n";

            if (used + piece.Length > budget) break;
            turns.Insert(0, piece);
            used += piece.Length;
        }

        return sys + turns + head.Substring(sys.Length);
    }
}
