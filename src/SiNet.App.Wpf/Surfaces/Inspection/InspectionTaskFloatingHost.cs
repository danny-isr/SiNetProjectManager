using System.Windows;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.WorkSurfaces;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Process-wide floating Inspection task window. Singleton + rebind (SOF-009).
/// </summary>
public sealed class InspectionTaskFloatingHost(
    IInspectionWindowFactory factory,
    ITaskSurfaceWindowCoordinator coordinator)
{
    private readonly IInspectionWindowFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ITaskSurfaceWindowCoordinator _coordinator =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public async Task<bool> OpenOrRebindAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var existing = _coordinator.PrepareOpen(TaskSurfaceWindowKind.Inspection, context.TaskId);
        if (existing is InspectionWindowView inspectionWindow)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "Inspection.TaskWindow",
                $"rebind task={context.TaskId} report={context.PrimaryWorkTargetEntityId}");
            // #endregion
            var rebound = await inspectionWindow
                .ApplyContextAsync(context, cancellationToken)
                .ConfigureAwait(true);
            if (!rebound)
                return false;

            TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(inspectionWindow);
            if (inspectionWindow.WindowState == WindowState.Minimized)
                inspectionWindow.WindowState = WindowState.Normal;
            inspectionWindow.Activate();
            return true;
        }

        var window = _factory.Create();
        var opened = await window.ApplyContextAsync(context, cancellationToken).ConfigureAwait(true);
        if (!opened)
            return false;

        TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(window);
        _coordinator.RegisterActive(window, TaskSurfaceWindowKind.Inspection, context.TaskId);

        // #region agent log
        WorkflowDebugTrace.Step(
            "Inspection.TaskWindow",
            $"create-float task={context.TaskId} report={context.PrimaryWorkTargetEntityId}");
        // #endregion
        window.Show();
        window.Activate();
        return true;
    }
}
