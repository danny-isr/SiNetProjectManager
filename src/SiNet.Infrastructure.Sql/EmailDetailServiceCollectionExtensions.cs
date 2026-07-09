using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Services.Email.Detail;

namespace SiNet.Infrastructure.Sql;

public static class EmailDetailServiceCollectionExtensions
{
    /// <summary>Registers Email Detail Application ports with native Infrastructure.Sql implementations.</summary>
    public static IServiceCollection AddSiNetEmailDetailSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEmailAccIngestionService, SqlEmailAccIngestionService>();
        services.AddSingleton<IEmailAttachmentTaggingService, SqlEmailAttachmentTaggingService>();
        services.AddSingleton<IEmailMoveToProjectEligibilityService, SqlEmailMoveToProjectEligibilityService>();
        services.AddSingleton<IEmailMoveToProjectService, SqlEmailMoveToProjectService>();
        services.AddSingleton<IEmailExternalDownloadService, SqlEmailExternalDownloadService>();
        services.AddSingleton<IEmailWorkflowContextService, SqlEmailWorkflowContextService>();
        services.AddSingleton<IEmailSuggestedActionService, SqlEmailSuggestedActionService>();
        services.AddSingleton<IEmailSuggestedActionExecutionService, SqlEmailSuggestedActionExecutionService>();

        return services;
    }
}
