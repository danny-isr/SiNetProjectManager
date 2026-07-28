using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Acc;
using SiNet.Infrastructure.Sql.Services.Email.Acc;

namespace SiNet.Infrastructure.Sql;

public static class EmailAccServiceCollectionExtensions
{
    /// <summary>Registers ACC inbox status/upload coordinator ports (read + explicit upload orchestration).</summary>
    public static IServiceCollection AddSiNetEmailAccSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<EmailAccInboxQueryService>();
        services.AddSingleton<IEmailAccStatusService, SqlEmailAccStatusService>();
        services.AddSingleton<IEmailAccUploadCoordinator, EmailAccUploadCoordinator>();
        services.AddSingleton<IEmailExternalDownloadCoordinator, EmailExternalDownloadCoordinator>();
        services.AddSingleton<IEmailAccBackgroundWorkTracker, EmailAccBackgroundWorkTracker>();
        services.AddSingleton<IEmailAccIngestQueue, EmailAccIngestQueue>();
        services.AddSingleton<IEmailMoveToProjectCoordinator, EmailMoveToProjectCoordinator>();

        // Standalone native ingest. V2 may register LegacyEmailAccIngestionExecutor later (last wins).
        services.AddTransient<IEmailAccIngestionExecutor, NativeEmailAccIngestionExecutor>();

        return services;
    }
}
