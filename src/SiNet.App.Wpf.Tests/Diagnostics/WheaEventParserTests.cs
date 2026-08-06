using SiNet.Application.Diagnostics;
using Xunit;

namespace SiNet.App.Wpf.Tests.Diagnostics;

public sealed class WheaEventParserTests
{
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

        var details = WheaEventParser.TryParse(xml, eventId: 19);

        Assert.NotNull(details);
        Assert.False(details!.IsCorrected);
        Assert.Equal("0", details.McaBank);
        Assert.Equal("0x2", details.ApicId);
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
                Whea = new WheaDetailsDto(null, "1", "0", null, null, null, false, null),
            },
            new WorkstationCrashEventDto
            {
                TimeCreated = DateTimeOffset.UtcNow.AddMinutes(-1),
                LogName = "System",
                EventId = 19,
                ProviderName = "Microsoft-Windows-WHEA-Logger",
                Whea = new WheaDetailsDto(null, "1", "0", null, null, null, false, null),
            },
        };

        Assert.True(WheaEventParser.HasRepeatBank(events));
    }

    [Fact]
    public void WhenMicrocodeBytesThenHexIsLowercase()
    {
        Assert.Equal("0a0b", MemoryModuleFacts.FormatMicrocode([0x0a, 0x0b]));
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
}
