namespace SiNet.Domain.Identifiers;

/// <summary>
/// Strongly-typed identifier for a SiNet project, replacing bare <see cref="int"/> ids
/// (for example the Office Management project id 136).
/// </summary>
public readonly record struct ProjectId(int Value)
{
    public override string ToString() => Value.ToString();
}
