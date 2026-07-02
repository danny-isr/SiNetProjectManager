namespace SiNet.Application.Abstractions.Logging;

/// <summary>
/// Application logging port. Implemented by <c>SiNet.Infrastructure.Logging</c>:
/// <c>SerilogAppLogger</c> (production, host Serilog pipeline) or <c>ConsoleAppLogger</c> (scaffold/tests).
/// See <c>docs/LOGGING.md</c>.
/// </summary>
public interface IAppLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
