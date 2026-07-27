using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.AutodeskLocal;

public sealed class LocalAccProjectService(IDbContextFactory<SiNetSQLDbContext> dbContextFactory) : ILocalAccProjectService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory = dbContextFactory;

    public async Task<IReadOnlyList<string>> GetProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var mappedProjectIds = await db.ProjectAccMappings
            .AsNoTracking()
            .Where(mapping => mapping.AccProjectId != null && mapping.AccProjectId != string.Empty)
            .Select(mapping => mapping.AccProjectId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var systemProjectIds = await db.AccSystemResources
            .AsNoTracking()
            .Where(resource => resource.AccProjectId != null && resource.AccProjectId != string.Empty)
            .Select(resource => resource.AccProjectId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return mappedProjectIds
            .Concat(systemProjectIds)
            .Select(projectId => projectId.Trim())
            .Where(projectId => projectId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(projectId => projectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
