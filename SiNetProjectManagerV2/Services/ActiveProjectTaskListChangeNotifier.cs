using SiNet.Application.Tasks;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Bridges native <see cref="ITaskCompletionService"/> completions to the legacy floating-task
/// refresh event (<see cref="ActiveProjectContext.NotifyTaskDataChanged"/>).
/// </summary>
internal sealed class ActiveProjectTaskListChangeNotifier : ITaskListChangeNotifier
{
    public void NotifyTaskListChanged()
        => ActiveProjectContext.Instance.NotifyTaskDataChanged();
}
