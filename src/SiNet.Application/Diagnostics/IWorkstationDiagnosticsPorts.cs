namespace SiNet.Application.Diagnostics;

/// <summary>
/// Reads crash-related entries from the <b>local</b> Windows Event Log (DEV-010).
/// Remote machines are out of scope: the app must run on the machine being investigated.
/// </summary>
public interface IWorkstationEventLogReader
{
    Task<IReadOnlyList<WorkstationCrashEventDto>> ReadAsync(
        WorkstationCrashQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Collects the hardware / OS profile of the local machine.</summary>
public interface IMachineProfileProvider
{
    Task<MachineProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);
}

/// <summary>Where a crash report was written, and what retention did afterwards.</summary>
public sealed record CrashReportSaveResult(
    string FolderPath,
    string CsvPath,
    string MarkdownPath,
    int DeletedReportCount,
    string? Warning);

/// <summary>
/// Persists crash reports. The share copy lands under <c>{share}\{MachineName}\</c> and triggers
/// retention for that machine's folder only.
/// </summary>
public interface IWorkstationCrashReportStore
{
    /// <summary>
    /// Resolves the configured share root: <c>Diagnostics.CrashReportSharePath</c>, or
    /// <c>{Logging.CentralLogPath}\CrashReports</c> when it is empty. Null when neither is set.
    /// </summary>
    Task<string?> ResolveShareRootAsync(CancellationToken cancellationToken = default);

    Task<CrashReportSaveResult> SaveToShareAsync(
        WorkstationCrashReport report,
        CancellationToken cancellationToken = default);

    /// <summary>Writes a single already-rendered file to an arbitrary path chosen by the user.</summary>
    Task SaveCopyAsync(string fullPath, string content, CancellationToken cancellationToken = default);
}
