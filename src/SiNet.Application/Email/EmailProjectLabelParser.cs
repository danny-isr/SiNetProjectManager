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
}
