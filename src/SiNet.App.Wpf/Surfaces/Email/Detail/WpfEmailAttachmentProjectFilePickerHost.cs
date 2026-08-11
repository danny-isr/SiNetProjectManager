using System.Windows;
using SiNet.App.Wpf.Shared.Pickers;
using FontWeights = System.Windows.FontWeights;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

/// <summary>
/// Standalone host for attachment → project-file tagging using the shared hierarchical
/// <see cref="FileTreePickerWindow"/> (same UX as V2 AttachmentProjectFilePicker).
/// </summary>
internal sealed class WpfEmailAttachmentProjectFilePickerHost(IEmailAttachmentTaggingService taggingService)
    : IEmailAttachmentProjectFilePickerHost
{
    private const string NoFolderGroupTitle = "ללא תיקייה";
    private const string AllTypesTitle = "כל הסוגים";
    private const string ProjectRootSentinelTitle = "תיקית הפרויקט";

    private readonly IEmailAttachmentTaggingService _taggingService =
        taggingService ?? throw new ArgumentNullException(nameof(taggingService));

    public bool IsAvailable => true;

    public async Task<int?> PickProjectFileAsync(
        int projectId,
        int? currentProjectFileId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return null;
        }

        var catalog = await _taggingService
            .LoadTagPickerCatalogAsync(cancellationToken)
            .ConfigureAwait(true);

        WorkflowDebugTrace.Step(
            "Email.TagUI",
            $"H-TAG0 tree-picker project={projectId} files={catalog.Files.Count} folders={catalog.Folders.Count} jobTypes={catalog.JobTypes.Count} current={currentProjectFileId?.ToString() ?? "null"}");

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return null;
        }

        if (dispatcher.CheckAccess())
        {
            return ShowDialog(catalog, currentProjectFileId);
        }

        return await dispatcher
            .InvokeAsync(() => ShowDialog(catalog, currentProjectFileId))
            .Task
            .ConfigureAwait(true);
    }

    private static int? ShowDialog(
        EmailAttachmentTagPickerCatalog catalog,
        int? currentProjectFileId)
    {
        if (catalog.Files.Count == 0)
        {
            MessageBox.Show(
                "לא נמצאו קבצי חומר חיצוני (OutSidData) לבחירה.",
                "בחר קובץ פרויקט לשיוך",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        var folderMap = catalog.Folders.ToDictionary(f => f.FolderId);
        var roots = BuildHierarchicalRoots(
            catalog.Files,
            folderMap,
            currentProjectFileId,
            includeTypePrefix: true);

        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current?.MainWindow;

        var window = new FileTreePickerWindow(
            roots,
            FilePickerSelectionMode.Single,
            headerText: "בחר קובץ פרויקט (חומר חיצוני) לשיוך הקובץ המצורף.")
        {
            Title = "בחר קובץ פרויקט לשיוך",
            Owner = owner is { IsVisible: true } ? owner : null,
        };

        if (catalog.JobTypes.Count > 0)
        {
            var options = new List<FileTreePickerWindow.TypeFilterOption>
            {
                new(null, AllTypesTitle),
            };
            options.AddRange(catalog.JobTypes.Select(jt =>
                new FileTreePickerWindow.TypeFilterOption(jt.Id, jt.Title)));

            window.ConfigureTypeFilter(options, typeProjId =>
            {
                var filtered = typeProjId is null
                    ? catalog.Files
                    : catalog.Files.Where(pf => pf.TypeProjId == typeProjId).ToList();

                var rebuilt = BuildHierarchicalRoots(
                    filtered,
                    folderMap,
                    currentProjectFileId,
                    includeTypePrefix: typeProjId is null);
                window.ReplaceRoots(rebuilt);
            });
        }

        return window.ShowDialog() == true
            ? window.SelectedTags.OfType<int>().Select(id => (int?)id).FirstOrDefault()
            : null;
    }

    private static List<FileTreePickerWindow.PickerNode> BuildHierarchicalRoots(
        IReadOnlyList<EmailAttachmentTagPickerFile> projectFiles,
        IReadOnlyDictionary<int, EmailAttachmentTagPickerFolder> folderMap,
        int? currentProjectFileId,
        bool includeTypePrefix)
    {
        var usedFolderIds = new HashSet<int>();
        foreach (var pf in projectFiles)
        {
            var folderId = pf.FolderId;
            while (folderId is int id && folderMap.TryGetValue(id, out var folder))
            {
                if (!usedFolderIds.Add(id))
                {
                    break;
                }

                folderId = folder.ParentFolderId;
            }
        }

        var nodes = new Dictionary<int, FileTreePickerWindow.PickerNode>();
        foreach (var id in usedFolderIds)
        {
            var folder = folderMap[id];
            nodes[id] = new FileTreePickerWindow.PickerNode
            {
                Kind = FileTreePickerWindow.PickerNodeKind.Folder,
                Title = folder.Title,
                Icon = "📁",
                IsSelectable = false,
                ShowCheckBox = false,
                IsExpanded = true,
                TitleWeight = FontWeights.SemiBold,
            };
        }

        var roots = new List<FileTreePickerWindow.PickerNode>();
        foreach (var id in usedFolderIds)
        {
            var folder = folderMap[id];
            var node = nodes[id];
            var parentId = folder.ParentFolderId;
            EmailAttachmentTagPickerFolder? parent = parentId is int pid && folderMap.TryGetValue(pid, out var p)
                ? p
                : null;

            var parentIsSentinel = parent != null &&
                string.Equals(parent.Title.Trim(), ProjectRootSentinelTitle, StringComparison.Ordinal);
            var parentInTree = parent != null && nodes.ContainsKey(parent.FolderId);

            if (parent is null || parentIsSentinel || !parentInTree)
            {
                roots.Add(node);
            }
            else
            {
                nodes[parent.FolderId].Children.Add(node);
            }
        }

        FileTreePickerWindow.PickerNode? orphanRoot = null;
        foreach (var pf in projectFiles.OrderBy(p => p.Number).ThenBy(p => p.Title))
        {
            FileTreePickerWindow.PickerNode parentNode;
            if (pf.FolderId is int fid && nodes.TryGetValue(fid, out var found))
            {
                parentNode = found;
            }
            else
            {
                orphanRoot ??= new FileTreePickerWindow.PickerNode
                {
                    Kind = FileTreePickerWindow.PickerNodeKind.Folder,
                    Title = NoFolderGroupTitle,
                    Icon = "📁",
                    IsSelectable = false,
                    ShowCheckBox = false,
                    IsExpanded = true,
                    TitleWeight = FontWeights.SemiBold,
                };
                if (!roots.Contains(orphanRoot))
                {
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
                Tag = pf.ProjectFileId,
                IsChecked = currentProjectFileId.HasValue && pf.ProjectFileId == currentProjectFileId.Value,
                TitleBrush = pf.IsRequired
                    ? ResolveRequiredBrush()
                    : System.Windows.Media.Brushes.Black,
                TitleWeight = pf.IsRequired ? FontWeights.SemiBold : FontWeights.Normal,
            });
        }

        SortRecursive(roots);
        return roots;
    }

    private static System.Windows.Media.Brush ResolveRequiredBrush()
    {
        try
        {
            if (System.Windows.Application.Current?.TryFindResource("SiTreeMissingBrush") is System.Windows.Media.Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            // Design-time / headless: fall through to solid orange.
        }

        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0x58, 0x0C));
    }

    private static void SortRecursive(List<FileTreePickerWindow.PickerNode> siblings)
    {
        siblings.Sort(CompareNodes);
        foreach (var n in siblings)
        {
            var list = n.Children.ToList();
            list.Sort(CompareNodes);
            n.Children.Clear();
            foreach (var c in list)
            {
                n.Children.Add(c);
            }

            SortRecursive(list);
        }
    }

    private static int CompareNodes(FileTreePickerWindow.PickerNode a, FileTreePickerWindow.PickerNode b)
    {
        var kindA = a.Kind == FileTreePickerWindow.PickerNodeKind.Folder ? 0 : 1;
        var kindB = b.Kind == FileTreePickerWindow.PickerNodeKind.Folder ? 0 : 1;
        if (kindA != kindB)
        {
            return kindA - kindB;
        }

        if (kindA == 0)
        {
            var orphanA = a.Title == NoFolderGroupTitle ? 1 : 0;
            var orphanB = b.Title == NoFolderGroupTitle ? 1 : 0;
            if (orphanA != orphanB)
            {
                return orphanA - orphanB;
            }
        }

        return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
    }

    private static string BuildLeafTitle(EmailAttachmentTagPickerFile pf, bool includeTypePrefix)
    {
        if (!includeTypePrefix)
        {
            return pf.Title;
        }

        return string.IsNullOrWhiteSpace(pf.TypeTitle)
            ? pf.Title
            : $"[{pf.TypeTitle}] {pf.Title}";
    }
}
