using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Minimal <see cref="IAppLogger"/> that writes to the console. This is a placeholder for the
/// Foundation Round; a Serilog-backed implementation replaces it during the logging migration.
/// </summary>
public sealed class ConsoleAppLogger : IAppLogger
{
    public void Info(string message) => Console.WriteLine($"[INFO]  {message}");

    public void Warn(string message) => Console.WriteLine($"[WARN]  {message}");

    public void Error(string message, Exception? exception = null) =>
        Console.WriteLine($"[ERROR] {message}{(exception is null ? string.Empty : " :: " + exception)}");
}
