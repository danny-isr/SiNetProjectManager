using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // Standalone hosts need an in-process notifier so Workbench reloads immediately after
        // completion. V2 may register ActiveProjectTaskListChangeNotifier later (last wins).
        services.TryAddSingleton<ITaskListChangeNotifier, InProcessTaskListChangeNotifier>();

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

        services.AddTransient<SqlTaskWorkbenchService>();
        services.AddTransient<ITaskWorkbenchService>(sp => sp.GetRequiredService<SqlTaskWorkbenchService>());

        return services;
    }
}
