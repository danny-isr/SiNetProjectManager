using System.Security.Principal;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>Windows account allow-list for dev reset (mirrors legacy DevDataResetService).</summary>
internal static class DevToolsWindowsUserPolicy
{
    private static readonly string[] AllowedWindowsUsers =
    [
        @"SI-ENG\Danny",
        @"AzureAD\dannyisrael",
    ];

    public static string CurrentWindowsUser => WindowsIdentity.GetCurrent().Name;

    public static bool IsCurrentUserAllowed()
    {
        var user = CurrentWindowsUser;
        foreach (var allowed in AllowedWindowsUsers)
        {
            if (string.Equals(user, allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
