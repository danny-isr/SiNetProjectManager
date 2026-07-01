namespace SiNet.App.Wpf.Shell;

/// <summary>
/// The startup mode chosen at the first login/user-selection moment (see
/// <c>docs/APP_SHELL.md</c> §2/§3).
/// <para>
/// <see cref="Legacy"/> keeps the current production behavior and opens the legacy
/// <c>SiNetProjectManagerV2.MainWindow</c>. <see cref="NewSystem"/> opens the clean
/// <see cref="NewShellWindow"/> instead and must NOT open the legacy main window.
/// </para>
/// </summary>
public enum StartupMode
{
    /// <summary>Current behavior: legacy host, legacy menus, legacy windows.</summary>
    Legacy = 0,

    /// <summary>Clean refactored shell only; the legacy main window is not opened.</summary>
    NewSystem = 1,
}
