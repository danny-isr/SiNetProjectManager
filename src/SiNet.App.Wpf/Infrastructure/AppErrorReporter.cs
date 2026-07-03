using System.Diagnostics;

namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Central place to record unexpected errors from the WPF layer without referencing Serilog or legacy
/// <c>AppLogger</c>. The production host may subscribe to <see cref="ExceptionReported"/> to forward
/// to <see cref="SiNet.Application.Abstractions.Logging.IAppLogger"/>.
/// </summary>
public static class AppErrorReporter
{
    /// <summary>Raised for every reported exception (optional host bridge).</summary>
    public static event Action<Exception, string>? ExceptionReported;

    public static void Report(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        Debug.WriteLine($"[AppError][{context}] {exception}");
        ExceptionReported?.Invoke(exception, context);
    }

    public static string FormatUserMessage(Exception exception, string context)
    {
        var errorId = Guid.NewGuid().ToString("N")[..8];
        return $"שגיאה ({context}) [{errorId}]: {exception.Message}";
    }
}
