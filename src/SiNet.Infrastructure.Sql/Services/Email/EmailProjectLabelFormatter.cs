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
        => labelName.StartsWith($"{rootLabel}/", StringComparison.OrdinalIgnoreCase);
}
