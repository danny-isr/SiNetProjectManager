using Microsoft.EntityFrameworkCore;
using SiNet.Application.FileCatalog;
using SiNetSQL.Data;
using SiNetSQL.Models;
using DomainDest = SiNet.Domain.Files.FileStorageDestination;

namespace SiNet.Infrastructure.Sql.Services.FileCatalog;

internal sealed class SqlFileCatalogQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IFileCatalogQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<FileCatalogSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var jobTypes = await db.JobTypes.AsNoTracking()
            .OrderBy(j => j.Title)
            .Select(j => new FileCatalogJobTypeDto(j.Id, j.Title ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var folders = await db.ProjectFolders.AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var files = await db.ProjectFiles.AsNoTracking()
            .OrderBy(f => f.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var extensions = db.AvailableExtensions
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FileCatalogSnapshotDto(
            JobTypes: jobTypes,
            FolderRoots: BuildFolderTree(folders),
            Files: files.Select(ToFileDto).ToList(),
            FileExtensions: extensions);
    }

    private static IReadOnlyList<FileCatalogFolderDto> BuildFolderTree(List<ProjectFolder> all)
    {
        var byParent = all
            .Where(f => f.Infolderid.HasValue)
            .GroupBy(f => f.Infolderid!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Title).ToList());

        var roots = all
            .Where(f => f.Infolderid is null)
            .OrderBy(f => f.Title)
            .Select(r => BuildNode(r, byParent))
            .ToList();

        return roots;
    }

    private static FileCatalogFolderDto BuildNode(
        ProjectFolder folder,
        IReadOnlyDictionary<int, List<ProjectFolder>> byParent)
    {
        var children = byParent.TryGetValue(folder.Id, out var list)
            ? list.Select(c => BuildNode(c, byParent)).ToList()
            : new List<FileCatalogFolderDto>();

        return new FileCatalogFolderDto(
            FolderId: folder.Id,
            Title: folder.Title ?? string.Empty,
            ParentFolderId: folder.Infolderid,
            Children: children);
    }

    private static FileCatalogFileDto ToFileDto(ProjectFile f) =>
        new(
            FileId: f.Id,
            Title: f.Title,
            Number: f.Number,
            Typefile: f.Typefile,
            LookAtDes: f.LookAtDes,
            OutSidData: f.OutSidData,
            StorageDestination: (DomainDest)(int)f.StorageDestination,
            TemplateLocation: f.TemplateLocation,
            Description: f.Des,
            FolderId: f.Folderid,
            JobTypeId: f.TypeProjId,
            IsRequired: f.IsRequired,
            Code: f.Code);
}
