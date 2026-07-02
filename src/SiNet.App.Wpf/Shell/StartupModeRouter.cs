namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Pure routing helper for the startup mode chosen in <see cref="StartupModeSelectionWindow"/>
/// (see <c>docs/APP_SHELL.md</c> §3).
/// </summary>
public static class StartupModeRouter
{
    /// <summary>
    /// Maps a boolean startup choice to <see cref="StartupMode"/> (legacy checkbox hosts only).
    /// </summary>
    public static StartupMode Resolve(bool runNewSystem) =>
        runNewSystem ? StartupMode.NewSystem : StartupMode.Legacy;

    /// <summary>True when the chosen mode must open <see cref="NewShellWindow"/>.</summary>
    public static bool OpensNewShell(StartupMode mode) => mode == StartupMode.NewSystem;

    /// <summary>True when the chosen mode must open the legacy <c>MainWindow</c>.</summary>
    public static bool OpensLegacyMainWindow(StartupMode mode) => mode == StartupMode.Legacy;
}
