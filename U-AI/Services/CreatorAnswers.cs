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
        "when were you trained", "when was you trained",
        "who owns this", "who runs this", "who is behind this",
        "who is your father",
        "shakkhor",
    ];

    public const string SpecsReply =
        "I'm U-AI, a 134.5M-parameter SmolLM2 (int4, ~82 MB) with 5 modes — " +
        "not GPT-4 and not a 13B model. Built and fine-tuned by Shakkhor Paul: " +
        "https://github.com/shakkhorpaul50-ai";

    // Identity/spec questions the model would otherwise hallucinate answers to.
    // Every trigger is self-referential (you/your/this model) so legitimate
    // technical questions ("explain what an LSTM architecture is") still reach
    // the model. Matched against normalized text: lowercase, punctuation and
    // hyphens already turned into single spaces ("GPT-4" -> "gpt 4").
    private static readonly string[] SpecTriggers =
    [
        // architecture / model identity
        "what architecture are you", "what architecture do you use",
        "your architecture", "which model are you", "what model are you",
        "what model is this", "what model do you use", "what model is this ai",
        "what llm are you", "which llm are you", "what llm is this",
        "are you a transformer", "are you transformer based",
        "powers you", "what powers you", "what model powers this",
        // rival models
        "are you gpt", "are you gpt 4", "are you gpt4", "are you chatgpt",
        "based on gpt", "built on gpt", "use gpt", "powered by gpt",
        "are you claude", "are you gemini", "are you llama", "are you bert",
        "are you deepseek", "are you qwen", "are you mistral", "are you falcon",
        // company / affiliation
        "are you openai", "made by openai", "powered by openai", "use openai",
        "made by google", "made by meta", "made by microsoft",
        "made by anthropic", "are you made by", "what company made you",
        // size / hardware
        "how many parameters", "your parameters", "how big are you",
        "how large are you", "how many billion parameters",
        "are you 13 billion", "are you 13b", "how much vram",
        "how much ram do you", "how many layers do you",
        "what hardware are you", "what hardware do you run on",
        "what gpu are you", "what gpu do you run on", "where are you running",
        // training provenance
        "what were you trained on", "how were you trained",
        "your training data", "what dataset were you",
        "what is your training data",
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
        foreach (var t in SpecTriggers)
        {
            if (norm.Contains(t, StringComparison.Ordinal))
            {
                reply = SpecsReply;
                return true;
            }
        }
        return false;
    }
}
