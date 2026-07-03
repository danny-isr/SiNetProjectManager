using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;

namespace SiNet.App.Wpf.Admin.Settings;

public static class SettingsAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSettingsAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AccControlPlaneStatusPresenter>();
        services.AddSingleton<SettingsViewModelFactory>();
        services.AddSingleton<ISettingsWindowFactory, SettingsWindowFactory>();
        services.AddTransient<SettingsView>();

        return services;
    }
}
