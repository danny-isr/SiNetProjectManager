using SiNet.Application.Diagnostics;
using Xunit;

using static SiNet.App.Wpf.Tests.Diagnostics.WorkstationCrashReportBuilderTests;

namespace SiNet.App.Wpf.Tests.Diagnostics;

/// <summary>
/// CSV escaping and the split of responsibilities between the two exports: the Markdown carries the
/// user's explanation for the AI, the CSV stays a clean table (DEV-010).
/// </summary>
public sealed class WorkstationCrashReportFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void WhenMessageContainsCommaThenTheCsvCellIsQuoted()
    {
        var csv = WorkstationCrashReportFormatter.ToCsv(
            BuildReport(AppCrash(Now.AddHours(-1), message: "Faulting module, offset 0x1234")));

        Assert.Contains("\"Faulting module, offset 0x1234\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenMessageContainsQuotesThenTheCsvDoublesThem()
    {
        var csv = WorkstationCrashReportFormatter.ToCsv(
            BuildReport(AppCrash(Now.AddHours(-1), message: "module \"acdb25.dll\" failed")));

        Assert.Contains("\"module \"\"acdb25.dll\"\" failed\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenMessageContainsNewLinesThenTheCsvKeepsThemInsideOneQuotedCell()
    {
        var csv = WorkstationCrashReportFormatter.ToCsv(
            BuildReport(AppCrash(Now.AddHours(-1), message: "line one\nline two")));

        Assert.Contains("\"line one\nline two\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenRenderingCsvThenTheHeaderRowComesFirst()
    {
        var csv = WorkstationCrashReportFormatter.ToCsv(BuildReport(AppCrash(Now.AddHours(-1))));

        Assert.StartsWith("Time,Log,EventId,Provider,Severity", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenRenderingMarkdownThenTheUserDescriptionIsIncluded()
    {
        var markdown = WorkstationCrashReportFormatter.ToMarkdown(BuildReport(AppCrash(Now.AddHours(-1))));

        Assert.Contains(TestContext.Description, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenRenderingCsvThenTheUserDescriptionIsNotIncluded()
    {
        var csv = WorkstationCrashReportFormatter.ToCsv(BuildReport(AppCrash(Now.AddHours(-1))));

        Assert.DoesNotContain(TestContext.Description, csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenRenderingMarkdownThenTheMachineProfileIsIncluded()
    {
        var markdown = WorkstationCrashReportFormatter.ToMarkdown(BuildReport(AppCrash(Now.AddHours(-1))));

        Assert.Contains("NVIDIA RTX A2000", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenRenderingMarkdownThenFirmwareAndIncidentsSectionsAreIncluded()
    {
        var markdown = WorkstationCrashReportFormatter.ToMarkdown(
            BuildReport(AppCrash(Now.AddHours(-1), reportId: "R-9"), Wer(Now.AddHours(-1).AddSeconds(1), reportId: "R-9")));

        Assert.Contains("BIOS", markdown, StringComparison.Ordinal);
        Assert.Contains("## Incidents", markdown, StringComparison.Ordinal);
        Assert.Contains("IncidentId", WorkstationCrashReportFormatter.ToCsv(
            BuildReport(AppCrash(Now.AddHours(-1)))), StringComparison.Ordinal);
    }

    [Fact]
    public void WhenThereAreNoEventsThenTheMarkdownSaysSoInsteadOfRenderingAnEmptyTable()
    {
        var markdown = WorkstationCrashReportFormatter.ToMarkdown(BuildReport());

        Assert.Contains("No matching events", markdown, StringComparison.Ordinal);
    }

    private static WorkstationCrashReport BuildReport(params WorkstationCrashEventDto[] events)
    {
        var query = new WorkstationCrashQuery(Now.AddDays(-14), ["acad.exe"], CrashReportScope.Both, 2000);
        return WorkstationCrashReportBuilder.Build(query, TestContext, TestMachine, events, Now);
    }
}
