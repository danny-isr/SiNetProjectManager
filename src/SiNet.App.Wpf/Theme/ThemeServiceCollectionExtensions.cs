using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Theme;

public static class ThemeServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetThemeWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IThemeRuntimeApplier, WpfThemeRuntimeApplier>();
        services.AddSingleton<ThemeStartupInitializer>();

        return services;
    }
}
