using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.App.Wpf.Autodesk;

namespace SiNet.App.Wpf.Admin.Settings;

public static class SettingsAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSettingsAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AccControlPlaneStatusPresenter>();
        services.AddSingleton<SettingsViewModelFactory>();
        services.AddSingleton<ISettingsWindowFactory, SettingsWindowFactory>();
        services.AddTransient<SettingsView>();

        return services;
    }
}
