namespace SiNet.Application.Tasks;

/// <summary>
/// Write port for creating/updating/closing tasks when the change is <b>not</b> part of the
/// completion-driven workflow path (which goes through <see cref="ITaskCompletionService"/>).
/// <para>
/// Declared now as part of the Workflow-first backbone (see <c>docs/ARCHITECTURE_TARGET.md</c> §4).
/// Intentionally empty until the first command use-case is connected — kept as a named seam so
/// DI/registration and call sites are stable. UI must not mutate workflow state through this port;
/// only <see cref="Workflow.IWorkflowCommandService"/> may advance workflow.
/// </para>
/// </summary>
public interface ITaskCommandService
{
}
