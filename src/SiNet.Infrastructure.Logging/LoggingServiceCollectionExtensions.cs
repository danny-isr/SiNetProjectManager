using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Modular DI registration for the logging module.
/// </summary>
public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetLogging(this IServiceCollection services)
    {
        services.AddSingleton<IAppLogger, ConsoleAppLogger>();
        return services;
    }
}
