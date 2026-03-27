using SiNetSQL.Services;
using SiOffice.GoogleConnector.Logging;

namespace SiNetProjectManager.Services;

/// <summary>
/// Adapter that connects the GoogleConnector's IReportLogger interface
/// to the central AppLogger implementation.
/// </summary>
public class AppLoggerReportAdapter : IReportLogger
{
    public static readonly AppLoggerReportAdapter Instance = new();

    public void Info(string message) => AppLogger.Info(message);
    public void Warn(string message) => AppLogger.Warn(message);
    public void Error(string message) => AppLogger.Error(message);
    public void Error(Exception ex, string context) => AppLogger.Error(ex, context);
    public void Debug(string message) => AppLogger.Debug(message);
}
