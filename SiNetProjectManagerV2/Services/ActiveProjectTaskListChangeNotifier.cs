using SiNet.Application.Tasks;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Bridges native <see cref="ITaskCompletionService"/> completions to:
/// <list type="bullet">
/// <item>legacy floating-task refresh (<see cref="ActiveProjectContext.NotifyTaskDataChanged"/>)</item>
/// <item>New System listeners via <see cref="ITaskListChangeNotifier.TaskListChanged"/></item>
/// </list>
/// </summary>
internal sealed class ActiveProjectTaskListChangeNotifier : ITaskListChangeNotifier
{
    public event Action? TaskListChanged;

    public void NotifyTaskListChanged()
    {
        ActiveProjectContext.Instance.NotifyTaskDataChanged();
        TaskListChanged?.Invoke();
    }
}
