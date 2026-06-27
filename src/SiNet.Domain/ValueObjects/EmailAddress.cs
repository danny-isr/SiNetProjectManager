namespace SiNet.Domain.ValueObjects;

/// <summary>
/// Value object representing a validated email address.
/// Keeps validation in the domain layer so application/infrastructure code
/// can rely on well-formed values.
/// </summary>
public readonly record struct EmailAddress
{
    /// <summary>
    /// Used when an inbound address cannot be parsed. Lets mapping continue with a valid,
    /// clearly-synthetic value instead of throwing and losing the whole message.
    /// </summary>
    public static readonly EmailAddress Unknown = new("unknown@local.invalid");

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
        {
            throw new ArgumentException("Invalid email address.", nameof(value));
        }

        Value = value.Trim();
    }

    /// <summary>The normalized email address text.</summary>
    public string Value { get; }

    public override string ToString() => Value;

    /// <summary>
    /// Attempts to extract a valid email address from raw header text such as
    /// <c>"Display Name &lt;user@host&gt;"</c> or a bare <c>user@host</c>. Does not throw.
    /// </summary>
    /// <returns><c>true</c> and a populated <paramref name="address"/> on success; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? raw, out EmailAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Trim();

        // "Display Name <user@host>" -> take the part inside the angle brackets.
        int open = candidate.IndexOf('<');
        int close = candidate.IndexOf('>');
        if (open >= 0 && close > open)
        {
            candidate = candidate.Substring(open + 1, close - open - 1).Trim();
        }

        // Reduce to the first whitespace-free token that contains '@'.
        foreach (var token in candidate.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Contains('@'))
            {
                candidate = token;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate) || !candidate.Contains('@'))
        {
            return false;
        }

        address = new EmailAddress(candidate);
        return true;
    }

    /// <summary>
    /// Parses <paramref name="raw"/> when possible, otherwise returns <see cref="Unknown"/>.
    /// Never throws, so it is safe to use while mapping batches of inbound emails.
    /// </summary>
    public static EmailAddress CreateOrFallback(string? raw) =>
        TryParse(raw, out var address) ? address : Unknown;
}
