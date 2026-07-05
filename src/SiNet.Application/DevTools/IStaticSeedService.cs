namespace SiNet.Application.DevTools;

/// <summary>
/// Development seed operations for static lookups, mappings, workflows, and demo tasks.
/// </summary>
public interface IStaticSeedService
{
    /// <summary>Clears the in-process static seed session flag (after a destructive reset).</summary>
    void ResetSeedingSessionFlag();

    ValueTask<SeedResult> SeedTaskStaticLookupsAsync(CancellationToken ct = default);
    ValueTask<SeedResult> SeedTaskMappingsAsync(CancellationToken ct = default);
    ValueTask<SeedResult> SeedWorkflowDefinitionsAsync(CancellationToken ct = default);
    ValueTask<SeedResult> SeedDemoTasksAsync(DemoTaskSeedOptions? options = null, CancellationToken ct = default);

    /// <summary>Runs static lookups, mappings, and workflow seed in canonical order.</summary>
    ValueTask<SeedResult> SeedAllCoreAsync(CancellationToken ct = default);
}
