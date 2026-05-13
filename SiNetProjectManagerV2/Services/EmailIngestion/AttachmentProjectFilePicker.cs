using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SiNetProjectManagerV2.Windows;
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

        var roots = BuildRoots(projectFiles, currentProjectFileId);

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

    private static string BuildLeafTitle(ProjectFile pf)
    {
        // Mirror ProjectFile.TagDisplayLabel but without the folder prefix
        // because the folder is already the parent node.
        return string.IsNullOrWhiteSpace(pf.Title) ? "(ללא שם)" : pf.Title!;
    }
}
