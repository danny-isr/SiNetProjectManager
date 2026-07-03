using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Settings;

namespace SiNet.Infrastructure.Sql;

/// <summary>Stage 5 global settings (DB <c>SystemSettings</c> + status colors).</summary>
public static class SystemSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSystemSettingsSql(this IServiceCollection services)
    {
        services.AddSingleton<SqlSystemSettingsService>();
        services.AddSingleton<ISystemSettingsQueryService>(sp => sp.GetRequiredService<SqlSystemSettingsService>());
        services.AddSingleton<ISystemSettingsCommandService>(sp => sp.GetRequiredService<SqlSystemSettingsService>());
        services.AddSingleton<ILoggingSettingsQueryService>(sp => sp.GetRequiredService<SqlSystemSettingsService>());
        services.AddSingleton<ILoggingSettingsCommandService>(sp => sp.GetRequiredService<SqlSystemSettingsService>());
        services.AddSingleton<IStatusColorSettingsService, SqlStatusColorSettingsService>();
        return services;
    }
}
