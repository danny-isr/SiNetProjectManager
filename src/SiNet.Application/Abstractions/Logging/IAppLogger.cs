namespace SiNet.Application.Abstractions.Logging;

/// <summary>
/// Application logging port. Implemented by <c>SiNet.Infrastructure.Logging</c>
/// (Serilog adapter). Keeps the rest of the codebase free of a concrete logger dependency.
/// </summary>
public interface IAppLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
