namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Distinguishes closing the email workbench window from quitting the whole application,
/// so <see cref="BackgroundUploadsDialog"/> can explain whether ACC work keeps running.
/// </summary>
public enum BackgroundCloseScope
{
    /// <summary>User is closing only the email window; ACC jobs continue while the app stays open.</summary>
    EmailWindow,

    /// <summary>User is quitting the application; unfinished ACC transfers may be interrupted.</summary>
    Application,
}
