namespace SiNet.Application.MasterPlan;

/// <summary>
/// Native MasterPlan ↔ SiNet company/contact mapping (Replica MP_* + SiData).
/// See <c>docs/MASTER_PLAN_MIGRATION.md</c> S2.
/// </summary>
public interface IMasterPlanMappingService
{
    Task<MasterPlanMappingLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<MasterPlanMappingApplyResult> ApplyAsync(
        MasterPlanMappingApplyCommand command,
        CancellationToken cancellationToken = default);

    Task<MasterPlanCompleteMissingResult> CompleteMissingAsync(
        CancellationToken cancellationToken = default);

    Task<MasterPlanEnableFullSyncResult> EnableFullSyncAsync(
        CancellationToken cancellationToken = default);
}
