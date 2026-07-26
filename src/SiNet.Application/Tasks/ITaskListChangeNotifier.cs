namespace SiNet.Application.Tasks;

/// <summary>
/// Host-side signal that task list UIs (floating panel, task board) should reload.
/// Native completion goes through <see cref="ITaskCompletionService"/> and does not touch
/// legacy <c>ActiveProjectContext</c>; the V2 host registers an adapter that bridges the two.
/// </summary>
public interface ITaskListChangeNotifier
{
    void NotifyTaskListChanged();
}
