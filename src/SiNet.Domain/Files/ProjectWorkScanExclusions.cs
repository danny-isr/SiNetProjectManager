namespace SiNet.Domain.Files;

/// <summary>
/// File extensions that must never appear in a ProjectWork file scan (legacy V2
/// <c>ExcludedExtensions</c> — DEV-003). Pure; no IO.
/// </summary>
public static class ProjectWorkScanExclusions
{
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bak",
        ".dwt",
        ".dwl",
        ".dwl2",
        ".ini",
        ".$ds",
        ".err",
        ".tmp",
        ".log",
        ".exe",
    };

    /// <summary>True when the file name's extension is on the excluded list.</summary>
    public static bool IsExcludedExtension(string? fullPathOrName)
    {
        if (string.IsNullOrWhiteSpace(fullPathOrName))
        {
            return false;
        }

        var extension = Path.GetExtension(fullPathOrName);
        return !string.IsNullOrEmpty(extension) && ExcludedExtensions.Contains(extension);
    }
}
