using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.DevTools;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Registers New System dev-tools services (DEBUG-capable; Release stubs fail closed).
/// </summary>
public static class DevToolsServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetDevTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DevToolsGate>();
        services.AddTransient<SqlWorkflowSeedService>();
        services.AddTransient<SqlTaskDemoSeedService>();
        // Read-only; available in Release too (System Status + optional DevTools UI in DEBUG).
        services.AddTransient<ISeedBaselineVerifyService, SqlSeedBaselineVerifyService>();

#if DEBUG
        services.AddTransient<IStaticSeedService, SqlStaticSeedService>();
        services.AddTransient<IDevDataResetService, SqlDevDataResetService>();
#else
        services.AddTransient<IStaticSeedService, SqlStaticSeedServiceReleaseStub>();
        services.AddTransient<IDevDataResetService, SqlDevDataResetServiceReleaseStub>();
#endif

        return services;
    }
}

#if !DEBUG
internal sealed class SqlStaticSeedServiceReleaseStub : IStaticSeedService
{
    public void ResetSeedingSessionFlag() { }

    public ValueTask<SeedResult> SeedTaskStaticLookupsAsync(CancellationToken ct = default) => Throw();
    public ValueTask<SeedResult> SeedTaskMappingsAsync(CancellationToken ct = default) => Throw();
    public ValueTask<SeedResult> SeedWorkflowDefinitionsAsync(CancellationToken ct = default) => Throw();
    public ValueTask<SeedResult> SeedDemoTasksAsync(DemoTaskSeedOptions? options = null, CancellationToken ct = default) => Throw();
    public ValueTask<SeedResult> SeedAllCoreAsync(CancellationToken ct = default) => Throw();

    private static ValueTask<SeedResult> Throw()
    {
        throw new NotSupportedException("Dev seed is available in DEBUG builds only.");
    }
}
#endif
