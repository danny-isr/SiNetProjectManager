using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Google;
using SiNet.Application.Runtime;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Health;

namespace SiNet.App.Composition;

/// <summary>
/// Registers the ported «מצב מערכת» contributors (see <c>docs/SYSTEM_HEALTH.md</c>).
/// <para>
/// Standalone-only by design. The V2 hybrid host already renders these rows through
/// <c>IExternalHealthCheckSource</c>, and registering the contributors there too would run every
/// probe twice only to have the merge discard the results.
/// </para>
/// </summary>
public static class SystemHealthContributorsExtensions
{
    public static IServiceCollection AddSiNetSystemHealthContributors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IGoogleDriveFolderDiagnostics, GoogleDriveFolderDiagnostics>();

        services.AddSingleton<ISubsystemStatusContributor, DatabaseStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, WorkflowEngineStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, SeedBaselineStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, FileServerStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, LoggingCentralStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, OllamaStatusContributor>();

        services.AddSingleton<ISubsystemStatusContributor, GoogleConfigStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, GoogleAccountStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, GmailReachabilityStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, GoogleTemplatesFolderStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, GoogleReportsFolderStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, MasterPlanReportsDriveStatusContributor>();

        services.AddSingleton<ISubsystemStatusContributor, AutodeskTokenStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, AccServiceStatusContributor>();
        services.AddSingleton<ISubsystemStatusContributor, AccAdminIdentityStatusContributor>();

        return services;
    }
}
