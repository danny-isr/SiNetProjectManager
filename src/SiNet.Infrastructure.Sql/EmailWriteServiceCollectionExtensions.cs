using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email;
using SiNet.Infrastructure.Sql.Services.Email;

namespace SiNet.Infrastructure.Sql;

public static class EmailWriteServiceCollectionExtensions
{
    /// <summary>Registers email filing and triage status write ports.</summary>
    public static IServiceCollection AddSiNetEmailWriteSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEmailFilingService, SqlEmailFilingService>();
        services.AddSingleton<IEmailStatusService, SqlEmailStatusService>();

        return services;
    }
}
