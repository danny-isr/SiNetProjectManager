using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.AutodeskLocal;

public sealed class LocalAccProjectCatalogService(IDbContextFactory<SiNetSQLDbContext> dbContextFactory) : ILocalAccProjectCatalogService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory = dbContextFactory;

    public async Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var mappedProjects = await db.ProjectAccMappings
            .AsNoTracking()
            .Where(mapping => mapping.AccProjectId != null && mapping.AccProjectId != string.Empty)
            .Select(mapping => new RawAccProjectCatalogRecord(
                mapping.AccProjectId!,
                mapping.AccProjectName,
                "ProjectAccMapping",
                0))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var systemProjects = await db.AccSystemResources
            .AsNoTracking()
            .Where(resource => resource.AccProjectId != null && resource.AccProjectId != string.Empty)
            .Select(resource => new RawAccProjectCatalogRecord(
                resource.AccProjectId!,
                resource.Key,
                "AccSystemResource",
                1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return mappedProjects
            .Concat(systemProjects)
            .Select(Normalize)
            .Where(static record => record is not null)
            .Cast<RawAccProjectCatalogRecord>()
            .GroupBy(static record => record.ProjectId, StringComparer.OrdinalIgnoreCase)
            .Select(SelectBestEntry)
            .OrderBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ProjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RawAccProjectCatalogRecord? Normalize(RawAccProjectCatalogRecord record)
    {
        var projectId = record.ProjectId.Trim();
        if (projectId.Length == 0)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(record.DisplayName)
            ? projectId
            : record.Priority == 1
                ? $"System: {record.DisplayName.Trim()}"
                : record.DisplayName.Trim();

        return record with
        {
            ProjectId = projectId,
            DisplayName = displayName,
        };
    }

    private static AccProjectCatalogEntry SelectBestEntry(IGrouping<string, RawAccProjectCatalogRecord> group)
    {
        var best = group
            .OrderBy(static record => record.Priority)
            .ThenBy(static record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();

        return new AccProjectCatalogEntry(best.ProjectId, best.DisplayName ?? best.ProjectId, best.SourceLabel);
    }

    private sealed record RawAccProjectCatalogRecord(
        string ProjectId,
        string? DisplayName,
        string SourceLabel,
        int Priority);
}
