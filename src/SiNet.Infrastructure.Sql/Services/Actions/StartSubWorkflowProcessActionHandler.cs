using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>
/// Native handler for <see cref="ProcessActionCodes.StartSubWorkflow"/>. Starts the sub-workflow
/// attached to the transition's target stage (<see cref="ActionExecutionDataKeys.ToStageId"/>) as a
/// child of the current instance, enforcing the open-child-instance cap, then provisions the child's
/// initial-stage tasks. Re-homed from the legacy
/// <c>SiNetSQL.Domain.Actions.Handlers.StartSubWorkflowProcessActionHandler</c>.
/// <para>
/// When invoked from atomic auto-advance, must enlist in the ambient
/// <see cref="SiNetSQLDbContext"/> — a separate context deadlocks / times out against the parent
/// row held by the same transaction (SOF-020).
/// </para>
/// </summary>
internal sealed class StartSubWorkflowProcessActionHandler : IProcessActionHandler
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly WorkflowEngine _engine;
    private readonly WorkflowStageTaskProvisioningService _provisioning;
    private readonly ISystemSettingsQueryService? _systemSettings;

    public StartSubWorkflowProcessActionHandler(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        WorkflowEngine engine,
        WorkflowStageTaskProvisioningService provisioning,
        ISystemSettingsQueryService? systemSettings = null)
    {
        _dbFactory = dbFactory;
        _engine = engine;
        _provisioning = provisioning;
        _systemSettings = systemSettings;
    }

    public string ActionCode => ProcessActionCodes.StartSubWorkflow;

    public async ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        var instanceId = command.WorkflowInstanceId ?? 0;
        if (instanceId <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "WorkflowInstanceId is required.");

        var toStageId = WorkflowActionHelpers.ReadDataInt(command, ActionExecutionDataKeys.ToStageId);
        if (toStageId is null or <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "ToStageId is required.");

        var (db, owns) = await WorkflowActionHelpers.ResolveDbContextAsync(command, _dbFactory, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var targetStage = await db.WorkflowStageDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == toStageId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (targetStage?.SubWorkflowDefinitionId is null)
                return ActionExecutionResultDto.Failed(ActionCode, $"Target stage {toStageId.Value} is not linked to a sub-workflow.");

            var parentInstance = await db.WorkflowInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
                .ConfigureAwait(false);

            if (parentInstance is null)
                return ActionExecutionResultDto.Failed(ActionCode, $"Workflow instance {instanceId} not found.");

            var maxOpen = await WorkflowOpenChildInstanceCap.ResolveMaxAsync(_systemSettings, cancellationToken).ConfigureAwait(false);
            var (allowed, _, _, blockMessage) = await WorkflowOpenChildInstanceCap.TryAllowStartAsync(
                db,
                parentInstance.ProjectId,
                targetStage.SubWorkflowDefinitionId.Value,
                maxOpen,
                cancellationToken).ConfigureAwait(false);

            if (!allowed)
                return ActionExecutionResultDto.Failed(ActionCode, blockMessage!);

            try
            {
                var subInstance = await _engine.StartAsync(
                    db,
                    targetStage.SubWorkflowDefinitionId.Value,
                    parentInstance.ProjectId,
                    parentInstance.TriggerType,
                    triggerEntityId: parentInstance.TriggerEntityId,
                    command.UserId ?? 0,
                    notes: $"תת-תהליך שהופעל מ-Workflow {instanceId}, שלב {toStageId.Value}",
                    cancellationToken,
                    parentWorkflowInstanceId: parentInstance.Id).ConfigureAwait(false);

                var (advancedInstance, tasks) = await _provisioning.EnsureInitialStageTasksAsync(
                    db, subInstance, command.UserId ?? 0, cancellationToken).ConfigureAwait(false);

                Trace.TraceInformation(
                    $"[StartSubWorkflow] Started sub-workflow {advancedInstance.Id} (def={targetStage.SubWorkflowDefinitionId}) from instance {instanceId}; provisioned {tasks.Count} tasks at stage {advancedInstance.CurrentStageId}.");

                return ActionExecutionResultDto.Completed(ActionCode, $"Sub-workflow {advancedInstance.Id} started.");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[StartSubWorkflow] failed to start sub-workflow from instance {instanceId}: {ex}");
                return ActionExecutionResultDto.Failed(ActionCode, $"Failed to start sub-workflow: {FlattenExceptionMessage(ex)}");
            }
        }
        finally
        {
            if (owns)
                await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string FlattenExceptionMessage(Exception ex)
    {
        var sb = new StringBuilder(ex.Message);
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            sb.Append(" | ").Append(inner.Message);
        }

        return sb.ToString();
    }
}
