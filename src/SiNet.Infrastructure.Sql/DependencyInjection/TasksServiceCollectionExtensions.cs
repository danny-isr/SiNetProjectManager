using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Actions;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Services.Actions;
using SiNet.Infrastructure.Sql.Services.Tasks;

namespace SiNet.Infrastructure.Sql.DependencyInjection;

/// <summary>
/// Registers native Infrastructure.Sql task services. Requires
/// <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c> from <see cref="SqlServiceCollectionExtensions.AddSiNetSql"/>.
/// </summary>
public static class TasksServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetTaskServices(this IServiceCollection services)
    {
        services.AddTransient<SqlTaskNavigationService>();
        services.AddTransient<ITaskNavigationService>(sp => sp.GetRequiredService<SqlTaskNavigationService>());

        services.AddTransient<SqlTaskCompletionService>();
        services.AddTransient<ITaskCompletionService>(sp => sp.GetRequiredService<SqlTaskCompletionService>());

        services.AddTransient<SqlTaskQueryService>();
        services.AddTransient<ITaskQueryService>(sp => sp.GetRequiredService<SqlTaskQueryService>());

        services.AddTransient<SqlTaskQueueService>();
        services.AddTransient<ITaskQueueService>(sp => sp.GetRequiredService<SqlTaskQueueService>());

        services.AddSingleton<SqlTaskCompletionMetadataResolver>();
        services.AddSingleton<ITaskCompletionMetadataResolver>(sp => sp.GetRequiredService<SqlTaskCompletionMetadataResolver>());

        return services;
    }
}
