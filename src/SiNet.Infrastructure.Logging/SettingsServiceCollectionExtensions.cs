using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Logging;

/// <summary>Stage 5 settings registrations (per-user JSON logging slice).</summary>
public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetUserLoggingSettings(this IServiceCollection services)
    {
        services.AddSingleton<IAppSettingsService, JsonUserLoggingSettingsService>();
        return services;
    }
}
