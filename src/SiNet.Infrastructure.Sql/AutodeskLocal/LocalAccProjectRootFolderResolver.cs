using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.AutodeskLocal;

/// <summary>
/// Resolves the ACC hub for a project from the database and delegates the remote folder lookup to
/// <see cref="IAccProjectRootFolderIdReader"/>. The reader is optional: when the Autodesk module is
/// not wired (or has no token provider) the resolver reports "unknown" instead of failing.
/// </summary>
public sealed class LocalAccProjectRootFolderResolver(
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    IAccProjectRootFolderIdReader? rootFolderIdReader) : IAccProjectRootFolderResolver
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory = dbContextFactory;
    private readonly IAccProjectRootFolderIdReader? _rootFolderIdReader = rootFolderIdReader;

    public async Task<string?> ResolveProjectFilesRootFolderIdAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (_rootFolderIdReader is null || string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var normalizedProjectId = NormalizeProjectId(projectId);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var hubId = await ResolveHubIdAsync(db, normalizedProjectId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(hubId))
        {
            return null;
        }

        return await _rootFolderIdReader
            .GetProjectRootFolderIdAsync(hubId, normalizedProjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string?> ResolveHubIdAsync(
        SiNetSQLDbContext db,
        string normalizedProjectId,
        CancellationToken cancellationToken)
    {
        var mappedHubId = await db.ProjectAccMappings
            .AsNoTracking()
            .Where(mapping => mapping.AccProjectId != null && mapping.AccProjectId.Trim() == normalizedProjectId)
            .Join(
                db.AccHubs.AsNoTracking(),
                mapping => mapping.AccHubId,
                hub => hub.Id,
                (_, hub) => hub.HubId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(mappedHubId))
        {
            return mappedHubId.Trim();
        }

        var systemHubId = await db.AccSystemResources
            .AsNoTracking()
            .Where(resource => resource.AccProjectId != null && resource.AccProjectId.Trim() == normalizedProjectId)
            .Join(
                db.AccHubs.AsNoTracking(),
                resource => resource.AccHubId,
                hub => hub.Id,
                (_, hub) => hub.HubId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(systemHubId) ? null : systemHubId.Trim();
    }

    private static string NormalizeProjectId(string projectId)
    {
        var trimmed = projectId.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"b.{trimmed}";
    }
}
