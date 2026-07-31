using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email;
using SiNet.Application.Email.QuoteSend;
using SiNet.Infrastructure.Sql.Services.Email;

namespace SiNet.Infrastructure.Sql;

public static class EmailReadServiceCollectionExtensions
{
    /// <summary>Registers read-only email inbox ports (no Gmail write, no filing).</summary>
    public static IServiceCollection AddSiNetEmailReadSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEmailInboxQueryService, SqlEmailInboxQueryService>();
        services.AddSingleton<IEmailThreadLinkQueryService, SqlEmailThreadLinkQueryService>();
        services.AddSingleton<IEmailThreadMappingSyncService, SqlEmailThreadMappingSyncService>();
        services.AddSingleton<IProposalSourceEmailQuery, SqlProposalSourceEmailQuery>();
        services.AddSingleton<IQuoteSendProofStore, SqlQuoteSendProofStore>();
        services.AddSingleton<IQuoteSendComposeService, QuoteSendComposeService>();
        services.AddSingleton<IQuoteSendAttachmentService, QuoteSendAttachmentService>();

        return services;
    }
}
