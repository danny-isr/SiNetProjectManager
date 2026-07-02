using Serilog;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// <see cref="IAppLogger"/> adapter that forwards to an existing Serilog logger (default:
/// <see cref="Log.Logger"/> configured by the host). Does not create sinks or alter levels — one
/// shared pipeline with legacy <c>AppLogger</c> (see <c>docs/LOGGING.md</c>).
/// </summary>
public sealed class SerilogAppLogger : IAppLogger
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates an adapter over <paramref name="logger"/>, or <see cref="Log.Logger"/> when omitted.
    /// </summary>
    public SerilogAppLogger(ILogger? logger = null)
    {
        _logger = logger ?? Log.Logger;
    }

    /// <inheritdoc />
    public void Info(string message) => _logger.Information(message);

    /// <inheritdoc />
    public void Warn(string message) => _logger.Warning(message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            _logger.Error(message);
        }
        else
        {
            _logger.Error(exception, message);
        }
    }
}
