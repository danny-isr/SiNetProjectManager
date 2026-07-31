using System.Windows;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Shared geometry for task-driven work surfaces that sit beside the Tasks workbench
/// (complementary WorkArea minus the right-hand workbench strip). See docs/APP_SHELL.md §10.1.
/// </summary>
public static class TaskSurfaceWindowLayout
{
    /// <summary>
    /// Computes the complementary bounds left of a reserved right strip inside <paramref name="workArea"/>.
    /// </summary>
    public static Rect ComputeComplementaryBounds(Rect workArea, double reservedRightWidth, double minWidth)
    {
        var reserved = Math.Max(0, reservedRightWidth);
        var width = Math.Max(Math.Max(0, minWidth), workArea.Width - reserved);
        return new Rect(workArea.Left, workArea.Top, width, workArea.Height);
    }

    /// <summary>
    /// Width to reserve on the right for the Tasks workbench (live window when open, else default).
    /// </summary>
    public static double ResolveReservedRightStripWidth()
    {
        if (System.Windows.Application.Current?.Windows is { } windows)
        {
            foreach (Window window in windows)
            {
                if (window is not TaskWorkbenchView { IsLoaded: true, IsVisible: true } workbench)
                    continue;

                var live = workbench.ActualWidth;
                if (live > 0)
                    return live;

                return TaskWorkbenchView.DefaultNarrowWidth;
            }
        }

        return TaskWorkbenchView.DefaultNarrowWidth;
    }

    /// <summary>
    /// Places <paramref name="window"/> in the complementary WorkArea left of the workbench strip.
    /// </summary>
    public static void ApplyComplementaryToWorkbench(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var minWidth = window.MinWidth > 0 ? window.MinWidth : 320;
        var bounds = ComputeComplementaryBounds(
            SystemParameters.WorkArea,
            ResolveReservedRightStripWidth(),
            minWidth);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
    }

    /// <summary>
    /// Complementary geometry + shell ownership for a task-driven surface (non-Topmost).
    /// Call before <c>Show</c> / <c>ShowDialog</c>; optionally call again on <c>Loaded</c>.
    /// </summary>
    public static void PrepareTaskSurfaceWindow(Window window, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Owner = owner ?? System.Windows.Application.Current?.MainWindow;
        window.Topmost = false;
        window.ShowInTaskbar = true;
        ApplyComplementaryToWorkbench(window);
    }
}
