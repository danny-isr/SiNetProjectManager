namespace SiNet.Application.Diagnostics;

/// <summary>One candidate file in a machine's crash-report folder.</summary>
public sealed record CrashReportFileInfo(string FileName, DateTimeOffset LastWrite);

/// <summary>
/// Decides which crash reports may be deleted. Pure so retention is provable without a file system:
/// only files this feature wrote are eligible, the newest reports always survive, and nothing is
/// deleted before the retention threshold.
/// </summary>
public static class CrashReportRetentionPlanner
{
    /// <summary>Reports kept regardless of age, so a machine's recent history is never wiped.</summary>
    public const int AlwaysKeepNewestReports = 5;

    public static IReadOnlyList<CrashReportFileInfo> SelectForDeletion(
        IReadOnlyList<CrashReportFileInfo> files,
        int retentionDays,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (retentionDays <= 0)
        {
            return [];
        }

        var cutoff = now.AddDays(-retentionDays);

        // A report is a CSV + Markdown pair sharing one base name; age and survival are per report.
        return files
            .Select(f => (File: f, BaseName: CrashReportFileNames.TryGetBaseName(f.FileName)))
            .Where(x => x.BaseName is not null)
            .GroupBy(x => x.BaseName!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(x => x.File.LastWrite))
            .Skip(AlwaysKeepNewestReports)
            .Where(g => g.Max(x => x.File.LastWrite) < cutoff)
            .SelectMany(g => g.Select(x => x.File))
            .ToList();
    }
}
