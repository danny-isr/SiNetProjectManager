using System.Windows;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Process-wide floating window for ProjectWork <b>task</b> mode (MaterialChecklist / ProjectWork /
/// PoliceSubmission). Browse mode stays in the shell content area; task mode must never NavigateTo
/// the main shell — NewShell and Legacy both use this host.
/// </summary>
public sealed class ProjectWorkTaskFloatingHost(IProjectWorkWindowFactory factory)
{
    private readonly IProjectWorkWindowFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    private static readonly object Gate = new();
    private static Window? _window;
    private static ProjectWorkWindowView? _view;

    public async Task<bool> OpenOrRebindAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        Window? existingWindow;
        ProjectWorkWindowView? existingView;
        lock (Gate)
        {
            existingWindow = _window;
            existingView = _view;
        }

        if (existingWindow is { IsLoaded: true } && existingView is not null)
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
            TaskSurfaceWindowLayout.ApplyComplementaryToWorkbench(existingWindow);
            existingWindow.Activate();
            return true;
        }

        var surface = _factory.Create();
        var opened = await surface.ApplyContextAsync(context, cancellationToken).ConfigureAwait(true);
        if (!opened)
        {
            surface.Dispose();
            return false;
        }

        surface.ViewModel.CloseRequested += CloseIfOpen;

        var owner = System.Windows.Application.Current?.MainWindow;
        var host = new Window
        {
            Title = "ביצוע משימה — סביבת עבודה",
            Owner = owner,
            Content = surface,
            MinWidth = 720,
            MinHeight = 480,
            FlowDirection = FlowDirection.RightToLeft,
            ShowInTaskbar = true,
            Topmost = true,
        };
        TaskSurfaceWindowLayout.ApplyComplementaryToWorkbench(host);

        host.Closed += (_, _) =>
        {
            surface.ViewModel.CloseRequested -= CloseIfOpen;
            lock (Gate)
            {
                if (ReferenceEquals(_window, host))
                {
                    _window = null;
                    _view = null;
                }
            }

            surface.Dispose();
        };

        lock (Gate)
        {
            _window = host;
            _view = surface;
        }

        // #region agent log
        WorkflowDebugTrace.Step("ProjectWork.TaskWindow",
            $"create-float task={context.TaskId} project={context.ProjectId} topmost=True");
        // #endregion
        host.Show();
        host.Activate();
        return true;
    }

    public void CloseIfOpen()
    {
        Window? win;
        lock (Gate)
            win = _window;

        if (win is not { IsLoaded: true })
            return;

        try { win.Close(); } catch { /* already closing */ }
    }
}
