namespace SiNet.Application.Tasks;

/// <summary>
/// Read port for task queues, project task lists, and task detail.
/// <para>
/// Declared now as part of the Workflow-first backbone (see <c>docs/ARCHITECTURE_TARGET.md</c> §4)
/// so consumers can depend on the port shape while the concrete read implementation is migrated in a
/// later slice. Intentionally empty until the first read use-case is connected — kept as a named
/// seam so DI/registration and call sites are stable.
/// </para>
/// </summary>
public interface ITaskQueryService
{
}
