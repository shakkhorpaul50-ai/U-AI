namespace UAI.Services;

/// <summary>One streaming unit: a text chunk, a terminal signal, or a failure.</summary>
public readonly record struct GenEvent(string? Text = null, bool Done = false, string? Error = null)
{
    public static GenEvent Of(string t) => new(t);
    public static GenEvent End() => new(Done: true);
    public static GenEvent Fail(string e) => new(Error: e, Done: true);
}
