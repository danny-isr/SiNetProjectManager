namespace SiNet.Application.Identity;

/// <summary>Hebrew-friendly labels for <see cref="AppRole"/> in native admin UI.</summary>
public static class AppRoleDisplay
{
    public static string GetDisplayName(AppRole value) => value switch
    {
        AppRole.Unauthorized => "ממתין לאישור מנהל מערכת",
        AppRole.Employee => "עובד",
        AppRole.Management => "ניהול",
        AppRole.Administrator => "מנהל מערכת",
        _ => value.ToString(),
    };
}
