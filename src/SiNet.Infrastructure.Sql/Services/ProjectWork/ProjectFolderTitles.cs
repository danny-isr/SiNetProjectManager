namespace SiNet.Infrastructure.Sql.Services.ProjectWork;

/// <summary>
/// Shared folder-title constants for the synthetic project root. Accepts both the correct
/// Hebrew spelling and a historical single-yod typo that exists in older DB rows / code.
/// </summary>
internal static class ProjectFolderTitles
{
    public static readonly string[] RootTitles =
    [
        "\u05EA\u05D9\u05E7\u05D9\u05D9\u05EA \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8", // תיקיית הפרויקט
        "\u05EA\u05D9\u05E7\u05D9\u05EA \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8", // תיקית הפרויקט (legacy typo)
    ];

    public static bool IsProjectRoot(string? title) =>
        title is not null && RootTitles.Contains(title, StringComparer.Ordinal);
}
