using System.Globalization;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// Naming convention for crash report files. Shared by the store (writing, retention) and the UI
/// (save dialogs) so retention never deletes a file this app did not create.
/// </summary>
public static class CrashReportFileNames
{
    public const string CsvSuffix = "_crashes.csv";
    public const string MarkdownSuffix = "_analysis.md";

    private const string TimestampFormat = "yyyy-MM-dd_HHmm";

    /// <summary><c>{Machine}_{yyyy-MM-dd_HHmm}_{category}</c> without a suffix.</summary>
    public static string BuildBaseName(
        string machineName,
        DateTimeOffset generatedAt,
        CrashReasonCategory category)
    {
        if (string.IsNullOrWhiteSpace(machineName))
        {
            throw new ArgumentException("Machine name is required.", nameof(machineName));
        }

        var safeMachine = Sanitize(machineName);
        var stamp = generatedAt.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        return $"{safeMachine}_{stamp}_{CrashReasonCategoryDisplay.ToSlug(category)}";
    }

    public static string BuildCsvFileName(
        string machineName,
        DateTimeOffset generatedAt,
        CrashReasonCategory category)
        => BuildBaseName(machineName, generatedAt, category) + CsvSuffix;

    public static string BuildMarkdownFileName(
        string machineName,
        DateTimeOffset generatedAt,
        CrashReasonCategory category)
        => BuildBaseName(machineName, generatedAt, category) + MarkdownSuffix;

    /// <summary>True only for files this feature writes. Retention refuses to touch anything else.</summary>
    public static bool IsReportFile(string fileName)
        => TryGetBaseName(fileName) is not null;

    /// <summary>
    /// The shared base name of a report's CSV and Markdown pair, or null when the file was not
    /// written by this feature.
    /// </summary>
    public static string? TryGetBaseName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var baseName = fileName switch
        {
            _ when fileName.EndsWith(CsvSuffix, StringComparison.OrdinalIgnoreCase)
                => fileName[..^CsvSuffix.Length],
            _ when fileName.EndsWith(MarkdownSuffix, StringComparison.OrdinalIgnoreCase)
                => fileName[..^MarkdownSuffix.Length],
            _ => null,
        };

        return string.IsNullOrWhiteSpace(baseName) ? null : baseName;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray();
        return new string(chars);
    }
}
