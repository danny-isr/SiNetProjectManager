using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.LegacyBridge.Tasks;

/// <summary>
/// Strangler adapter that implements the new <see cref="ITaskNavigationService"/> Application port by
/// delegating to the legacy-host <see cref="ILegacyTaskNavigationSource"/> seam. It maps the
/// bridge-local <see cref="LegacyTaskNavigationRequestDto"/> onto the Application
/// <see cref="WorkSurfaceContext"/>.
/// <para>
/// The seam is optional: when no host binds it (the new <c>SiNet.App.Wpf</c> shell during early
/// migration), the adapter returns <see langword="null"/> so the work-surface caller can show a clear
/// "cannot open from task yet" message instead of guessing a target. The legacy WPF host supplies a
/// real source backed by <c>TaskNavigationResolver</c>. Replace this with a native infrastructure
/// implementation once task navigation is fully migrated.
/// </para>
/// </summary>
internal sealed class LegacyTaskNavigationService : ITaskNavigationService
{
    private readonly ILegacyTaskNavigationSource? _source;

    public LegacyTaskNavigationService(ILegacyTaskNavigationSource? source = null)
    {
        _source = source;
    }

    public async ValueTask<WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct)
    {
        if (_source is null)
        {
            return null;
        }

        var request = await _source.ResolveAsync(taskId, ct).ConfigureAwait(false);

        // No task at all, or the legacy resolver could not open it (unknown type, no interaction
        // definition, missing assignee/group). Per the architecture rules the caller must surface a
        // clear error rather than fall back to an arbitrary work target.
        if (request is null || !request.IsSuccess)
        {
            return null;
        }

        return new WorkSurfaceContext(
            TaskId: request.TaskId,
            ProjectId: request.ProjectId ?? 0,
            WorkflowInstanceId: request.WorkflowInstanceId,
            ComponentKey: request.ComponentKey,
            PrimaryWorkTargetEntityId: ToInt32(request.PrimaryWorkTargetEntityId),
            AllowedResultCodes: request.AllowedTaskResultCodes,
            CompletionEventCode: request.CompletionEventCode,
            ActingUserId: request.ActingUserId);
    }

    // Legacy work-target ids are long; the new context exposes int? to match the EF entity keys the
    // work surfaces load. Values outside int range are treated as "no concrete target" so a malformed
    // id can never be silently truncated into a different valid id.
    private static int? ToInt32(long? value)
        => value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
}
