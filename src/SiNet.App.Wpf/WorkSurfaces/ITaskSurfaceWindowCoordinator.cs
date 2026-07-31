using System.Windows;

namespace SiNet.App.Wpf.WorkSurfaces;

/// <summary>
/// Process-wide gate: at most one task work surface open (SOF-009).
/// Extends <see cref="ITaskFamilyWindowGate"/> for legacy close-ProjectWork callers.
/// </summary>
public interface ITaskSurfaceWindowCoordinator : ITaskFamilyWindowGate
{
    /// <summary>
    /// Prepares to open a surface of <paramref name="kind"/>.
    /// Same kind already open → returns that window (caller Activate / rebind).
    /// Other kind open → closes it and returns null (caller creates).
    /// </summary>
    Window? PrepareOpen(TaskSurfaceWindowKind kind, int? taskId);

    /// <summary>Registers the active surface; clears automatically when the window closes.</summary>
    void RegisterActive(Window window, TaskSurfaceWindowKind kind, int? taskId);

    /// <summary>True when a surface of <paramref name="kind"/> is the current active window.</summary>
    bool IsActiveKind(TaskSurfaceWindowKind kind);
}
