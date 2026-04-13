using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Registers and manages the custom URI scheme (<c>com.sinet.manager://</c>) in the Windows Registry.
/// This enables the system browser to redirect back to the application after Google OAuth consent.
/// Registration is per-user (HKCU) and does not require elevation.
///
/// Google Console must have the Redirect URI set to: <c>com.sinet.manager:/oauth2redirect</c>
/// </summary>
internal static class CustomProtocolRegistrar
{
    /// <summary>The custom URI scheme registered for this application.</summary>
    internal const string ProtocolScheme = "com.sinet.manager";

    /// <summary>The OAuth redirect path appended to the scheme.</summary>
    private const string OAuthRedirectPath = "/oauth2redirect";

    /// <summary>
    /// The full redirect URI for Google OAuth configuration.
    /// Configure this exact value in Google Cloud Console → Credentials → Authorized redirect URIs.
    /// </summary>
    internal static string RedirectUri => $"{ProtocolScheme}:{OAuthRedirectPath}";

    /// <summary>
    /// Ensures the custom protocol is registered in HKCU for the current user.
    /// Safe to call multiple times — idempotent. Updates the exe path if it changed.
    /// </summary>
    internal static void EnsureRegistered()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot determine executable path.");

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolScheme}");
            key.SetValue("", $"URL:{ProtocolScheme} Protocol");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"\"{exePath}\",1");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey.SetValue("", $"\"{exePath}\" \"%1\"");

            Log.Information("Custom protocol '{Scheme}' registered → {ExePath}", ProtocolScheme, exePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register custom protocol '{Scheme}'", ProtocolScheme);
        }
    }

    /// <summary>
    /// Parses the query string from a protocol activation URI into a dictionary.
    /// Expected format: <c>com.sinet.manager:/oauth2redirect?code=AUTH_CODE&amp;scope=...</c>
    /// </summary>
    internal static Dictionary<string, string> ParseCallbackQuery(string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(uri)) return result;

        try
        {
            var queryIndex = uri.IndexOf('?');
            if (queryIndex < 0) return result;

            var query = uri[(queryIndex + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse OAuth callback URI: {Uri}", uri);
        }

        return result;
    }
}
