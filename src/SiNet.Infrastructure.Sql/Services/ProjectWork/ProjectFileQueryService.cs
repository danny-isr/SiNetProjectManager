using System.IO;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.ProjectWork;
using SiNetSQL.Data;
using SiNetSQL.Models;
using DomainDest = SiNet.Domain.Files.FileStorageDestination;

namespace SiNet.Infrastructure.Sql.Services.ProjectWork;

/// <summary>
/// EF-backed <see cref="IProjectFileQueryService"/>. Builds a project's DB-defined folder/file skeleton
/// as clean DTOs, reproducing the legacy <c>ProjectWorkViewModel.LoadUnifiedTree</c> DB rules:
/// folders come from the shared template tree (excluding the synthetic project-root folder), and file
/// definitions are the project's <c>ProjectFile</c> rows filtered by the project's configured types.
/// </summary>
public sealed class ProjectFileQueryService : IProjectFileQueryService
{
    /// <summary>Synthetic DB root folder whose direct children are the per-project root folders.</summary>
    private const string ProjectRootFolderTitle = "\u05EA\u05D9\u05E7\u05D9\u05EA \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8";

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public ProjectFileQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<ProjectFileTreeDto?> GetProjectFileTreeAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
            return null;

        var projectNumber = (int)(project.Number ?? 0);

        var allFolders = await db.ProjectFolders.AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var syntheticRootIds = allFolders
            .Where(f => f.Title == ProjectRootFolderTitle)
            .Select(f => f.Id)
            .ToHashSet();

        var childrenByParent = allFolders
            .Where(f => f.Infolderid.HasValue)
            .GroupBy(f => f.Infolderid!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var validTypes = (await db.TypeOfProjectInProjects.AsNoTracking()
                .Where(t => t.ProjectId == projectId && t.ProjectTypeId != null)
                .Select(t => t.ProjectTypeId!.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        var filesByFolder = (await db.ProjectFiles.AsNoTracking()
                .Where(f => f.Folderid != null && f.TypeProjId != null && validTypes.Contains(f.TypeProjId!.Value))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(f => f.Folderid!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var roots = allFolders
            .Where(f => f.Infolderid.HasValue && syntheticRootIds.Contains(f.Infolderid.Value))
            .OrderBy(f => f.Title)
            .ToList();

        var rootDtos = roots.Select(r => BuildFolder(r, childrenByParent, filesByFolder, syntheticRootIds)).ToList();

        return new ProjectFileTreeDto(projectId, projectNumber, rootDtos);
    }

    private static ProjectFolderDto BuildFolder(
        ProjectFolder folder,
        IReadOnlyDictionary<int, List<ProjectFolder>> childrenByParent,
        IReadOnlyDictionary<int, List<ProjectFile>> filesByFolder,
        IReadOnlySet<int> syntheticRootIds)
    {
        var childDtos = new List<ProjectFolderDto>();
        if (childrenByParent.TryGetValue(folder.Id, out var children))
        {
            foreach (var child in children.OrderBy(c => c.Title))
            {
                if (syntheticRootIds.Contains(child.Id))
                    continue;
                childDtos.Add(BuildFolder(child, childrenByParent, filesByFolder, syntheticRootIds));
            }
        }

        var fileDtos = new List<ProjectFileDefinitionDto>();
        if (filesByFolder.TryGetValue(folder.Id, out var files))
        {
            foreach (var f in files.OrderBy(f => f.Title))
                fileDtos.Add(ToFileDto(f));
        }

        return new ProjectFolderDto(
            FolderId: folder.Id,
            Name: folder.Title ?? string.Empty,
            ParentFolderId: folder.Infolderid,
            Children: childDtos,
            Files: fileDtos);
    }

    private static ProjectFileDefinitionDto ToFileDto(ProjectFile file) => new(
        FileId: file.Id,
        BaseName: file.Title ?? string.Empty,
        Extension: string.IsNullOrEmpty(file.Title) ? string.Empty : Path.GetExtension(file.Title),
        StorageDestination: (DomainDest)(int)file.StorageDestination,
        FolderId: file.Folderid ?? 0,
        ProjectType: file.TypeProjId,
        Number: file.Number.HasValue ? (int)file.Number.Value : null,
        TemplateLocation: string.IsNullOrWhiteSpace(file.TemplateLocation) ? null : file.TemplateLocation);
}
