using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.DevTools;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Registers New System dev-tools services.
/// DEBUG: full seed/reset/demo. Release: read-only seed-baseline verify + fail-closed stubs only
/// (no mutating seed/reset services). See <c>docs/RELEASE_AUTOMATION_LOCKDOWN.md</c>.
/// </summary>
public static class DevToolsServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetDevTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Read-only; System Status SeedBaseline contributor needs this in Release too.
        services.AddTransient<ISeedBaselineVerifyService, SqlSeedBaselineVerifyService>();

#if DEBUG
        services.AddSingleton<DevToolsGate>();
        services.AddTransient<SqlWorkflowSeedService>();
        services.AddTransient<SqlTaskDemoSeedService>();
        services.AddTransient<IStaticSeedService, SqlStaticSeedService>();
        services.AddTransient<IDevDataResetService, SqlDevDataResetService>();
#else
        // Fail-closed stubs: resolve ≠ mutate. Menu is already #if DEBUG in NewShellFactory.
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
