using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Services.Projects;

namespace SiNet.Infrastructure.Sql;
/// <summary>
/// Modular DI registration for the real, read-only Project query slice
/// (see <c>docs/PROJECTS.md</c> §5 and <c>docs/PROJECT_CONTEXT_MIGRATION.md</c>).
/// <para>
/// Registers <see cref="ProjectQueryService"/> as the concrete type and forwards the Application port
/// <see cref="IProjectQueryService"/> to the same instance, so the shared Project Selector loads real
/// projects instead of the in-memory fake. This is a <b>read-only</b> slice: no writes, no schema, no
/// migrations.
/// </para>
/// </summary>
public static class ProjectQueryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the real read-only <see cref="IProjectQueryService"/> backed by
    /// <see cref="ProjectQueryService"/>. Requires an
    /// <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c> to be registered separately (for example via
    /// <see cref="SqlServiceCollectionExtensions.AddSiNetSql(IServiceCollection, string)"/>).
    /// </summary>
    public static IServiceCollection AddSiNetProjectQuerySql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<ProjectQueryService>();
        services.AddTransient<IProjectQueryService>(sp => sp.GetRequiredService<ProjectQueryService>());

        services.AddTransient<ProjectFilterOptionsService>();
        services.AddTransient<IProjectFilterOptionsService>(sp => sp.GetRequiredService<ProjectFilterOptionsService>());

        services.AddTransient<ProjectDashboardQueryService>();
        services.AddTransient<IProjectDashboardQueryService>(sp =>
            sp.GetRequiredService<ProjectDashboardQueryService>());

        return services;
    }

    /// <summary>
    /// Registers project create + place/company/job-type catalog write/read ports.
    /// </summary>
    public static IServiceCollection AddSiNetProjectCreateSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IProjectCreateService>(sp =>
            new SqlProjectCreateService(
                sp.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>(),
                sp.GetService<IProjectFolderBootstrapper>(),
                sp.GetService<IProjectAccMappingProvisioner>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<SqlProjectCreateService>>()));
        services.AddTransient<IPlaceCatalogService, SqlPlaceCatalogService>();
        services.AddTransient<ICompanyCatalogService, SqlCompanyCatalogService>();
        services.AddTransient<IJobTypeQueryService, SqlJobTypeQueryService>();

        return services;
    }

    /// <summary>
    /// Registers project update, rename orchestrator, and Gmail label sync ports (DEV-008 / DEV-009).
    /// </summary>
    public static IServiceCollection AddSiNetProjectUpdateSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IProjectUpdateService, SqlProjectUpdateService>();
        services.AddTransient<IProjectRenameOrchestrator>(sp =>
            new ProjectRenameOrchestrator(
                sp.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>(),
                sp.GetService<IProjectDriveRootRenameService>(),
                sp.GetService<SiNet.Application.Abstractions.Autodesk.IAccFolderRenameService>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<ProjectRenameOrchestrator>>()));
        services.AddTransient<IProjectGmailLabelSyncService, ProjectGmailLabelSyncService>();
        services.AddTransient<IGmailMailboxLabelAuditService>(sp =>
            new GmailMailboxLabelAuditService(
                sp.GetRequiredService<SiNet.Application.Abstractions.Email.IEmailGateway>(),
                sp.GetRequiredService<IProjectQueryService>(),
                sp.GetService<IPlaceCatalogService>()));

        return services;
    }
}
