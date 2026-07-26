using System.Windows;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.ProjectWork;
using SiNet.Application.WorkSurfaces;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Browse: Legacy MainWindow content or NewShell content host.
/// Task: always <see cref="ProjectWorkTaskFloatingHost"/> (works for both shells).
/// </summary>
internal sealed class V2ProjectWorkSurfaceHost(
    ProjectWorkSurfaceHost newShellHost,
    ProjectWorkTaskFloatingHost taskFloatingHost) : IProjectWorkSurfaceHost
{
    private readonly ProjectWorkSurfaceHost _newShellHost =
        newShellHost ?? throw new ArgumentNullException(nameof(newShellHost));
    private readonly ProjectWorkTaskFloatingHost _taskFloatingHost =
        taskFloatingHost ?? throw new ArgumentNullException(nameof(taskFloatingHost));

    public async Task<bool> TryOpenBrowseAsync(CancellationToken cancellationToken = default)
    {
        if (Application.Current?.MainWindow is MainWindow main)
            return await main.TryOpenProjectWorkBrowseAsync(cancellationToken).ConfigureAwait(true);

        return await _newShellHost.TryOpenBrowseAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task<bool> TryOpenFromTaskAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        // #region agent log
        WorkflowDebugTrace.Step("ProjectWork.TaskWindow",
            $"V2Host.TryOpenFromTaskAsync task={context.TaskId} mainIsLegacyMainWindow={Application.Current?.MainWindow is MainWindow}");
        // #endregion

        // Close Inspection family before opening ProjectWork (plan D).
        if (Application.Current?.MainWindow is MainWindow legacyMain)
            legacyMain.CloseInspectionTaskWindows();
        else
            CloseInspectionWindows();

        return await _taskFloatingHost.OpenOrRebindAsync(context, cancellationToken).ConfigureAwait(true);
    }

    private static void CloseInspectionWindows()
    {
        var app = Application.Current;
        if (app is null)
            return;

        foreach (Window w in app.Windows.OfType<Window>().ToList())
        {
            if (w is SiNet.App.Wpf.Surfaces.Inspection.InspectionWindowView)
            {
                try { w.Close(); } catch { /* ignore */ }
            }
        }
    }
}
