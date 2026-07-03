namespace SiNet.App.Wpf.Admin.Settings;

/// <summary>Which settings surface the window presents (personal vs admin/global).</summary>
public enum SettingsSurfaceScope
{
    /// <summary>Per-user JSON + personal status colors — any authenticated user.</summary>
    Personal,

    /// <summary>Global DB + central logging + global status colors — requires <c>System.Settings.Write</c>.</summary>
    SystemAdmin,
}
