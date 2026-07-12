using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>
/// Opens the native closed-world workflow viewer (App.Wpf surface — no V2 Dialogs).
/// </summary>
public interface IWorkflowClosedViewerWindowFactory
{
    Window Create();
}

/// <summary>
/// Default factory: resolves a transient <see cref="WorkflowClosedViewerViewModel"/> and binds
/// a new <see cref="WorkflowClosedViewerWindow"/>.
/// </summary>
public sealed class WorkflowClosedViewerWindowFactory(IServiceProvider services) : IWorkflowClosedViewerWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public Window Create()
    {
        var viewModel = _services.GetRequiredService<WorkflowClosedViewerViewModel>();
        return new WorkflowClosedViewerWindow(viewModel);
    }
}
