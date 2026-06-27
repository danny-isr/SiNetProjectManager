namespace SiNet.Domain.ValueObjects;

/// <summary>
/// Value object representing a validated email address.
/// Keeps validation in the domain layer so application/infrastructure code
/// can rely on well-formed values.
/// </summary>
public readonly record struct EmailAddress
{
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
}
