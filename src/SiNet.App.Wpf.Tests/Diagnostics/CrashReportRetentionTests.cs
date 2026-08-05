using System.IO;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Diagnostics;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Diagnostics;
using Xunit;

namespace SiNet.App.Wpf.Tests.Diagnostics;

/// <summary>
/// Retention for «דוח קריסות תחנה» (DEV-010): old reports of one machine are removed, the newest
/// reports always survive, and files the feature did not write are never touched.
/// </summary>
public sealed class CrashReportRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void WhenAReportIsOlderThanTheThresholdThenBothOfItsFilesAreSelected()
    {
        var files = BuildReports(count: 8, oldestFirstAgeDays: 400);

        var doomed = CrashReportRetentionPlanner.SelectForDeletion(files, retentionDays: 180, Now);

        Assert.Contains(doomed, f => f.FileName.EndsWith(CrashReportFileNames.CsvSuffix, StringComparison.Ordinal));
        Assert.Contains(doomed, f => f.FileName.EndsWith(CrashReportFileNames.MarkdownSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public void WhenAllReportsAreExpiredThenTheNewestFiveStillSurvive()
    {
        var files = BuildReports(count: 9, oldestFirstAgeDays: 400);

        var doomed = CrashReportRetentionPlanner.SelectForDeletion(files, retentionDays: 30, Now);
        var survivingFiles = files.Count - doomed.Count;

        Assert.Equal(CrashReportRetentionPlanner.AlwaysKeepNewestReports * 2, survivingFiles);
    }

    [Fact]
    public void WhenReportsAreYoungerThanTheThresholdThenNothingIsSelected()
    {
        var files = BuildReports(count: 9, oldestFirstAgeDays: 20);

        var doomed = CrashReportRetentionPlanner.SelectForDeletion(files, retentionDays: 180, Now);

        Assert.Empty(doomed);
    }

    [Fact]
    public void WhenAForeignFileIsPresentThenItIsNeverSelected()
    {
        var files = new List<CrashReportFileInfo>(BuildReports(count: 8, oldestFirstAgeDays: 400))
        {
            new("payroll-2020.xlsx", Now.AddDays(-2000)),
        };

        var doomed = CrashReportRetentionPlanner.SelectForDeletion(files, retentionDays: 30, Now);

        Assert.DoesNotContain(doomed, f => f.FileName == "payroll-2020.xlsx");
    }

    [Fact]
    public void WhenRetentionIsDisabledThenNothingIsSelected()
    {
        var files = BuildReports(count: 20, oldestFirstAgeDays: 4000);

        var doomed = CrashReportRetentionPlanner.SelectForDeletion(files, retentionDays: 0, Now);

        Assert.Empty(doomed);
    }

    [Fact]
    public void WhenRetentionRunsOnAFolderThenOnlyExpiredReportFilesAreDeleted()
    {
        var folder = Directory.CreateTempSubdirectory("sinet-crash-retention").FullName;

        try
        {
            for (var i = 1; i <= 8; i++)
            {
                WriteReportPair(folder, i, DateTime.Now.AddDays(-100 * i));
            }

            var foreign = Path.Combine(folder, "keep-me.txt");
            File.WriteAllText(foreign, "not a crash report");
            File.SetLastWriteTime(foreign, DateTime.Now.AddDays(-5000));

            var store = new FileSystemCrashReportStore(new StubSettings(), new NullLogger());
            var (deleted, warning) = store.ApplyRetention(folder, retentionDays: 180, DateTimeOffset.Now);

            Assert.Null(warning);
            Assert.Equal(6, deleted);
            Assert.True(File.Exists(foreign));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void WriteReportPair(string folder, int index, DateTime lastWrite)
    {
        foreach (var suffix in new[] { CrashReportFileNames.CsvSuffix, CrashReportFileNames.MarkdownSuffix })
        {
            var path = Path.Combine(folder, $"SI-WS-07_2026-01-{index:00}_1200_civil3d-repeat{suffix}");
            File.WriteAllText(path, "x");
            File.SetLastWriteTime(path, lastWrite);
        }
    }

    private static IReadOnlyList<CrashReportFileInfo> BuildReports(int count, int oldestFirstAgeDays)
        => Enumerable
            .Range(1, count)
            .SelectMany(i => new[]
            {
                new CrashReportFileInfo(
                    $"SI-WS-07_2026-01-{i:00}_1200_civil3d-repeat{CrashReportFileNames.CsvSuffix}",
                    Now.AddDays(-oldestFirstAgeDays + i)),
                new CrashReportFileInfo(
                    $"SI-WS-07_2026-01-{i:00}_1200_civil3d-repeat{CrashReportFileNames.MarkdownSuffix}",
                    Now.AddDays(-oldestFirstAgeDays + i)),
            })
            .ToList();

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class StubSettings : ISystemSettingsQueryService
    {
        public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Retention does not read settings.");
    }
}
