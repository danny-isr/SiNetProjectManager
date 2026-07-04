using Microsoft.Extensions.DependencyInjection;
using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;

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

        services.AddSiNetAutodeskLocalFileTransfer();
        services.AddTransient<IAccFolderItemsReader>(sp =>
            new Bim360AccFolderItemsReader(sp.GetService<ITokenProvider>()));
        services.AddTransient<IAccFolderContentsReader>(sp =>
            new Bim360AccFolderContentsReader(sp.GetService<ITokenProvider>()));
        services.AddTransient<IAccHubReader>(sp =>
            new Bim360AccHubReader(sp.GetService<ITokenProvider>()));
        services.AddTransient<IAccLiveProjectReader>(sp =>
            new Bim360AccLiveProjectReader(sp.GetService<ITokenProvider>()));
        services.AddTransient<IAccProjectRootFolderResolver, LocalAccProjectRootFolderResolver>();
        services.AddTransient<LocalAccProjectCatalogService>();
        services.AddTransient<LocalAccLiveProjectDiscoveryService>();
        services.AddTransient<LocalAccProjectService>();
        services.AddTransient<LocalAccDocumentService>();
        services.AddTransient<LocalAccFolderBrowserService>();
        services.AddTransient<LocalAccProjectTreeSearchService>();
        services.AddTransient<IAccLookupSeedService, LocalAccLookupSeedService>();
        services.AddHttpClient<RemoteAccProjectCatalogService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccLiveProjectDiscoveryService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccProjectService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccDocumentService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccFolderBrowserService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccProjectTreeSearchService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccInboxBootstrapService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccFileUploadService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddHttpClient<RemoteAccFileDownloadService>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(sp.GetRequiredService<AccServiceControlPlaneOptions>()));
        services.AddTransient<IAccLiveProjectDiscoveryService, ModeSwitchingAccLiveProjectDiscoveryService>();
        services.AddTransient<IAccProjectCatalogService, ModeSwitchingAccProjectCatalogService>();
        services.AddTransient<IAccProjectService, ModeSwitchingAccProjectService>();
        services.AddTransient<IAccDocumentService, ModeSwitchingAccDocumentService>();
        services.AddTransient<IAccFileUploadService, ModeSwitchingAccFileUploadService>();
        services.AddTransient<IAccFileDownloadService, ModeSwitchingAccFileDownloadService>();
        services.AddTransient<IAccFolderBrowserService, ModeSwitchingAccFolderBrowserService>();
        services.AddTransient<IAccProjectTreeSearchService, ModeSwitchingAccProjectTreeSearchService>();
        services.AddTransient<IAccInboxBootstrapService>(sp =>
            new ModeSwitchingAccInboxBootstrapService(
                sp.GetRequiredService<IAccServiceModeProvider>(),
                sp.GetService<IAccInboxBootstrapLocalExecutor>(),
                sp.GetRequiredService<RemoteAccInboxBootstrapService>()));

        return services;
    }
}
