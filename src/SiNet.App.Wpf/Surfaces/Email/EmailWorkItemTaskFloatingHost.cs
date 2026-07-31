using System.Windows;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.WorkSurfaces;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Process-wide floating Email work-item window (task filing). Singleton + rebind (SOF-009).
/// </summary>
public sealed class EmailWorkItemTaskFloatingHost(
    IEmailWorkItemWindowFactory factory,
    ITaskSurfaceWindowCoordinator coordinator)
{
    private readonly IEmailWorkItemWindowFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ITaskSurfaceWindowCoordinator _coordinator =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public bool OpenOrRebind(WorkSurfaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var existing = _coordinator.PrepareOpen(TaskSurfaceWindowKind.EmailWorkItem, context.TaskId);
        if (existing is EmailWorkItemWindow emailWindow)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TaskWindow",
                $"rebind task={context.TaskId} email={context.PrimaryWorkTargetEntityId}");
            // #endregion
            emailWindow.ApplyContext(context);
            TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(emailWindow);
            if (emailWindow.WindowState == WindowState.Minimized)
                emailWindow.WindowState = WindowState.Normal;
            emailWindow.Activate();
            return true;
        }

        var window = _factory.Create();
        window.ApplyContext(context);
        TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(window);
        _coordinator.RegisterActive(window, TaskSurfaceWindowKind.EmailWorkItem, context.TaskId);

        // #region agent log
        WorkflowDebugTrace.Step(
            "Email.TaskWindow",
            $"create-float task={context.TaskId} email={context.PrimaryWorkTargetEntityId}");
        // #endregion
        window.Show();
        window.Activate();
        return true;
    }
}
