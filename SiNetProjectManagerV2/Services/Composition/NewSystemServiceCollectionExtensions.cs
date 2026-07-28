using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.App.Composition;
using SiNet.App.Wpf;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql.AutodeskLocal;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Modular DI for the New System shell graph (Project Context + shell menu + admin surfaces).
/// Legacy host still shares the same container today; this extension documents the New System slice
/// explicitly (P7 composition split stepping stone).
/// <para>
/// Composition converged on 2026-07-28: the shared modules now come from
/// <c>AddSiNet(<see cref="SiNetHostMode.V2Hybrid"/>, ...)</c>. What remains here is what only the V2
/// host can provide - legacy adapters, WPF surfaces and host-specific configuration.
/// </para>
/// </summary>
public static class NewSystemServiceCollectionExtensions
{
    /// <summary>
    /// Registers Project Context and the clean shell factory. Does not register legacy window factories.
    /// Call after SQL project reads and Inspection shell views are registered.
    /// </summary>
    public static IServiceCollection AddSiNetNewSystemGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Modules that read settings (ACC control-plane TLS pins) resolve IConfiguration from the
        // container. The V2 host owns the configuration root, so publish it here.
        services.TryAddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(AppConfiguration.Configuration);

        // Single shared composition root. V2Hybrid is the only mode that adds LegacyBridge; SQL
        // itself stays host-owned (AddSiNetSql() without a connection string is a no-op) because the
        // V2 host registers IDbContextFactory from its own secret store earlier in ConfigureServices.
        services.AddSiNet(SiNetHostMode.V2Hybrid, ConfigureNewSystemGmail);

        SiNet.Infrastructure.Sql.InspectionServiceCollectionExtensions.AddSiNetAi(services);
        services.AddSiNetSecrets();
        services.AddSiNetSerilogLogging();
        services.AddSiNetUserLoggingSettings();
        SiNet.Infrastructure.Sql.SystemSettingsServiceCollectionExtensions.AddSiNetSystemSettingsSql(services);
        services.AddTransient<IAccInboxBootstrapLocalExecutor, LegacyHostLocalAccInboxBootstrapExecutor>();
        services.AddSingleton<ILoggingRuntimeApplier, LegacyLoggingRuntimeApplier>();
        SiNet.App.Wpf.Theme.ThemeServiceCollectionExtensions.AddSiNetThemeWpf(services);
        // Native centralized project-file filing (FileServer + ACC). Required by the
        // native MoveToProject executor and AddMaterial flows (Phase 3).
        SiNet.Infrastructure.Sql.FilingServiceCollectionExtensions.AddSiNetFilingServices(services);
        // Resolved lazily by GmailClientProvider, so registering it after AddSiNet is fine.
        services.AddSingleton(LegacyGoogleClientSecretsFallback.Create());
        services.AddSiNetNewSystemWpf();
        services.AddSingleton<IMasterPlanEmployeeConnectionProvider, LegacyMasterPlanEmployeeConnectionProvider>();
        services.AddSingleton<IDirectoryUserConnectionProvider, LegacyDirectoryUserConnectionProvider>();
        services.AddSingleton<ISecretSetupHostConfiguration, LegacySecretSetupHostConfiguration>();
        services.AddTransient<IDirectoryUserLookupService, ActiveDirectoryUserLookupService>();

        // Prefer V2 Inspection host adapters over App.Wpf no-ops / SQL placeholders.
        services.AddSingleton<SiNet.Application.Abstractions.Inspection.IInspectionFileTreePickerHost, V2InspectionFileTreePickerHost>();
        services.AddSingleton<SiNet.Application.Abstractions.Inspection.IInspectionReportEmailHost, V2InspectionReportEmailHost>();
        services.AddSingleton<SiNet.Application.Abstractions.Inspection.IInspectionNoteScreenshotHost, V2InspectionNoteScreenshotHost>();
        services.AddSingleton<SiNet.Application.Abstractions.Inspection.IInspectionNoteLinkedFileHost, V2InspectionNoteLinkedFileHost>();
        services.AddSingleton<SiNet.Application.Abstractions.Inspection.IInspectionTemplateCatalog, V2InspectionTemplateCatalog>();
        services.AddTransient<SiNet.Application.Abstractions.Inspection.IInspectionReportExportPort, V2InspectionReportExportPort>();
        services.AddTransient<SiNet.Application.Abstractions.Inspection.IInspectionReportCommandService, V2InspectionReportCommandService>();

        // ProjectWork: shell content host (cached UserControl) preferred over floating window.
        services.AddSingleton<SiNet.Application.ProjectWork.IProjectWorkSurfaceHost, V2ProjectWorkSurfaceHost>();

        services.AddSingleton<SiNet.Application.Runtime.IExternalHealthCheckSource, LegacySystemHealthCheckSource>();

        return services;
    }

    private static void ConfigureNewSystemGmail(GmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.TokenStorePath = AppConfiguration.GoogleTokenStorePath;
        options.ApplicationName = AppConfiguration.GoogleApplicationName;
        options.SharedDriveId = AppConfiguration.GoogleDriveSharedDriveId;
        options.ProjectsRootFolderId = AppConfiguration.GoogleDriveProjectsRootFolderId;
    }
}
