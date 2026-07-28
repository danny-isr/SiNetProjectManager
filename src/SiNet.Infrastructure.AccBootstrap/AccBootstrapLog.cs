using Serilog;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Thin Serilog facade for the AccBootstrap module. Native replacement for the legacy
/// <c>SiNetSQL.Services.AppLogger</c> so this project does not need a ProjectReference to
/// SiNetSQL (see docs/ACC_SERVICE_DECOUPLING.md, slice B4). Same call-site signatures as
/// <c>AppLogger</c> (<c>Info</c>/<c>Warn</c>/<c>Error</c>) to keep the moved files' diff minimal.
/// </summary>
internal static class AccBootstrapLog
{
    public static void Info(string message) => Log.Information(message);

    public static void Warn(string message) => Log.Warning(message);

    public static void Error(string message) => Log.Error(message);

    public static void Error(Exception ex, string message) => Log.Error(ex, message);
}
