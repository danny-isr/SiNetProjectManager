using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.Settings;

public static class SettingsAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSettingsAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsView>();
        services.AddTransient<SettingsWindow>();

        return services;
    }
}
