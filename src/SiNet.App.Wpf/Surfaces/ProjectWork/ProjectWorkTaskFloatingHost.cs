using System.Windows;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.WorkSurfaces;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Process-wide floating window for ProjectWork <b>task</b> mode (MaterialChecklist / ProjectWork /
/// PoliceSubmission). Browse mode stays in the shell content area; task mode must never NavigateTo
/// the main shell — NewShell and Legacy both use this host.
/// </summary>
public sealed class ProjectWorkTaskFloatingHost(
    IProjectWorkWindowFactory factory,
    ITaskSurfaceWindowCoordinator coordinator)
{
    private readonly IProjectWorkWindowFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ITaskSurfaceWindowCoordinator _coordinator =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    private readonly object _gate = new();
    private Window? _window;
    private ProjectWorkWindowView? _view;

    public async Task<bool> OpenOrRebindAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var prepared = _coordinator.PrepareOpen(TaskSurfaceWindowKind.ProjectWork, context.TaskId);
        if (prepared is { IsLoaded: true } existingWindow)
        {
            ProjectWorkWindowView? existingView = existingWindow.Content as ProjectWorkWindowView;
            lock (_gate)
            {
                if (existingView is null && ReferenceEquals(_window, existingWindow))
                    existingView = _view;
                if (existingView is not null)
                {
                    _window = existingWindow;
                    _view = existingView;
                }
            }

            if (existingView is not null)
            {
                // #region agent log
                WorkflowDebugTrace.Step("ProjectWork.TaskWindow",
                    $"rebind task={context.TaskId} project={context.ProjectId}");
                // #endregion
                existingView.ViewModel.CloseRequested -= CloseIfOpen;
                existingView.ViewModel.CloseRequested += CloseIfOpen;
                var rebound = await existingView.ApplyContextAsync(context, cancellationToken).ConfigureAwait(true);
                if (!rebound)
                    return false;
                if (existingWindow.WindowState == WindowState.Minimized)
                    existingWindow.WindowState = WindowState.Normal;
                TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(existingWindow);
                existingWindow.Activate();
                return true;
            }
        }

        var surface = _factory.Create();
        var opened = await surface.ApplyContextAsync(context, cancellationToken).ConfigureAwait(true);
        if (!opened)
        {
            surface.Dispose();
            return false;
        }

        surface.ViewModel.CloseRequested += CloseIfOpen;

        var host = new Window
        {
            Title = "ביצוע משימה — סביבת עבודה",
            Content = surface,
            MinWidth = 720,
            MinHeight = 480,
            FlowDirection = FlowDirection.RightToLeft,
        };
        TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(host);

        host.Closed += (_, _) =>
        {
            surface.ViewModel.CloseRequested -= CloseIfOpen;
            lock (_gate)
            {
                if (ReferenceEquals(_window, host))
                {
                    _window = null;
                    _view = null;
                }
            }

            surface.Dispose();
        };

        lock (_gate)
        {
            _window = host;
            _view = surface;
        }

        _coordinator.RegisterActive(host, TaskSurfaceWindowKind.ProjectWork, context.TaskId);

        // #region agent log
        WorkflowDebugTrace.Step("ProjectWork.TaskWindow",
            $"create-float task={context.TaskId} project={context.ProjectId} topmost=False owner=MainWindow");
        // #endregion
        host.Show();
        host.Activate();
        return true;
    }

    public void CloseIfOpen()
    {
        Window? win;
        lock (_gate)
            win = _window;

        if (win is not { IsLoaded: true })
            return;

        try { win.Close(); } catch { /* already closing */ }
    }
}
