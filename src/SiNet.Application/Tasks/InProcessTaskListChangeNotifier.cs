namespace SiNet.Application.Tasks;

/// <summary>
/// Default in-process <see cref="ITaskListChangeNotifier"/> for hosts that do not bridge
/// to legacy <c>ActiveProjectContext</c> (e.g. standalone <c>SiNet.App.Wpf</c>).
/// </summary>
public sealed class InProcessTaskListChangeNotifier : ITaskListChangeNotifier
{
    public event Action? TaskListChanged;

    public void NotifyTaskListChanged() => TaskListChanged?.Invoke();
}
