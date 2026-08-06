using SiNet.Application.Diagnostics;
using Xunit;

namespace SiNet.App.Wpf.Tests.Diagnostics;

public sealed class WheaEventParserTests
{
    [Fact]
    public void WhenEvent19MessageSaysCorrectedThenIsCorrectedAndRawXmlOmitted()
    {
        const string xml =
            """
            <Event>
              <EventData>
                <Data Name="ErrorSource">Machine Check Exception</Data>
                <Data Name="ApicId">0x2</Data>
                <Data Name="MCABank">0</Data>
              </EventData>
            </Event>
            """;

        var details = WheaEventParser.TryParse(
            xml,
            eventId: 19,
            message: "A corrected hardware error has occurred.");

        Assert.NotNull(details);
        Assert.True(details!.IsCorrected);
        Assert.Null(details.RawXml);
        Assert.Equal("0", details.McaBank);
    }

    [Fact]
    public void WhenUncorrectedWheaXmlThenFieldsAndRawXmlAreKept()
    {
        const string xml =
            """
            <Event>
              <EventData>
                <Data Name="ErrorSource">Machine Check Exception</Data>
                <Data Name="ApicId">0x2</Data>
                <Data Name="MCABank">0</Data>
                <Data Name="Address">0xffff</Data>
                <Data Name="MciStat">0x1</Data>
                <Data Name="ProcessorId">0</Data>
              </EventData>
            </Event>
            """;

        var details = WheaEventParser.TryParse(xml, eventId: 18, message: "An uncorrected hardware error has occurred.");

        Assert.NotNull(details);
        Assert.False(details!.IsCorrected);
        Assert.NotNull(details.RawXml);
        Assert.Contains("MCABank", details.RawXml, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenCorrectedWheaXmlThenRawXmlIsOmitted()
    {
        const string xml =
            """
            <Event>
              <EventData>
                <Data Name="MCABank">1</Data>
                <Data Name="ApicId">3</Data>
              </EventData>
            </Event>
            """;

        var details = WheaEventParser.TryParse(xml, eventId: 17);

        Assert.NotNull(details);
        Assert.True(details!.IsCorrected);
        Assert.Null(details.RawXml);
    }

    [Fact]
    public void WhenSameBankRepeatsThenHasRepeatBankIsTrue()
    {
        var events = new[]
        {
            new WorkstationCrashEventDto
            {
                TimeCreated = DateTimeOffset.UtcNow,
                LogName = "System",
                EventId = 19,
                ProviderName = "Microsoft-Windows-WHEA-Logger",
                Whea = new WheaDetailsDto(null, "1", "0", null, null, null, true, null),
            },
            new WorkstationCrashEventDto
            {
                TimeCreated = DateTimeOffset.UtcNow.AddMinutes(-1),
                LogName = "System",
                EventId = 19,
                ProviderName = "Microsoft-Windows-WHEA-Logger",
                Whea = new WheaDetailsDto(null, "1", "0", null, null, null, true, null),
            },
        };

        Assert.True(WheaEventParser.HasRepeatBank(events));
    }

    [Fact]
    public void WhenMicrocodeBytesLittleEndianThenFormattedAsHexDword()
    {
        // Raw dump would look like 20010000; LE DWORD is 0x120.
        Assert.Equal("0x120", MemoryModuleFacts.FormatMicrocode([0x20, 0x01, 0x00, 0x00]));
    }

    [Fact]
    public void WhenDimmsDifferThenMixedFlagIsTrue()
    {
        var modules = new[]
        {
            new MemoryModuleDto("A", "P1", 16, 4800, 4800, "0", "A1"),
            new MemoryModuleDto("A", "P2", 16, 5600, 5600, "1", "A2"),
        };

        Assert.True(MemoryModuleFacts.HasMixedDimms(modules));
    }

    [Fact]
    public void WhenWerOnlyReportIdClusterThenItIsNotAnIncident()
    {
        var now = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(3));
        var report = WorkstationCrashReportBuilder.Build(
            new WorkstationCrashQuery(now.AddDays(-14), ["acad.exe"], CrashReportScope.Both, 2000),
            WorkstationCrashReportBuilderTests.TestContext,
            WorkstationCrashReportBuilderTests.TestMachine,
            [
                new WorkstationCrashEventDto
                {
                    TimeCreated = now.AddHours(-1),
                    LogName = "Application",
                    EventId = 1001,
                    ProviderName = "Windows Error Reporting",
                    AppName = "MsMpEng.exe",
                    ReportId = "WER-ONLY-1",
                    Message = "WER",
                },
            ],
            now);

        Assert.Empty(report.Incidents);
        Assert.Equal(0, report.Summary.OtherApplicationCrashIncidents);
    }

    [Fact]
    public void WhenMachineHasMinidumpsThenBugcheckFlagIsTrue()
    {
        var now = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(3));
        var machine = WorkstationCrashReportBuilderTests.TestMachine with
        {
            KernelMinidumpCount = 5,
            KernelMinidumpFileNames = ["a.dmp", "b.dmp"],
        };

        var report = WorkstationCrashReportBuilder.Build(
            new WorkstationCrashQuery(now.AddDays(-14), ["acad.exe"], CrashReportScope.Both, 2000),
            WorkstationCrashReportBuilderTests.TestContext,
            machine,
            [],
            now);

        Assert.True(report.Summary.HasBugCheck);
        Assert.Contains("Kernel minidump", WorkstationCrashReportFormatter.ToMarkdown(report), StringComparison.Ordinal);
    }
}
