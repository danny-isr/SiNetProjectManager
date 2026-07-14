namespace SiNet.Application.Runtime;

/// <summary>
/// Optional bridge to legacy <c>ISystemHealthService</c> snapshots without referencing SiNetSQL from App.Wpf.
/// Host (V2) registers an adapter; when missing, the aggregator skips external health rows.
/// </summary>
public interface IExternalHealthCheckSource
{
    IReadOnlyList<ExternalHealthCheckSnapshot> Current { get; }

    event EventHandler? Changed;

    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Normalized health row from the legacy health stack.</summary>
public sealed record ExternalHealthCheckSnapshot(
    string Key,
    string DisplayNameHe,
    SubsystemRuntimeState State,
    string SummaryHe,
    DateTimeOffset? LastCheckedUtc);
