using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.AutodeskLocal;

public sealed class LocalAccProjectRootFolderResolver(
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    ITokenProvider? tokenProvider) : IAccProjectRootFolderResolver
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory = dbContextFactory;
    private readonly ITokenProvider? _tokenProvider = tokenProvider;

    public async Task<string?> ResolveProjectFilesRootFolderIdAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (_tokenProvider is null || string.IsNullOrWhiteSpace(projectId))
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

        return await new Bim360Service(_tokenProvider)
            .GetProjectRootFolderIdAsync(hubId, normalizedProjectId)
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
