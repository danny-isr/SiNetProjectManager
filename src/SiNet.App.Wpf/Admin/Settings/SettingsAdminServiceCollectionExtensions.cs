using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.Settings;

public static class SettingsAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSettingsAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<SettingsViewModelFactory>();
        services.AddSingleton<ISettingsWindowFactory, SettingsWindowFactory>();
        services.AddTransient<SettingsView>();

        return services;
    }
}
