using SiNet.LegacyBridge.Tasks;
using SiNetSQL.Services.Tasks;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Binds the new <see cref="ILegacyTaskNavigationSource"/> strangler seam to the existing legacy
/// read-only <see cref="TaskNavigationResolver"/>.
/// <para>
/// This is the single place that knows both worlds for task navigation: it calls the legacy
/// resolver and projects the rich <see cref="TaskNavigationRequest"/> down to the bridge-local
/// <see cref="LegacyTaskNavigationRequestDto"/> (no <c>SiNetSQL</c> type crosses the boundary).
/// It is the host-side fulfilment that lets a real workflow-created task open the new Inspection
/// Work Surface through the official path
/// (<c>ITaskNavigationService</c> → seam → <c>TaskNavigationResolver</c> → <c>WorkSurfaceContext</c>).
/// </para>
/// <para>
/// <b>Strict, no-guessing semantics (matches the resolver's authority boundary):</b>
/// the resolver is read-only and never mutates assignment/ownership. This adapter forwards its
/// decision faithfully:
/// <list type="bullet">
/// <item>If the resolver reports a failure (unknown TaskType, missing interaction definition,
/// no assignee/group), the projected DTO carries <see cref="LegacyTaskNavigationRequestDto.IsSuccess"/>
/// = <see langword="false"/> plus the resolver's failure message. The downstream
/// <c>LegacyTaskNavigationService</c> turns that into a <see langword="null"/> context, so the
/// surface shows a clear error rather than falling back to an arbitrary report.</item>
/// <item>If the task has no concrete work target, <see cref="LegacyTaskNavigationRequestDto.PrimaryWorkTargetEntityId"/>
/// stays <see langword="null"/>; no first/last report is invented here.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class TaskNavigationLegacySource : ILegacyTaskNavigationSource
{
    private readonly TaskNavigationResolver _resolver;

    public TaskNavigationLegacySource(TaskNavigationResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async ValueTask<LegacyTaskNavigationRequestDto?> ResolveAsync(
        int taskId,
        CancellationToken cancellationToken = default)
    {
        var request = await _resolver
            .ResolveAsync(taskId, cancellationToken)
            .ConfigureAwait(false);

        // Defensive only: the resolver returns a (possibly failure-flagged) request rather than null.
        // We never synthesise a success here.
        if (request is null)
        {
            return null;
        }

        return new LegacyTaskNavigationRequestDto(
            TaskId: request.TaskId,
            ProjectId: request.ProjectId,
            WorkflowInstanceId: request.WorkflowInstanceId,
            ComponentKey: request.ComponentKey,
            PrimaryWorkTargetEntityId: request.PrimaryWorkTargetEntityId,
            AllowedTaskResultCodes: request.AllowedTaskResultCodes,
            IsSuccess: request.IsSuccess,
            FailureMessage: request.FailureMessage);
    }
}
