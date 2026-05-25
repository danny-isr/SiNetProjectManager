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
/// folder → ProjectFile tree directly from the DB (via
/// <see cref="AttachmentTaggingService.LoadStrictExternalAsync"/>) and shows it
/// in the shared <see cref="FileTreePickerWindow"/> using its pre-built-tree
/// constructor.
///
/// Differences from the Inspection / ReviewedPlans path:
/// <list type="bullet">
/// <item>Source = DB rows, not the live work-window tree.</item>
/// <item>Includes planned ProjectFiles that have no physical instance yet.</item>
/// <item>Strictly filters to <c>OutSidData == true</c>.</item>
/// <item>Selection returns a <c>ProjectFile.Id</c> via <see cref="FileTreePickerWindow.SelectedTags"/>.</item>
/// </list>
/// </summary>
internal sealed class AttachmentProjectFilePicker : IAttachmentProjectFilePicker
{
    private const string NoFolderGroupTitle = "ללא תיקייה";

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
            .LoadStrictExternalAsync(projectId, cancellationToken)
            .ConfigureAwait(true);

        AppLogger.Debug(
            $"[AttachmentProjectFilePicker] projectId={projectId} " +
            $"loaded={projectFiles.Count} currentProjectFileId={currentProjectFileId?.ToString() ?? "(null)"}");

        // Load ancestor ProjectFolder chain so we can build a real hierarchical
        // tree (folder → sub-folder → … → file). The ProjectFiles loaded above
        // only carry their immediate Folder via Include; parents are resolved
        // here in one extra DB pass keyed by Folder.Id + Infolderid.
        var folderMap = await LoadFolderAncestorsAsync(projectFiles, cancellationToken)
            .ConfigureAwait(true);

        var roots = BuildHierarchicalRoots(projectFiles, folderMap, currentProjectFileId);

        int? picked = null;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            picked = ShowDialog(roots);
        }
        else
        {
            dispatcher.Invoke(() => { picked = ShowDialog(roots); });
        }

        AppLogger.Debug(
            $"[AttachmentProjectFilePicker] result={(picked.HasValue ? picked.Value.ToString() : "Cancelled")}");
        return picked;
    }

    private static int? ShowDialog(List<FileTreePickerWindow.PickerNode> roots)
    {
        var window = new FileTreePickerWindow(
            roots,
            FilePickerSelectionMode.Single,
            headerText: "בחר קובץ פרויקט (חומר חיצוני) לשיוך הקובץ המצורף.")
        {
            Title = "בחר קובץ פרויקט לשיוך",
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow
        };

        var ok = window.ShowDialog() == true;
        if (!ok) return null;

        return window.SelectedTags
            .OfType<int>()
            .Select(id => (int?)id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Loads every <see cref="ProjectFolder"/> referenced by the supplied
    /// ProjectFiles plus the full ancestor chain (walking <c>Infolderid</c>),
    /// so the hierarchical tree builder can reconstruct intermediate folders
    /// even when no ProjectFile is attached directly to them.
    /// </summary>
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

    /// <summary>
    /// Builds a hierarchical folder tree from <see cref="ProjectFolder.Infolderid"/>
    /// parent links. Each ProjectFile is attached as a leaf to its own folder
    /// node. Files without a folder fall back to a synthetic "ללא תיקייה" root.
    /// The legacy synthetic top-level container "תיקית הפרויקט" (used elsewhere
    /// as a sentinel root) is treated as the project root and is not rendered;
    /// its direct children become the visible roots of the picker tree.
    /// </summary>
    private static List<FileTreePickerWindow.PickerNode> BuildHierarchicalRoots(
        IReadOnlyList<ProjectFile> projectFiles,
        IReadOnlyDictionary<int, ProjectFolder> folderMap,
        int? currentProjectFileId)
    {
        const string projectRootSentinelTitle = "תיקית הפרויקט";

        // 1) Create a PickerNode for every folder we know about.
        var nodes = new Dictionary<int, FileTreePickerWindow.PickerNode>();
        foreach (var (id, folder) in folderMap)
        {
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

        // 2) Wire parent/child relationships. A folder whose parent is missing,
        //    null, or the project-root sentinel becomes a visible root.
        var roots = new List<FileTreePickerWindow.PickerNode>();
        foreach (var (id, folder) in folderMap)
        {
            var node = nodes[id];
            var parentId = folder.Infolderid;
            ProjectFolder? parent = parentId.HasValue && folderMap.TryGetValue(parentId.Value, out var p)
                ? p
                : null;

            bool parentIsSentinel = parent != null &&
                string.Equals(parent.Title?.Trim(), projectRootSentinelTitle, System.StringComparison.Ordinal);

            if (parent == null || parentIsSentinel)
                roots.Add(node);
            else
                nodes[parent.Id].Children.Add(node);
        }

        // 3) Attach ProjectFile leaves to their own folder node. Files whose
        //    folder is unknown go into a synthetic "ללא תיקייה" root so they
        //    remain selectable.
        FileTreePickerWindow.PickerNode? orphanRoot = null;
        foreach (var pf in projectFiles.OrderBy(p => p.Number).ThenBy(p => p.Title))
        {
            FileTreePickerWindow.PickerNode parentNode;
            if (pf.Folder != null && nodes.TryGetValue(pf.Folder.Id, out var found))
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
                Title = BuildLeafTitle(pf),
                Icon = "📄",
                IsSelectable = true,
                ShowCheckBox = true,
                Tag = pf.Id,
                IsChecked = currentProjectFileId.HasValue && pf.Id == currentProjectFileId.Value
            });
        }

        // 4) Sort siblings: real folders first, then orphan bucket; alphabetic
        //    within each level.
        SortRecursive(roots);

        return roots;
    }

    private static void SortRecursive(List<FileTreePickerWindow.PickerNode> siblings)
    {
        siblings.Sort(CompareNodes);
        foreach (var n in siblings)
        {
            // PickerNode.Children is ObservableCollection<PickerNode>; sort in
            // place via a temp list to preserve identity.
            var list = n.Children.ToList();
            list.Sort(CompareNodes);
            n.Children.Clear();
            foreach (var c in list) n.Children.Add(c);
        }
    }

    private static int CompareNodes(FileTreePickerWindow.PickerNode a, FileTreePickerWindow.PickerNode b)
    {
        // Folders before files; orphan bucket last among folders.
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

    // ──────────────────────────────────────────────────────────────────────
    // LEGACY: flat folder list (suspended — replaced by hierarchical tree).
    //
    // The original implementation grouped ProjectFiles by their immediate
    // folder title only, producing a single-level list of folders regardless
    // of the actual ProjectFolder hierarchy. It is preserved here, commented
    // out, until the hierarchical tree above has been validated in the field.
    // Candidate for removal after sign-off — do not re-enable as a "safety
    // fallback" without explicit approval.
    // ──────────────────────────────────────────────────────────────────────
    /*
    private static List<FileTreePickerWindow.PickerNode> BuildRoots(
        IReadOnlyList<ProjectFile> projectFiles,
        int? currentProjectFileId)
    {
        // Group by folder title (DB-driven). ProjectFiles without a folder go
        // into a synthetic "ללא תיקייה" group so they are still selectable.
        var byFolder = projectFiles
            .GroupBy(pf => pf.Folder?.Title?.Trim() is { Length: > 0 } t ? t : NoFolderGroupTitle)
            .OrderBy(g => g.Key == NoFolderGroupTitle ? 1 : 0) // real folders first
            .ThenBy(g => g.Key);

        var roots = new List<FileTreePickerWindow.PickerNode>();

        foreach (var grp in byFolder)
        {
            var folderNode = new FileTreePickerWindow.PickerNode
            {
                Kind = FileTreePickerWindow.PickerNodeKind.Folder,
                Title = grp.Key,
                Icon = "📁",
                IsSelectable = false,
                ShowCheckBox = false,
                IsExpanded = true,
                TitleWeight = System.Windows.FontWeights.SemiBold
            };

            foreach (var pf in grp.OrderBy(p => p.Number).ThenBy(p => p.Title))
            {
                var leaf = new FileTreePickerWindow.PickerNode
                {
                    Kind = FileTreePickerWindow.PickerNodeKind.File,
                    Title = BuildLeafTitle(pf),
                    Icon = "📄",
                    IsSelectable = true,
                    ShowCheckBox = true,
                    Tag = pf.Id,
                    IsChecked = currentProjectFileId.HasValue && pf.Id == currentProjectFileId.Value
                };
                folderNode.Children.Add(leaf);
            }

            if (folderNode.Children.Count > 0)
                roots.Add(folderNode);
        }

        return roots;
    }
    */

    private static string BuildLeafTitle(ProjectFile pf)
    {
        // Mirror ProjectFile.TagDisplayLabel but without the folder prefix
        // because the folder is already the parent node.
        return string.IsNullOrWhiteSpace(pf.Title) ? "(ללא שם)" : pf.Title!;
    }
}
