using Microsoft.Extensions.DependencyInjection;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Modular DI registration for the Autodesk/ACC module. Real implementations of
/// <c>IAccProjectService</c> and <c>IAccDocumentService</c> are wired here during the
/// ACC migration round (or temporarily via <c>SiNet.LegacyBridge</c>).
/// </summary>
public static class AutodeskServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAutodesk(this IServiceCollection services)
        => services.AddSiNetAutodesk(static _ => { });

    public static IServiceCollection AddSiNetAutodesk(
        this IServiceCollection services,
        Action<AccServiceControlPlaneOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(sp =>
        {
            var options = new AccServiceControlPlaneOptions();
            configure(options);
            return options;
        });

        services.AddSingleton<SiNet.Application.Abstractions.Autodesk.IAccServiceModeProvider, ConfigurationAccServiceModeProvider>();
        services.AddSingleton<SiNet.Application.Abstractions.Autodesk.IAccServiceKeyDiagnostics, VaultAccServiceKeyDiagnostics>();

        services.AddHttpClient<SiNet.Application.Abstractions.Autodesk.IAccServiceHealthProbe, HttpAccServiceHealthProbe>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));

        services.AddHttpClient<SiNet.Application.Abstractions.Autodesk.IAccServiceDiagnosticsProbe, HttpAccServiceDiagnosticsProbe>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));

        return services;
    }
}
