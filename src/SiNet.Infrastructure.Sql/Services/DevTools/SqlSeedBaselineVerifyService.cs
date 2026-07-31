using Microsoft.EntityFrameworkCore;
using SiNet.Application.DevTools;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>Read-only SQL probe for <see cref="SeedBaselineCatalog"/> Codes.</summary>
public sealed class SqlSeedBaselineVerifyService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : ISeedBaselineVerifyService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<SeedBaselineVerifyResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Materialize for EF Core IN translation.
        var requiredWorkflows = SeedBaselineCatalog.RequiredWorkflowDefinitionCodes.ToArray();
        var requiredGroups = SeedBaselineCatalog.RequiredUserGroupCodes.ToArray();
        var requiredCatalog = SeedBaselineCatalog.RequiredProjectFileCatalogCodes.ToArray();

        var workflowCodes = await db.WorkflowDefinitions.AsNoTracking()
            .Where(w => w.IsActive && w.Code != null && requiredWorkflows.Contains(w.Code))
            .Select(w => w.Code!)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var groupCodes = await db.UserGroups.AsNoTracking()
            .Where(g => requiredGroups.Contains(g.Code))
            .Select(g => g.Code)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var catalogCodes = await db.ProjectFiles.AsNoTracking()
            .Where(f => f.Code != null && requiredCatalog.Contains(f.Code))
            .Select(f => f.Code!)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobTypePresent = await db.JobTypes.AsNoTracking()
            .AnyAsync(
                j => j.Title == SeedBaselineCatalog.RequiredJobTypeTitle,
                cancellationToken)
            .ConfigureAwait(false);

        var correspondencePresent = await db.ProjectFolders.AsNoTracking()
            .AnyAsync(
                f => f.Title == SeedBaselineCatalog.RequiredCorrespondenceFolderTitle,
                cancellationToken)
            .ConfigureAwait(false);

        return SeedBaselineVerifyResult.Evaluate(
            workflowCodes,
            groupCodes,
            catalogCodes,
            jobTypePresent,
            correspondencePresent);
    }
}
