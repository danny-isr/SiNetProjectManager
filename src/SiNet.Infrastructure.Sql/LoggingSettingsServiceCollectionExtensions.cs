using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Settings;

namespace SiNet.Infrastructure.Sql;

/// <summary>Stage 5 global logging settings (DB <c>Logging.*</c> keys).</summary>
public static class LoggingSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetLoggingSettingsSql(this IServiceCollection services)
    {
        services.AddSingleton<SqlLoggingSettingsService>();
        services.AddSingleton<ILoggingSettingsQueryService>(sp => sp.GetRequiredService<SqlLoggingSettingsService>());
        services.AddSingleton<ILoggingSettingsCommandService>(sp => sp.GetRequiredService<SqlLoggingSettingsService>());
        return services;
    }
}
