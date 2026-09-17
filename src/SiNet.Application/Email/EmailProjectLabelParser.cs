using System.Text.RegularExpressions;

namespace SiNet.Application.Email;

/// <summary>
/// Parses Gmail project label paths into project id/display (legacy <c>GoogleService.ExtractProjectIdFromLabel</c> parity).
/// </summary>
public static class EmailProjectLabelParser
{
    private static readonly Regex ProjectIdRegex = new(@"^\((\d+)\)", RegexOptions.Compiled);

    public static int? TryExtractProjectIdFromDisplaySegment(string? projectDisplaySegment)
    {
        if (string.IsNullOrWhiteSpace(projectDisplaySegment)
            || !projectDisplaySegment.TrimStart().StartsWith('('))
        {
            return null;
        }

        var match = ProjectIdRegex.Match(projectDisplaySegment.Trim());
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    public static string? TryExtractProjectDisplaySegment(string? projectLabelPath)
    {
        if (string.IsNullOrWhiteSpace(projectLabelPath))
        {
            return null;
        }

        var parts = projectLabelPath.Split('/');
        return parts.Length >= 1 ? parts[^1] : null;
    }

    public static (int? ProjectId, string? ProjectDisplayName)? TryParseProjectFromLabelPath(
        string? labelPath,
        string rootLabel = EmailGmailLabelNames.RootLabel)
    {
        if (string.IsNullOrWhiteSpace(labelPath)
            || !EmailGmailLabelNames.IsProjectLabel(labelPath, rootLabel))
        {
            return null;
        }

        var display = TryExtractProjectDisplaySegment(labelPath);
        if (string.IsNullOrWhiteSpace(display))
        {
            return null;
        }

        return (TryExtractProjectIdFromDisplaySegment(display), display.Trim());
    }

    /// <summary>
    /// Strict project-label parse: leaf under <paramref name="rootLabel"/> whose name starts with
    /// <c>(digits)</c>. Parent folders and labels outside the root are rejected.
    /// </summary>
    public static ProjectLabelEntry? TryParseProjectLabel(
        string? labelId,
        string? fullPath,
        string rootLabel = EmailGmailLabelNames.RootLabel)
    {
        var parsed = TryParseProjectFromLabelPath(fullPath, rootLabel);
        if (parsed is not { ProjectId: int number } || number <= 0
            || string.IsNullOrWhiteSpace(parsed.Value.ProjectDisplayName)
            || string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        return new ProjectLabelEntry(
            string.IsNullOrWhiteSpace(labelId) ? fullPath.Trim() : labelId.Trim(),
            fullPath.Trim(),
            number,
            parsed.Value.ProjectDisplayName,
            TryExtractParentPath(fullPath) ?? string.Empty);
    }

    public static string? TryExtractParentPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var lastSlash = fullPath.LastIndexOf('/');
        return lastSlash <= 0 ? null : fullPath[..lastSlash];
    }

    public static string BuildCanonicalPath(string rootLabel, string location, string projectDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDisplayName);
        return $"{rootLabel.Trim()}/{location.Trim()}/{projectDisplayName.Trim()}";
    }
}
