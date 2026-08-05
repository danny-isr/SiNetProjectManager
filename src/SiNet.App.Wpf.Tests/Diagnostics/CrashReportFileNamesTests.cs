using SiNet.Application.Diagnostics;
using Xunit;

namespace SiNet.App.Wpf.Tests.Diagnostics;

/// <summary>
/// The share folder must be readable without opening files, and retention must be able to tell our
/// own reports from anything else that lands there (DEV-010).
/// </summary>
public sealed class CrashReportFileNamesTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 5, 14, 32, 0, TimeSpan.FromHours(3));

    [Fact]
    public void WhenBuildingACsvNameThenItCarriesMachineTimestampAndCategorySlug()
    {
        var name = CrashReportFileNames.BuildCsvFileName(
            "SI-WS-07", GeneratedAt, CrashReasonCategory.UnexpectedShutdown);

        Assert.Equal("SI-WS-07_2026-08-05_1432_unexpected-shutdown_crashes.csv", name);
    }

    [Fact]
    public void WhenBuildingAMarkdownNameThenItSharesTheBaseNameWithTheCsv()
    {
        var csv = CrashReportFileNames.BuildCsvFileName("SI-WS-07", GeneratedAt, CrashReasonCategory.BlueScreen);
        var markdown = CrashReportFileNames.BuildMarkdownFileName("SI-WS-07", GeneratedAt, CrashReasonCategory.BlueScreen);

        Assert.Equal(
            CrashReportFileNames.TryGetBaseName(csv),
            CrashReportFileNames.TryGetBaseName(markdown));
    }

    [Fact]
    public void WhenTheMachineNameContainsSpacesThenTheyAreReplaced()
    {
        var name = CrashReportFileNames.BuildBaseName("Danny PC", GeneratedAt, CrashReasonCategory.Other);

        Assert.StartsWith("Danny-PC_", name, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenTheFileIsNotOneOfOursThenItIsNotARecognizedReport()
    {
        Assert.False(CrashReportFileNames.IsReportFile("payroll-2020.xlsx"));
    }

    [Fact]
    public void WhenTheFileNameIsOnlyTheSuffixThenItIsNotARecognizedReport()
    {
        Assert.False(CrashReportFileNames.IsReportFile(CrashReportFileNames.CsvSuffix));
    }
}
