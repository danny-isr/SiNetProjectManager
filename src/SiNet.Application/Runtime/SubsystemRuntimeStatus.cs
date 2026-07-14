namespace SiNet.Application.Runtime;

/// <summary>One subsystem row for shell status / System Status window.</summary>
public sealed record SubsystemRuntimeStatus(
    string Key,
    string DisplayNameHe,
    SubsystemRuntimeState State,
    int? ActiveWorkCount,
    string SummaryHe,
    DateTimeOffset? LastCheckedUtc);
