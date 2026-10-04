namespace MedReminder.Domain.Prescriptions;

// The Italian electronic prescription number (NRE;
// docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.2): 15 letters
// or digits, printed on the paper slip as two groups and shown by the
// regional services, often with spaces or dashes. No
// check digit: no public specification of one was found, so none is
// verified. Never logged, as the prescription code.
public readonly record struct NreCode
{
    public const int Length = 15;

    private NreCode(string value) => Value = value;

    // Upper case, without separators.
    public string Value { get; }

    public override string ToString() => Value;

    // Drops spaces (any white space) and dashes and upper-cases the rest;
    // succeeds when exactly 15 ASCII letters or digits remain.
    public static bool TryParse(string? text, out NreCode code)
    {
        code = default;
        if (text is null) return false;
        Span<char> buffer = stackalloc char[Length];
        var count = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == '-') continue;
            if (!char.IsAsciiLetterOrDigit(c) || count == Length) return false;
            buffer[count++] = char.ToUpperInvariant(c);
        }
        if (count != Length) return false;
        code = new NreCode(new string(buffer));
        return true;
    }
}
