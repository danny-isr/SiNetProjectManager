using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using SiNetProjectManagerV2.Windows;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Services.EmailIngestion;

/// <summary>
/// WPF implementation of <see cref="IAttachmentProjectFilePicker"/>. Builds a
/// folder → ProjectFile tree from all OutSidData catalog rows (all project types),
/// with an optional JobType filter in the shared <see cref="FileTreePickerWindow"/>.
/// </summary>
internal sealed class AttachmentProjectFilePicker : IAttachmentProjectFilePicker
{
    private const string NoFolderGroupTitle = "ללא תיקייה";
    private const string AllTypesTitle = "כל הסוגים";

    private readonly AttachmentTaggingService _taggingService;

    public AttachmentProjectFilePicker() : this(new AttachmentTaggingService()) { }

    public AttachmentProjectFilePicker(AttachmentTaggingService taggingService)
    {
        _taggingService = taggingService;
    }

    public async Task<int?> PickAsync(
        int projectId,
        int? currentProjectFileId,
        CancellationToken cancellationToken = default)
    {
        var projectFiles = await _taggingService
            .LoadStrictExternalAsync(projectId, typeProjIdFilter: null, cancellationToken)
            .ConfigureAwait(true);

        var jobTypes = await _taggingService
            .LoadExternalMaterialJobTypesAsync(cancellationToken)
            .ConfigureAwait(true);

        AppLogger.Debug(
            $"[AttachmentProjectFilePicker] projectId={projectId} " +
            $"loaded={projectFiles.Count} jobTypes={jobTypes.Count} " +
            $"currentProjectFileId={currentProjectFileId?.ToString() ?? "(null)"}");

        var folderMap = await LoadFolderAncestorsAsync(projectFiles, cancellationToken)
            .ConfigureAwait(true);

        int? picked = null;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            picked = ShowDialog(projectFiles, folderMap, jobTypes, currentProjectFileId);
        }
        else
        {
            dispatcher.Invoke(() =>
            {
                picked = ShowDialog(projectFiles, folderMap, jobTypes, currentProjectFileId);
            });
        }

        AppLogger.Debug(
            $"[AttachmentProjectFilePicker] result={(picked.HasValue ? picked.Value.ToString() : "Cancelled")}");
        return picked;
    }

    private static int? ShowDialog(
        IReadOnlyList<ProjectFile> allFiles,
        IReadOnlyDictionary<int, ProjectFolder> folderMap,
        IReadOnlyList<(int Id, string Title)> jobTypes,
        int? currentProjectFileId)
    {
        var roots = BuildHierarchicalRoots(
            allFiles,
            folderMap,
            currentProjectFileId,
            includeTypePrefix: true);

        var window = new FileTreePickerWindow(
            roots,
            FilePickerSelectionMode.Single,
            headerText: "בחר קובץ פרויקט (חומר חיצוני) לשיוך הקובץ המצורף.")
        {
            Title = "בחר קובץ פרויקט לשיוך",
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow
        };

        if (jobTypes.Count > 0)
        {
            var options = new List<FileTreePickerWindow.TypeFilterOption>
            {
                new(null, AllTypesTitle),
            };
            options.AddRange(jobTypes.Select(jt => new FileTreePickerWindow.TypeFilterOption(jt.Id, jt.Title)));

            window.ConfigureTypeFilter(options, typeProjId =>
            {
                var filtered = typeProjId is null
                    ? allFiles
                    : allFiles.Where(pf => pf.TypeProjId == typeProjId).ToList();

                var rebuilt = BuildHierarchicalRoots(
                    filtered,
                    folderMap,
                    currentProjectFileId,
                    includeTypePrefix: typeProjId is null);
                window.ReplaceRoots(rebuilt);
            });
        }

        var ok = window.ShowDialog() == true;
        if (!ok) return null;

        return window.SelectedTags
            .OfType<int>()
            .Select(id => (int?)id)
            .FirstOrDefault();
    }

    private static async Task<Dictionary<int, ProjectFolder>> LoadFolderAncestorsAsync(
        IReadOnlyList<ProjectFile> projectFiles,
        CancellationToken ct)
    {
        var map = new Dictionary<int, ProjectFolder>();

        var seedFolders = projectFiles
            .Select(pf => pf.Folder)
            .Where(f => f != null)
            .Cast<ProjectFolder>()
            .GroupBy(f => f.Id)
            .Select(g => g.First())
            .ToList();

        foreach (var f in seedFolders)
            map[f.Id] = f;

        var pending = seedFolders
            .Select(f => f.Infolderid)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Where(id => !map.ContainsKey(id))
            .Distinct()
            .ToList();

        if (pending.Count == 0)
            return map;

        await using var db = new SiNetSQLDbContext();

        while (pending.Count > 0)
        {
            var batch = await db.ProjectFolders
                .AsNoTracking()
                .Where(f => pending.Contains(f.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var f in batch)
                map[f.Id] = f;

            pending = batch
                .Select(f => f.Infolderid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Where(id => !map.ContainsKey(id))
                .Distinct()
                .ToList();
        }

        return map;
    }

    private static List<FileTreePickerWindow.PickerNode> BuildHierarchicalRoots(
        IReadOnlyList<ProjectFile> projectFiles,
        IReadOnlyDictionary<int, ProjectFolder> folderMap,
        int? currentProjectFileId,
        bool includeTypePrefix)
    {
        const string projectRootSentinelTitle = "תיקית הפרויקט";

        // Only include folders that appear under the filtered file set.
        var usedFolderIds = new HashSet<int>();
        foreach (var pf in projectFiles)
        {
            var folderId = pf.Folder?.Id ?? pf.Folderid;
            while (folderId is int id && folderMap.TryGetValue(id, out var folder))
            {
                if (!usedFolderIds.Add(id))
                    break;
                folderId = folder.Infolderid;
            }
        }

        var nodes = new Dictionary<int, FileTreePickerWindow.PickerNode>();
        foreach (var id in usedFolderIds)
        {
            var folder = folderMap[id];
            nodes[id] = new FileTreePickerWindow.PickerNode
            {
                Kind = FileTreePickerWindow.PickerNodeKind.Folder,
                Title = string.IsNullOrWhiteSpace(folder.Title) ? "(תיקייה ללא שם)" : folder.Title!,
                Icon = "📁",
                IsSelectable = false,
                ShowCheckBox = false,
                IsExpanded = true,
                TitleWeight = System.Windows.FontWeights.SemiBold
            };
        }

        var roots = new List<FileTreePickerWindow.PickerNode>();
        foreach (var id in usedFolderIds)
        {
            var folder = folderMap[id];
            var node = nodes[id];
            var parentId = folder.Infolderid;
            ProjectFolder? parent = parentId.HasValue && folderMap.TryGetValue(parentId.Value, out var p)
                ? p
                : null;

            bool parentIsSentinel = parent != null &&
                string.Equals(parent.Title?.Trim(), projectRootSentinelTitle, System.StringComparison.Ordinal);
            bool parentInTree = parent != null && nodes.ContainsKey(parent.Id);

            if (parent == null || parentIsSentinel || !parentInTree)
                roots.Add(node);
            else
                nodes[parent.Id].Children.Add(node);
        }

        FileTreePickerWindow.PickerNode? orphanRoot = null;
        foreach (var pf in projectFiles.OrderBy(p => p.Number).ThenBy(p => p.Title))
        {
            FileTreePickerWindow.PickerNode parentNode;
            var folderId = pf.Folder?.Id ?? pf.Folderid;
            if (folderId is int fid && nodes.TryGetValue(fid, out var found))
            {
                parentNode = found;
            }
            else
            {
                if (orphanRoot == null)
                {
                    orphanRoot = new FileTreePickerWindow.PickerNode
                    {
                        Kind = FileTreePickerWindow.PickerNodeKind.Folder,
                        Title = NoFolderGroupTitle,
                        Icon = "📁",
                        IsSelectable = false,
                        ShowCheckBox = false,
                        IsExpanded = true,
                        TitleWeight = System.Windows.FontWeights.SemiBold
                    };
                    roots.Add(orphanRoot);
                }
                parentNode = orphanRoot;
            }

            parentNode.Children.Add(new FileTreePickerWindow.PickerNode
            {
                Kind = FileTreePickerWindow.PickerNodeKind.File,
                Title = BuildLeafTitle(pf, includeTypePrefix),
                Icon = "📄",
                IsSelectable = true,
                ShowCheckBox = true,
                Tag = pf.Id,
                IsChecked = currentProjectFileId.HasValue && pf.Id == currentProjectFileId.Value
            });
        }

        SortRecursive(roots);
        return roots;
    }

    private static void SortRecursive(List<FileTreePickerWindow.PickerNode> siblings)
    {
        siblings.Sort(CompareNodes);
        foreach (var n in siblings)
        {
            var list = n.Children.ToList();
            list.Sort(CompareNodes);
            n.Children.Clear();
            foreach (var c in list) n.Children.Add(c);
            SortRecursive(list);
        }
    }

    private static int CompareNodes(FileTreePickerWindow.PickerNode a, FileTreePickerWindow.PickerNode b)
    {
        int kindA = a.Kind == FileTreePickerWindow.PickerNodeKind.Folder ? 0 : 1;
        int kindB = b.Kind == FileTreePickerWindow.PickerNodeKind.Folder ? 0 : 1;
        if (kindA != kindB) return kindA - kindB;

        if (kindA == 0)
        {
            int orphanA = a.Title == NoFolderGroupTitle ? 1 : 0;
            int orphanB = b.Title == NoFolderGroupTitle ? 1 : 0;
            if (orphanA != orphanB) return orphanA - orphanB;
        }

        return string.Compare(a.Title, b.Title, System.StringComparison.CurrentCulture);
    }

    private static string BuildLeafTitle(ProjectFile pf, bool includeTypePrefix)
    {
        var title = string.IsNullOrWhiteSpace(pf.Title) ? "(ללא שם)" : pf.Title!;
        if (!includeTypePrefix)
            return title;

        var typeTitle = pf.TypeProj?.Title?.Trim();
        return string.IsNullOrWhiteSpace(typeTitle) ? title : $"[{typeTitle}] {title}";
    }
}
