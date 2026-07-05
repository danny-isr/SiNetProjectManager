namespace SiNet.Application.Tasks;

/// <summary>Per-queue repair breakdown returned by queue reindex operations.</summary>
public sealed record TaskQueueRepairDetail(
    int NullPrioritiesFixed,
    int DuplicatePrioritiesFixed,
    int GapsClosed,
    int StaleClosedCleared,
    int TotalCorrected);
