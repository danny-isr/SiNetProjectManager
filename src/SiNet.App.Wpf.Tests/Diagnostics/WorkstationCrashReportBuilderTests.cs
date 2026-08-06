using SiNet.Application.Diagnostics;
using Xunit;

namespace SiNet.App.Wpf.Tests.Diagnostics;

/// <summary>
/// Severity, correlation, scope and aggregation for «דוח קריסות תחנה» (DEV-010). The builder is pure,
/// so none of this needs a real Event Log.
/// </summary>
public sealed class WorkstationCrashReportBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void WhenProviderIsBugCheckThenEventId1001IsCritical()
    {
        var severity = WorkstationCrashReportBuilder.ClassifySeverity("Microsoft-Windows-WER-SystemErrorReporting", 1001);
        var bugCheck = WorkstationCrashReportBuilder.ClassifySeverity("BugCheck", 1001);

        Assert.Equal(CrashSeverity.Critical, bugCheck);
        Assert.NotEqual(CrashSeverity.Critical, severity);
    }

    [Fact]
    public void WhenProviderIsWindowsErrorReportingThenEventId1001IsOnlySupporting()
    {
        var severity = WorkstationCrashReportBuilder.ClassifySeverity("Windows Error Reporting", 1001);

        Assert.Equal(CrashSeverity.Supporting, severity);
    }

    [Fact]
    public void WhenApplicationErrorIsReportedThenItIsAnAppCrash()
    {
        var severity = WorkstationCrashReportBuilder.ClassifySeverity("Application Error", 1000);

        Assert.Equal(CrashSeverity.AppCrash, severity);
    }

    [Fact]
    public void WhenWheaLoggerReportsThenTheEventCountsAsHardware()
    {
        var isHardware = WorkstationCrashReportBuilder.IsHardwareEvent("Microsoft-Windows-WHEA-Logger", 17);

        Assert.True(isHardware);
    }

    [Fact]
    public void WhenAppCrashHappensWithinFiveMinutesOfCriticalEventThenItIsCorrelated()
    {
        var report = Build(
        [
            AppCrash(Now.AddMinutes(-30)),
            Critical(Now.AddMinutes(-32), "Microsoft-Windows-WHEA-Logger", 17),
        ]);

        var crash = report.Events.Single(e => e.Severity == CrashSeverity.AppCrash);

        Assert.Contains("WHEA-Logger 17", crash.CorrelatedWith!, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenCriticalEventIsFarFromTheAppCrashThenNothingIsCorrelated()
    {
        var report = Build(
        [
            AppCrash(Now.AddHours(-1)),
            Critical(Now.AddHours(-5), "Microsoft-Windows-WHEA-Logger", 17),
        ]);

        var crash = report.Events.Single(e => e.Severity == CrashSeverity.AppCrash);

        Assert.Null(crash.CorrelatedWith);
    }

    [Fact]
    public void WhenScopeIsApplicationOnlyThenSystemEventsAreExcluded()
    {
        var report = Build(
            [AppCrash(Now.AddMinutes(-10)), Critical(Now.AddMinutes(-11), "BugCheck", 1001)],
            CrashReportScope.ApplicationOnly);

        Assert.All(report.Events, e => Assert.Equal("Application", e.LogName));
    }

    [Fact]
    public void WhenScopeIsMachineOnlyThenApplicationEventsAreExcluded()
    {
        var report = Build(
            [AppCrash(Now.AddMinutes(-10)), Critical(Now.AddMinutes(-11), "BugCheck", 1001)],
            CrashReportScope.MachineOnly);

        Assert.All(report.Events, e => Assert.Equal("System", e.LogName));
    }

    [Fact]
    public void WhenAppErrorAndWerShareReportIdThenTheyFormOneIncident()
    {
        var report = Build(
        [
            AppCrash(Now.AddMinutes(-30), reportId: "R-1"),
            Wer(Now.AddMinutes(-30).AddSeconds(2), reportId: "R-1"),
        ]);

        Assert.Equal(1, report.Incidents.Count);
        Assert.Equal(CrashIncidentKind.ApplicationCrash, report.Incidents[0].Kind);
        Assert.Equal(2, report.Incidents[0].RecordCount);
        Assert.All(report.Events, e => Assert.Equal(report.Incidents[0].IncidentId, e.IncidentId));
        Assert.Equal(1, report.Summary.IncidentCount);
        Assert.Equal(report.Summary.IncidentsPerDay, Math.Round(1 / 14d, 2));
    }

    [Fact]
    public void WhenKernelPowerAndEventLogAreCloseThenTheyFormOneShutdownIncident()
    {
        var report = Build(
        [
            Critical(Now.AddHours(-2), "Microsoft-Windows-Kernel-Power", 41),
            Critical(Now.AddHours(-2).AddMinutes(1), "EventLog", 6008),
        ]);

        Assert.Equal(1, report.Summary.UnexpectedShutdownIncidents);
        Assert.Equal(1, report.Incidents.Count);
        Assert.Equal(CrashIncidentKind.UnexpectedShutdown, report.Incidents[0].Kind);
    }

    [Fact]
    public void WhenOtherAppCrashesThenTheyAreCountedSeparately()
    {
        var report = Build(
        [
            AppCrash(Now.AddHours(-1)),
            new WorkstationCrashEventDto
            {
                TimeCreated = Now.AddHours(-2),
                LogName = "Application",
                EventId = 1000,
                ProviderName = "Application Error",
                AppName = "chrome.exe",
                ModuleName = "chrome.dll",
                ExceptionCode = "0xc0000005",
            },
        ]);

        Assert.Equal(1, report.Summary.CivilApplicationCrashIncidents);
        Assert.Equal(1, report.Summary.OtherApplicationCrashIncidents);
    }

    [Fact]
    public void WhenBugCheckIsPresentThenTheSummaryFlagsIt()
    {
        var report = Build([Critical(Now.AddHours(-2), "BugCheck", 1001)]);

        Assert.True(report.Summary.HasBugCheck);
    }

    [Fact]
    public void WhenKernelPowerReportsThenTheSummaryFlagsAnUnexpectedShutdown()
    {
        var report = Build([Critical(Now.AddHours(-2), "Microsoft-Windows-Kernel-Power", 41)]);

        Assert.True(report.Summary.HasUnexpectedShutdown);
    }

    [Fact]
    public void WhenOneModuleFaultsMostThenItLeadsTheTopModules()
    {
        var report = Build(
        [
            AppCrash(Now.AddHours(-1), module: "atio6axx.dll"),
            AppCrash(Now.AddHours(-2), module: "atio6axx.dll"),
            AppCrash(Now.AddHours(-3), module: "acdb25.dll"),
        ]);

        Assert.Equal("atio6axx.dll", report.Summary.TopModules[0].Key);
    }

    [Fact]
    public void WhenMaxEventsIsReachedThenOnlyTheNewestEventsSurvive()
    {
        var events = Enumerable
            .Range(1, 10)
            .Select(i => AppCrash(Now.AddHours(-i)))
            .ToList();

        var report = Build(events, maxEvents: 3);

        Assert.Equal(3, report.Events.Count);
        Assert.Equal(Now.AddHours(-1), report.Events[0].TimeCreated);
    }

    private static WorkstationCrashReport Build(
        IReadOnlyList<WorkstationCrashEventDto> events,
        CrashReportScope scope = CrashReportScope.Both,
        int maxEvents = 2000)
    {
        var query = new WorkstationCrashQuery(Now.AddDays(-14), ["acad.exe"], scope, maxEvents);
        return WorkstationCrashReportBuilder.Build(query, TestContext, TestMachine, events, Now);
    }

    internal static CrashReportContextDto TestContext { get; } = new(
        CrashReasonCategory.Civil3DRepeatCrash,
        "Civil 3D נסגר בלי הודעה כשפותחים קובץ גדול.",
        null);

    internal static MachineProfileDto TestMachine { get; } = new(
        "SI-WS-07",
        "danny",
        "Windows 11 Pro",
        "10.0.26200",
        "Intel Core i9",
        24,
        64d,
        120d,
        900d,
        [new GraphicsAdapterDto("NVIDIA RTX A2000", "31.0.15.3623", new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), "Ampere")],
        ["AutoCAD Civil 3D 2026 (26.0)"],
        TimeSpan.FromHours(30),
        new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
        [],
        SystemManufacturer: "Dell Inc.",
        SystemModel: "Precision 5860",
        BiosVersion: "1.2.3",
        MemoryModules:
        [
            new MemoryModuleDto("Samsung", "M123", 32d, 4800, 5600, "BANK 0", "DIMM_A1"),
            new MemoryModuleDto("Samsung", "M123", 32d, 4800, 5600, "BANK 1", "DIMM_A2"),
        ]);

    internal static WorkstationCrashEventDto AppCrash(
        DateTimeOffset time,
        string? module = "acdb25.dll",
        string? message = null,
        string? reportId = null)
        => new()
        {
            TimeCreated = time,
            LogName = "Application",
            EventId = 1000,
            ProviderName = "Application Error",
            AppName = "acad.exe",
            AppVersion = "26.0.0.0",
            ModuleName = module,
            ExceptionCode = "0xc0000005",
            Message = message,
            ReportId = reportId,
        };

    internal static WorkstationCrashEventDto Wer(DateTimeOffset time, string? reportId = null)
        => new()
        {
            TimeCreated = time,
            LogName = "Application",
            EventId = 1001,
            ProviderName = "Windows Error Reporting",
            AppName = "acad.exe",
            ReportId = reportId,
            Message = "WER bucket",
        };

    internal static WorkstationCrashEventDto Critical(DateTimeOffset time, string provider, int eventId)
        => new()
        {
            TimeCreated = time,
            LogName = "System",
            EventId = eventId,
            ProviderName = provider,
            Message = "machine event",
        };
}
