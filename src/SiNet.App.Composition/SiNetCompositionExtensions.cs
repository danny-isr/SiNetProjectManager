using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.FileSystem;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Sql;
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
        => services.AddSiNet(static _ => { });

    /// <summary>
    /// Aggregates the modular registrations and lets the host configure the Gmail module
    /// (client secrets path, token store, application name, interactive sign-in).
    /// </summary>
    public static IServiceCollection AddSiNet(
        this IServiceCollection services,
        Action<GmailOptions> configureGmail)
    {
        services.AddSiNetLogging();
        services.AddSiNetFileSystem();
        services.AddSiNetSql();
        services.AddSiNetWorkflowReads();
        services.AddSiNetProjectQuerySql();
        services.AddSiNetGoogle(configureGmail);
        services.AddSiNetAutodesk();
        services.AddSiNetLegacyBridge();
        return services;
    }
}
