using System.Windows;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG

namespace SiNet.App.Wpf.WorkSurfaces;

/// <inheritdoc cref="ITaskSurfaceWindowCoordinator" />
public sealed class TaskSurfaceWindowCoordinator : ITaskSurfaceWindowCoordinator
{
    private readonly object _gate = new();
    private Window? _active;
    private TaskSurfaceWindowKind _kind;
    private int? _taskId;

    /// <inheritdoc />
    public Window? PrepareOpen(TaskSurfaceWindowKind kind, int? taskId)
    {
        Window? toClose = null;
        Window? reuse = null;

        lock (_gate)
        {
            if (_active is { } existing)
            {
                if (_kind == kind)
                {
                    _taskId = taskId;
                    reuse = existing;
                }
                else
                {
                    toClose = existing;
                    ClearUnlocked();
                }
            }
        }

        if (toClose is not null)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "TaskSurface.Coordinator",
                $"PrepareOpen close-other kind={kind} task={taskId?.ToString() ?? "null"}");
            // #endregion
            CloseQuietly(toClose);
        }

        return reuse;
    }

    /// <inheritdoc />
    public void RegisterActive(Window window, TaskSurfaceWindowKind kind, int? taskId)
    {
        ArgumentNullException.ThrowIfNull(window);

        Window? previous = null;
        lock (_gate)
        {
            if (_active is { } current && !ReferenceEquals(current, window))
                previous = current;

            _active = window;
            _kind = kind;
            _taskId = taskId;
            window.Closed -= OnActiveClosed;
            window.Closed += OnActiveClosed;
        }

        if (previous is not null)
            CloseQuietly(previous);

        // #region agent log
        WorkflowDebugTrace.Step(
            "TaskSurface.Coordinator",
            $"RegisterActive kind={kind} task={taskId?.ToString() ?? "null"}");
        // #endregion
    }

    /// <inheritdoc />
    public bool IsActiveKind(TaskSurfaceWindowKind kind)
    {
        lock (_gate)
            return _active is not null && _kind == kind;
    }

    /// <inheritdoc />
    public void CloseProjectWorkTaskWindows()
    {
        Window? toClose = null;
        lock (_gate)
        {
            if (_active is not null && _kind == TaskSurfaceWindowKind.ProjectWork)
            {
                toClose = _active;
                ClearUnlocked();
            }
        }

        if (toClose is not null)
            CloseQuietly(toClose);
    }

    private void OnActiveClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
            window.Closed -= OnActiveClosed;

        lock (_gate)
        {
            if (ReferenceEquals(_active, sender))
                ClearUnlocked();
        }
    }

    private void ClearUnlocked()
    {
        _active = null;
        _taskId = null;
    }

    private static void CloseQuietly(Window window)
    {
        try
        {
            window.Close();
        }
        catch
        {
            // already closing
        }
    }
}
