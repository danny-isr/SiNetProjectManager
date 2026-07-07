namespace SiNet.Infrastructure.Sql.Services.Email;

internal static class EmailProjectLabelFormatter
{
    public static string FormatProjectName(int projectId, string? nameAndNumber, string? title)
    {
        if (!string.IsNullOrWhiteSpace(nameAndNumber))
        {
            return nameAndNumber;
        }

        return $"({projectId}){title ?? $"Project_{projectId}"}";
    }

    public static string GetLocation(string? placeTitle)
        => string.IsNullOrWhiteSpace(placeTitle) ? "General" : placeTitle;

    public static bool IsProjectLabel(string labelName, string rootLabel)
        => labelName.StartsWith($"{rootLabel}/", StringComparison.OrdinalIgnoreCase)
            && labelName.Count(static ch => ch == '/') >= 2;

    public static bool TryParseProjectLabelPath(
        string fullPath,
        string rootLabel,
        out string location,
        out string projectDisplayName)
    {
        location = string.Empty;
        projectDisplayName = string.Empty;

        if (!IsProjectLabel(fullPath, rootLabel))
        {
            return false;
        }

        var suffix = fullPath[(rootLabel.Length + 1)..];
        var slashIndex = suffix.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= suffix.Length - 1)
        {
            return false;
        }

        location = suffix[..slashIndex];
        projectDisplayName = suffix[(slashIndex + 1)..];
        return !string.IsNullOrWhiteSpace(location) && !string.IsNullOrWhiteSpace(projectDisplayName);
    }
}
