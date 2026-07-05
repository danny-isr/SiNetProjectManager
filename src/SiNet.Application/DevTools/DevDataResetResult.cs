namespace SiNet.Application.DevTools;

/// <summary>Report returned after a development-database reset.</summary>
public sealed class DevDataResetResult
{
    public string WindowsUser { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
    public IReadOnlyList<DevDataResetTableResult> Tables { get; init; } = [];
    public string? PostResetError { get; init; }
    public bool SeedApplied { get; init; }
    public string? SeedError { get; init; }
    public bool MappingsApplied { get; init; }
    public string? MappingsError { get; init; }
    public bool WorkflowSeedApplied { get; init; }
    public string? WorkflowSeedError { get; init; }
    public bool DemoTasksSeedApplied { get; init; }
    public string? DemoTasksSeedError { get; init; }
    public bool SystemSettingsPreserved { get; init; }
    public bool UserSettingsPreserved { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public int TotalRowsDeleted => Tables.Sum(t => t.RowsDeleted);
    public int FailedTableCount => Tables.Count(t => t.Error is not null);
    public TimeSpan Duration => CompletedUtc - StartedUtc;
}
