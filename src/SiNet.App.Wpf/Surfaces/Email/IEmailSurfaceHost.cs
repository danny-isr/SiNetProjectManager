using System.Windows;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Shell-owned singleton host for the New System email inbox surface. Creates the surface once,
/// keeps it in memory when the user navigates away, and re-shows it in the main shell content area
/// (legacy <c>MainWindow._cachedEmailManagementView</c> pattern).
/// </summary>
public interface IEmailSurfaceHost
{
    /// <summary>
    /// Shows the cached email surface inside the main shell. Creates it on first use.
    /// When <paramref name="context"/> is provided, applies that task/work-surface context.
    /// When omitted (<see langword="null"/>), resets the inbox to browse default (menu «מיילים»).
    /// </summary>
    void Show(WorkSurfaceContext? context = null);

    /// <summary>The live inbox view model when the surface has been created; otherwise null.</summary>
    EmailWindowViewModel? TryGetViewModel();

    /// <summary>
    /// Returns <see langword="true"/> when the shell should cancel closing because email background
    /// work is still running (ACC uploads, etc.).
    /// </summary>
    bool TryBlockShellClose(Window owner);
}
