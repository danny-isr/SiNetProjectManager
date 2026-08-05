namespace SiNet.Application.Diagnostics;

/// <summary>Which event sources a workstation crash report covers (DEV-010).</summary>
public enum CrashReportScope
{
    /// <summary>Application crashes and machine events, including correlation between them.</summary>
    Both = 0,

    /// <summary>Civil 3D / acad style application crashes only.</summary>
    ApplicationOnly = 1,

    /// <summary>Bugchecks, unexpected shutdowns, hardware and disk events only.</summary>
    MachineOnly = 2,
}

/// <summary>How severe a single event is. Derived from provider + event id, never guessed.</summary>
public enum CrashSeverity
{
    /// <summary>Context only (WER buckets and similar). Not an incident on its own.</summary>
    Supporting = 0,

    /// <summary>The application died; the operating system kept running.</summary>
    AppCrash = 1,

    /// <summary>The machine failed, or hardware reported an error.</summary>
    Critical = 2,
}

/// <summary>Why the user produced the report. Mandatory human context for the AI analysis.</summary>
public enum CrashReasonCategory
{
    Civil3DRepeatCrash = 0,
    UnexpectedShutdown = 1,
    BlueScreen = 2,
    FreezeOrSlowness = 3,
    CrashDuringSpecificAction = 4,
    Other = 5,
}

/// <summary>What the user asked the reader to collect.</summary>
public sealed record WorkstationCrashQuery(
    DateTimeOffset Since,
    IReadOnlyList<string> AppNameFilters,
    CrashReportScope Scope,
    int MaxEvents)
{
    /// <summary>Whole days covered by the query, at least 1.</summary>
    public int LookbackDays(DateTimeOffset now)
        => Math.Max(1, (int)Math.Ceiling((now - Since).TotalDays));
}

/// <summary>One Windows event, enriched with severity and correlation by the builder.</summary>
public sealed record WorkstationCrashEventDto
{
    public required DateTimeOffset TimeCreated { get; init; }

    /// <summary><c>Application</c> or <c>System</c>.</summary>
    public required string LogName { get; init; }

    public required int EventId { get; init; }

    public required string ProviderName { get; init; }

    public string? LevelDisplayName { get; init; }

    public string? AppName { get; init; }

    public string? AppVersion { get; init; }

    public string? ModuleName { get; init; }

    public string? ModuleVersion { get; init; }

    public string? ExceptionCode { get; init; }

    public string? FaultOffset { get; init; }

    public string? AppPath { get; init; }

    public string? ModulePath { get; init; }

    public string? ReportId { get; init; }

    public string? Message { get; init; }

    public CrashSeverity Severity { get; init; } = CrashSeverity.Supporting;

    /// <summary>Set when this app crash happened close to a <see cref="CrashSeverity.Critical"/> event.</summary>
    public string? CorrelatedWith { get; init; }
}

/// <summary>One display adapter with its driver stamp — the usual suspect in Civil 3D crashes.</summary>
public sealed record GraphicsAdapterDto(
    string Name,
    string? DriverVersion,
    DateTimeOffset? DriverDate,
    string? VideoProcessor);

/// <summary>
/// Hardware / OS context for the machine that produced the report. Without this an AI cannot tell
/// a GPU driver fault from a memory or disk problem.
/// </summary>
public sealed record MachineProfileDto(
    string MachineName,
    string UserName,
    string OsCaption,
    string OsVersion,
    string CpuName,
    int LogicalProcessorCount,
    double TotalMemoryGb,
    double SystemDriveFreeGb,
    double SystemDriveTotalGb,
    IReadOnlyList<GraphicsAdapterDto> GraphicsAdapters,
    IReadOnlyList<string> InstalledAutodeskProducts,
    TimeSpan Uptime,
    DateTimeOffset? LastWindowsUpdate,
    IReadOnlyList<string> CollectionWarnings);

/// <summary>The user's own words about why the report exists.</summary>
public sealed record CrashReportContextDto(
    CrashReasonCategory Category,
    string Description,
    DateTimeOffset? LastOccurrence);

/// <summary>A counted bucket in one of the report aggregations.</summary>
public sealed record CrashCountDto(string Key, int Count);

/// <summary>Facts only. Interpretation is left to a human or an external AI.</summary>
public sealed record CrashReportSummaryDto(
    int TotalEvents,
    int ApplicationCrashCount,
    int CriticalCount,
    int CorrelatedCount,
    bool HasBugCheck,
    bool HasHardwareEvents,
    bool HasUnexpectedShutdown,
    double CrashesPerDay,
    IReadOnlyList<CrashCountDto> CrashesByDay,
    IReadOnlyList<CrashCountDto> CrashesByHour,
    IReadOnlyList<CrashCountDto> TopModules,
    IReadOnlyList<CrashCountDto> TopExceptionCodes);

/// <summary>The complete report: context, machine, events and aggregations.</summary>
public sealed record WorkstationCrashReport(
    DateTimeOffset GeneratedAt,
    CrashReportContextDto Context,
    MachineProfileDto Machine,
    WorkstationCrashQuery Query,
    IReadOnlyList<WorkstationCrashEventDto> Events,
    CrashReportSummaryDto Summary);
