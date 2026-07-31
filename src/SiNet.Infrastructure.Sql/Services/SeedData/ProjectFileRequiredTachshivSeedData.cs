using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Thin facade kept for existing call sites/tests — delegates to <see cref="ProjectFileCatalogSeedData"/>.
/// </summary>
public static class ProjectFileRequiredOmdanSeedData
{
    public const string DisplayTitle = "\u05D0\u05D5\u05DE\u05D3\u05DF_\u05D4\u05E6\u05E2\u05D4"; // אומדן_הצעה
    public const string DefaultTypeFile = ".xlsx";
    public const string CatalogCode = ProjectFileCatalogCodes.QuoteEstimate;
    public const string FolderTitle = "\u05E0\u05D9\u05D4\u05D5\u05DC_\u05DB\u05E1\u05E4\u05D9"; // ניהול_כספי
    public const string ParentFolderTitle = "\u05EA\u05DB\u05EA\u05D5\u05D1\u05EA"; // תכתובת

    public static Task<string> EnsureAsync(SiNetSQLDbContext db, CancellationToken ct = default)
        => ProjectFileCatalogSeedData.EnsureAsync(db, ct);

    public static bool IsOmdanCatalogTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return false;

        foreach (var alias in ProjectFileCatalogSeedData.TitleAliases(DisplayTitle))
        {
            if (string.Equals(title, alias, StringComparison.Ordinal)
                || title.StartsWith(alias, StringComparison.Ordinal))
                return true;
        }

        return title.Equals("\u05EA\u05D7\u05E9\u05D9\u05D1", StringComparison.Ordinal)
               || title.StartsWith("\u05EA\u05D7\u05E9\u05D9\u05D1", StringComparison.Ordinal)
               || title.Equals("\u05D0\u05D5\u05DE\u05D3\u05DF \u05D4\u05E6\u05E2\u05EA \u05DE\u05D7\u05D9\u05E8", StringComparison.Ordinal)
               || title.StartsWith("\u05D0\u05D5\u05DE\u05D3\u05DF \u05D4\u05E6\u05E2\u05EA \u05DE\u05D7\u05D9\u05E8", StringComparison.Ordinal)
               || title.Equals("\u05D0\u05D5\u05DE\u05D3\u05DF_\u05D4\u05E6\u05E2\u05EA_\u05DE\u05D7\u05D9\u05E8", StringComparison.Ordinal)
               || title.StartsWith("\u05D0\u05D5\u05DE\u05D3\u05DF_\u05D4\u05E6\u05E2\u05EA_\u05DE\u05D7\u05D9\u05E8", StringComparison.Ordinal);
    }

    public static bool IsOmdanCatalogCode(string? code) =>
        string.Equals(code, CatalogCode, StringComparison.Ordinal);
}

/// <summary>Backward-compatible alias.</summary>
[Obsolete("Use ProjectFileCatalogSeedData / ProjectFileRequiredOmdanSeedData.")]
public static class ProjectFileRequiredTachshivSeedData
{
    public const string DisplayTitle = ProjectFileRequiredOmdanSeedData.DisplayTitle;
    public const string DefaultTypeFile = ProjectFileRequiredOmdanSeedData.DefaultTypeFile;

    public static Task<string> EnsureAsync(SiNetSQLDbContext db, CancellationToken ct = default)
        => ProjectFileRequiredOmdanSeedData.EnsureAsync(db, ct);

    public static bool IsTachshivCatalogTitle(string? title)
        => ProjectFileRequiredOmdanSeedData.IsOmdanCatalogTitle(title);
}
