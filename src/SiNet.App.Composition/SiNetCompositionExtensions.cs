using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.ProjectWork;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.FileSystem;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.AutodeskLocal;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.LegacyBridge;

namespace SiNet.App.Composition;

/// <summary>
/// Composition root. Aggregates the modular <c>AddSiNet*</c> registrations so an application
/// host (for example <c>SiNet.App.Wpf</c>) wires the whole service graph with a single call.
/// This replaces the legacy ~690-line <c>ConfigureServices</c>.
/// </summary>
public static class SiNetCompositionExtensions
{
    public static IServiceCollection AddSiNet(this IServiceCollection services)
        => services.AddSiNet(SiNetHostMode.StandaloneNew, static _ => { });

    /// <summary>
    /// Aggregates the modular registrations for <see cref="SiNetHostMode.StandaloneNew"/>.
    /// </summary>
    public static IServiceCollection AddSiNet(
        this IServiceCollection services,
        Action<GmailOptions> configureGmail)
        => services.AddSiNet(SiNetHostMode.StandaloneNew, configureGmail);

    /// <summary>
    /// Aggregates the modular registrations and lets the host configure the Gmail module
    /// (client secrets path, token store, application name, interactive sign-in).
    /// <see cref="SiNetHostMode.V2Hybrid"/> is the only mode that registers LegacyBridge.
    /// </summary>
    public static IServiceCollection AddSiNet(
        this IServiceCollection services,
        SiNetHostMode hostMode,
        Action<GmailOptions> configureGmail)
    {
        services.AddSiNetLogging();
        services.AddSiNetSql();
        services.AddSiNetProcessBackbone();
        services.AddTransient<IOpenQuoteProjectDecisionService, OpenQuoteProjectDecisionService>();
        services.AddSiNetProjectQuerySql();
        services.AddSiNetProjectCreateSql();
        services.AddSiNetEmailReadSql();
        services.AddSiNetEmailWriteSql();
        services.AddSiNetEmailAccSql();
        services.AddSiNetEmailDetailSql();
        services.AddSiNetUserManagementSql();
        services.AddSiNetGoogle(configureGmail);
        services.AddSiNetAutodesk();
        services.AddSiNetAutodeskLocalSql();

        if (hostMode == SiNetHostMode.V2Hybrid)
        {
            services.AddSiNetLegacyBridge();
        }

        services.AddSiNetInspectionSql();
        services.AddSiNetDevTools();

        // ProjectWork runtime (FileServer/ACC/Drive stores + hubs). Google Drive store is registered
        // by AddSiNetGoogle above; FileServer/ACC + hubs land here.
        services.AddSiNetProjectWorkRuntime();

        return services;
    }

    /// <summary>
    /// Registers the ProjectWork file-index runtime: SQL folder resolvers, FileServer store/watcher,
    /// ACC store, ACC-write gate (closed by default), FileIndex coordinator, and process-wide
    /// <see cref="IActiveFileQueryHub"/> / <see cref="IFileOpenHub"/>. Safe to call from hosts that
    /// already registered Google/Autodesk modules (Drive store comes from <c>AddSiNetGoogle</c>).
    /// Idempotent for the ACC-write policy via <c>TryAddSingleton</c>.
    /// </summary>
    public static IServiceCollection AddSiNetProjectWorkRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSiNetFileSystem();
        services.AddSiNetProjectWorkSql();

        // ACC-write gate: closed by default (writes ship dark). A host may override this registration
        // with a configuration-driven policy after the ACC-Write-Policy is approved.
        services.TryAddSingleton<IAccWritePolicy>(new StaticAccWritePolicy(isWriteEnabled: false));

        if (!services.Any(d =>
                d.ServiceType == typeof(IFileStore)
                && d.ImplementationType == typeof(SiNet.Infrastructure.Sql.ProjectWork.AccFileStore)))
        {
            services.AddSingleton<IFileStore, SiNet.Infrastructure.Sql.ProjectWork.AccFileStore>();
        }

        services.TryAddSingleton<IFileIndexService, FileIndexService>();
        services.TryAddSingleton<ActiveFileQueryHub>();
        services.TryAddSingleton<IActiveFileQueryHub>(sp => sp.GetRequiredService<ActiveFileQueryHub>());
        services.TryAddSingleton<IActiveFileQueryService>(sp => sp.GetRequiredService<ActiveFileQueryHub>());
        services.TryAddSingleton<FileOpenHub>();
        services.TryAddSingleton<IFileOpenHub>(sp => sp.GetRequiredService<FileOpenHub>());
        services.TryAddSingleton<IFileOpenService>(sp => sp.GetRequiredService<FileOpenHub>());

        return services;
    }
}
