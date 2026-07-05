using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Default factory for <see cref="TaskPanelReadOnlyView"/>.
/// </summary>
public sealed class TaskPanelReadOnlyWindowFactory(IServiceProvider services) : ITaskPanelReadOnlyWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public TaskPanelReadOnlyView Create()
    {
        var viewModel = _services.GetRequiredService<TaskPanelReadOnlyViewModel>();
        return new TaskPanelReadOnlyView(viewModel);
    }
}
