namespace SiNet.Domain.Files;

/// <summary>
/// Parsed ProjectWork scan exclusion rules (extensions + name prefixes). Immutable.
/// </summary>
public sealed class ParsedProjectWorkScanExclusions
{
    public ParsedProjectWorkScanExclusions(
        IReadOnlySet<string> extensions,
        IReadOnlySet<string> namePrefixes)
    {
        Extensions = extensions;
        NamePrefixes = namePrefixes;
    }

    /// <summary>Extensions including the leading dot (ordinal ignore-case set).</summary>
    public IReadOnlySet<string> Extensions { get; }

    /// <summary>File-name prefixes such as <c>~$</c> (ordinal ignore-case set).</summary>
    public IReadOnlySet<string> NamePrefixes { get; }

    public bool Matches(string? fullPathOrName)
    {
        if (string.IsNullOrWhiteSpace(fullPathOrName))
        {
            return false;
        }

        var name = Path.GetFileName(fullPathOrName);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var prefix in NamePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var extension = Path.GetExtension(name);
        return !string.IsNullOrEmpty(extension) && Extensions.Contains(extension);
    }
}

/// <summary>
/// ProjectWork scan exclusion rules — legacy V2 extensions plus Office lock prefix <c>~$</c>.
/// CSV tokens starting with <c>.</c> are extensions; all other tokens are name prefixes.
/// </summary>
public static class ProjectWorkScanExclusions
{
    /// <summary>Default CSV written to SystemSettings when the row is missing.</summary>
    public const string DefaultRulesCsv =
        ".bak,.dwt,.dwl,.dwl2,.ini,.$ds,.err,.tmp,.log,.exe,~$";

    private static readonly ParsedProjectWorkScanExclusions DefaultParsed = Parse(DefaultRulesCsv);

    public static ParsedProjectWorkScanExclusions Default => DefaultParsed;

    /// <summary>True when the file name matches the <see cref="Default"/> rule set.</summary>
    public static bool IsExcludedExtension(string? fullPathOrName) =>
        Default.Matches(fullPathOrName);

    public static ParsedProjectWorkScanExclusions Parse(string? rulesCsv)
    {
        if (string.IsNullOrWhiteSpace(rulesCsv))
        {
            return DefaultParsed;
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rulesCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            if (raw.StartsWith(".", StringComparison.Ordinal))
            {
                extensions.Add(raw);
            }
            else
            {
                prefixes.Add(raw);
            }
        }

        if (extensions.Count == 0 && prefixes.Count == 0)
        {
            return DefaultParsed;
        }

        return new ParsedProjectWorkScanExclusions(extensions, prefixes);
    }

    public static bool Matches(string? fullPathOrName, ParsedProjectWorkScanExclusions rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Matches(fullPathOrName);
    }
}
