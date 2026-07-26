using Microsoft.EntityFrameworkCore;
using SiNet.Application.DevTools;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// New System static / workflow / demo seed facade.
/// </summary>
public sealed class SqlStaticSeedService : IStaticSeedService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly SqlWorkflowSeedService _workflowSeed;
    private readonly SqlTaskDemoSeedService _demoSeed;
    private readonly DevToolsGate _gate;

    public SqlStaticSeedService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SqlWorkflowSeedService workflowSeed,
        SqlTaskDemoSeedService demoSeed,
        DevToolsGate gate)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _workflowSeed = workflowSeed ?? throw new ArgumentNullException(nameof(workflowSeed));
        _demoSeed = demoSeed ?? throw new ArgumentNullException(nameof(demoSeed));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public void ResetSeedingSessionFlag() => SqlTaskManagementSeedService.ResetSeedingSessionFlag();

    public async ValueTask<SeedResult> SeedTaskStaticLookupsAsync(CancellationToken ct = default)
    {
        EnsureSeedAuthorized();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var svc = new SqlTaskManagementSeedService(db);
            await svc.EnsureStaticLookupDataAsync(ct).ConfigureAwait(false);
            return Ok("Static task lookups seeded (create-missing-only).");
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public async ValueTask<SeedResult> SeedTaskMappingsAsync(CancellationToken ct = default)
    {
        EnsureSeedAuthorized();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var svc = new SqlTaskManagementSeedService(db);
            var result = svc.ResetMappingsToDefaults();
            return result.Success
                ? Ok(result.GetSummary())
                : new SeedResult { Succeeded = false, Summary = result.GetSummary(), Errors = [result.ErrorMessage ?? "Mapping reset failed"] };
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public async ValueTask<SeedResult> SeedWorkflowDefinitionsAsync(CancellationToken ct = default)
    {
        EnsureSeedAuthorized();
        try
        {
            await _workflowSeed.SeedAllAsync(ct).ConfigureAwait(false);
            return Ok("Workflow definitions, user groups, and ProjectType activations seeded.");
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public async ValueTask<SeedResult> SeedDemoTasksAsync(DemoTaskSeedOptions? options = null, CancellationToken ct = default)
    {
        EnsureSeedAuthorized();
        return await _demoSeed.SeedAsync(options, ct).ConfigureAwait(false);
    }

    public async ValueTask<SeedResult> SeedAllCoreAsync(CancellationToken ct = default)
    {
        var errors = new List<string>();

        var staticResult = await SeedTaskStaticLookupsAsync(ct).ConfigureAwait(false);
        if (!staticResult.Succeeded) errors.AddRange(staticResult.Errors);

        var mapResult = await SeedTaskMappingsAsync(ct).ConfigureAwait(false);
        if (!mapResult.Succeeded) errors.AddRange(mapResult.Errors);

        var wfResult = await SeedWorkflowDefinitionsAsync(ct).ConfigureAwait(false);
        if (!wfResult.Succeeded) errors.AddRange(wfResult.Errors);

        var catalogResult = await SeedProjectFileCatalogAsync(ct).ConfigureAwait(false);
        if (!catalogResult.Succeeded) errors.AddRange(catalogResult.Errors);

        return new SeedResult
        {
            Succeeded = errors.Count == 0,
            Summary =
                $"Core seed: static={staticResult.Succeeded}, mappings={mapResult.Succeeded}, " +
                $"workflow={wfResult.Succeeded}, projectFileCatalog={catalogResult.Succeeded}",
            Errors = errors,
        };
    }

    public async ValueTask<SeedResult> SeedProjectFileCatalogAsync(CancellationToken ct = default)
    {
        EnsureSeedAuthorized();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var summary = await ProjectFileCatalogSeedData.EnsureAsync(db, ct).ConfigureAwait(false);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    private void EnsureSeedAuthorized()
    {
#if !DEBUG
        throw new NotSupportedException("Dev seed is available in DEBUG builds only.");
#else
        _gate.EnsureDevToolsAuthorized("Dev seed");
#endif
    }

    private static SeedResult Ok(string summary) => new() { Succeeded = true, Summary = summary };

    private static SeedResult Fail(Exception ex) =>
        new() { Succeeded = false, Summary = ex.Message, Errors = [ex.Message] };
}
