using SiNet.Application.WorkSurfaces;

namespace SiNet.Application.Tasks;

/// <summary>
/// Resolves a <c>taskId</c> into the explicit <see cref="WorkSurfaceContext"/> that a Work Surface
/// needs in order to open the <b>exact</b> work target. This is the only sanctioned way for the UI
/// to turn a task into a navigation target — screens must not hand-build the context or guess the
/// target (see <c>docs/AI_DEVELOPMENT_GUIDE.md</c> §2 rule 12 and §3).
/// </summary>
public interface ITaskNavigationService
{
    /// <summary>
    /// Resolves the work-surface context for <paramref name="taskId"/>, or <see langword="null"/>
    /// when the task cannot be opened (unknown task, no interaction definition, missing
    /// assignee/group, or no host bound the underlying resolver). Callers must show a clear error
    /// rather than falling back to an arbitrary work target.
    /// </summary>
    ValueTask<WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct);
}
