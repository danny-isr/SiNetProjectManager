namespace SiNet.Application.Runtime;

/// <summary>One subsystem row for shell status / System Status window.</summary>
/// <param name="GuidanceHe">
/// Optional Hebrew remediation under the summary; null/empty when no known operator action.
/// </param>
public sealed record SubsystemRuntimeStatus(
    string Key,
    string DisplayNameHe,
    SubsystemRuntimeState State,
    int? ActiveWorkCount,
    string SummaryHe,
    DateTimeOffset? LastCheckedUtc,
    string? GuidanceHe = null);
