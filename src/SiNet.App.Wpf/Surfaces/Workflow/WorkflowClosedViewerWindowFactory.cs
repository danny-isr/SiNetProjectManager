using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>
/// Opens the native closed-world workflow visual canvas (App.Wpf — no V2 Dialogs).
/// </summary>
public interface IWorkflowClosedViewerWindowFactory
{
    Window Create();
}

/// <summary>
/// Default factory: visual canvas V1 (replaces tree viewer entry in New Shell).
/// </summary>
public sealed class WorkflowClosedViewerWindowFactory(IServiceProvider services) : IWorkflowClosedViewerWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public Window Create()
    {
        var viewModel = _services.GetRequiredService<WorkflowVisualCanvasViewModel>();
        return new WorkflowVisualCanvasWindow(viewModel);
    }
}
