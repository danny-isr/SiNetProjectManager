namespace SiNet.Application.Tasks;

/// <summary>
/// Host-side signal that task list UIs (floating panel, task workbench) should reload.
/// Native completion goes through <see cref="ITaskCompletionService"/> and does not touch
/// legacy <c>ActiveProjectContext</c>.
/// <para>
/// Default registration is <see cref="InProcessTaskListChangeNotifier"/> (standalone New System).
/// The V2 host may replace it with an adapter that also bridges
/// <c>ActiveProjectContext.NotifyTaskDataChanged</c>.
/// </para>
/// <para>
/// <see cref="NotifyTaskListChanged"/> is raised on the machine that mutated tasks.
/// Subscribers should also poll periodically so lists stay fresh when another user
/// creates or closes tasks on a different client.
/// </para>
/// </summary>
public interface ITaskListChangeNotifier
{
    /// <summary>Raised after <see cref="NotifyTaskListChanged"/> (any thread).</summary>
    event Action? TaskListChanged;

    void NotifyTaskListChanged();
}
