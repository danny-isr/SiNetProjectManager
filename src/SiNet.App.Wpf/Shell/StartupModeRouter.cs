namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Pure, WPF-free routing helper that turns the "run new system" choice from the first
/// login/user-selection window into a <see cref="StartupMode"/> (see <c>docs/APP_SHELL.md</c> §3).
/// <para>
/// This exists so the mode decision is unit-testable without constructing WPF windows: the host
/// captures the checkbox value and calls <see cref="Resolve(bool)"/>, then branches on the returned
/// <see cref="StartupMode"/> to open either the legacy main window or <see cref="NewShellWindow"/>.
/// </para>
/// </summary>
public static class StartupModeRouter
{
    /// <summary>
    /// Maps the user's choice to a <see cref="StartupMode"/>. Default is
    /// <see cref="StartupMode.Legacy"/>; New system mode is strictly opt-in.
    /// </summary>
    /// <param name="runNewSystem">
    /// <see langword="true"/> when the user checked "הפעל מערכת חדשה"; otherwise
    /// <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see cref="StartupMode.NewSystem"/> when <paramref name="runNewSystem"/> is
    /// <see langword="true"/>; otherwise <see cref="StartupMode.Legacy"/>.
    /// </returns>
    public static StartupMode Resolve(bool runNewSystem) =>
        runNewSystem ? StartupMode.NewSystem : StartupMode.Legacy;

    /// <summary>
    /// Convenience predicate: <see langword="true"/> when the chosen mode must open the clean shell
    /// instead of the legacy main window.
    /// </summary>
    public static bool OpensNewShell(StartupMode mode) => mode == StartupMode.NewSystem;

    /// <summary>
    /// Convenience predicate: <see langword="true"/> when the chosen mode must keep the legacy
    /// startup path (open the legacy main window) unchanged.
    /// </summary>
    public static bool OpensLegacyMainWindow(StartupMode mode) => mode == StartupMode.Legacy;
}
