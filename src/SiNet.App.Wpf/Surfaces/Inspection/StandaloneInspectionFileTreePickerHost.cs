using System.Windows;
using SiNet.App.Wpf.Shared.Pickers;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.ProjectWork;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Standalone file-tree picker for reviewed plans / note links.
/// Prefers the live ProjectWork hub when registered; otherwise loads the project file
/// skeleton from <see cref="IProjectFileQueryService"/> so Inspection does not require
/// opening «בעבודה 2» first.
/// </summary>
internal sealed class StandaloneInspectionFileTreePickerHost(
    IActiveFileQueryHub activeFiles,
    ICurrentProjectContext currentProject,
    IProjectFileQueryService projectFiles) : IInspectionFileTreePickerHost
{
    private readonly IActiveFileQueryHub _activeFiles =
        activeFiles ?? throw new ArgumentNullException(nameof(activeFiles));
    private readonly ICurrentProjectContext _currentProject =
        currentProject ?? throw new ArgumentNullException(nameof(currentProject));
    private readonly IProjectFileQueryService _projectFiles =
        projectFiles ?? throw new ArgumentNullException(nameof(projectFiles));

    public Task<IReadOnlyList<InspectionFilePickResult>?> PickReviewedPlansAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        PickAsync(projectId, multiSelect: true, "בחר תוכניות שנבדקו", cancellationToken);

    public async Task<InspectionFilePickResult?> PickNoteLinkedFileAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        var list = await PickAsync(projectId, multiSelect: false, "בחר קובץ מקושר להערה", cancellationToken)
            .ConfigureAwait(true);
        if (list is null || list.Count == 0)
            return null;
        return list[0];
    }

    private async Task<IReadOnlyList<InspectionFilePickResult>?> PickAsync(
        int projectId,
        bool multiSelect,
        string title,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId <= 0)
            return null;

        if (_currentProject.CurrentProject is not { } project || project.ProjectId != projectId)
        {
            MessageBox.Show(
                "בחירת קובץ מותרת רק עבור הפרויקט הנוכחי שנבחר במעטפת.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }

        var expectedNumber = project.ProjectNumber?.Trim();
        List<FileTreePickerWindow.PickerNode> roots;
        if (_activeFiles.IsAvailable)
        {
            roots = BuildPickerRoots(_activeFiles.GetActiveFolderTree(), expectedNumber);
        }
        else
        {
            var tree = await _projectFiles.GetProjectFileTreeAsync(projectId, cancellationToken)
                .ConfigureAwait(true);
            roots = BuildPickerRootsFromProjectTree(tree);
        }

        if (roots.Count == 0)
        {
            MessageBox.Show(
                "לא נמצאו קבצים בעץ הפרויקט לבחירה.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return null;

        if (dispatcher.CheckAccess())
            return ShowDialog(roots, multiSelect, title);

        return await dispatcher
            .InvokeAsync(() => ShowDialog(roots, multiSelect, title))
            .Task
            .ConfigureAwait(true);
    }

    private static IReadOnlyList<InspectionFilePickResult>? ShowDialog(
        IReadOnlyList<FileTreePickerWindow.PickerNode> roots,
        bool multiSelect,
        string title)
    {
        var window = new FileTreePickerWindow(
            roots,
            multiSelect ? FilePickerSelectionMode.Multiple : FilePickerSelectionMode.Single,
            title);

        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible)
            window.Owner = owner;

        var ok = window.ShowDialog() == true;
        if (!ok)
            return null;

        return window.SelectedTags
            .OfType<InspectionFilePickResult>()
            .ToList();
    }

    /// <summary>Maps DB project-file skeleton to picker nodes (no Project Work UI required).</summary>
    internal static List<FileTreePickerWindow.PickerNode> BuildPickerRootsFromProjectTree(
        ProjectFileTreeDto? tree)
    {
        var roots = new List<FileTreePickerWindow.PickerNode>();
        if (tree is null)
            return roots;

        foreach (var folder in tree.RootFolders)
        {
            var node = MapProjectFolder(folder);
            if (node is not null)
                roots.Add(node);
        }

        return roots;
    }

    private static FileTreePickerWindow.PickerNode? MapProjectFolder(ProjectFolderDto folder)
    {
        var children = new List<FileTreePickerWindow.PickerNode>();
        foreach (var child in folder.Children)
        {
            var mapped = MapProjectFolder(child);
            if (mapped is not null)
                children.Add(mapped);
        }

        foreach (var file in folder.Files)
        {
            var displayName = string.IsNullOrWhiteSpace(file.Extension)
                ? file.BaseName
                : file.BaseName.EndsWith(file.Extension, StringComparison.OrdinalIgnoreCase)
                    ? file.BaseName
                    : file.BaseName + file.Extension;

            children.Add(new FileTreePickerWindow.PickerNode
            {
                Kind = FileTreePickerWindow.PickerNodeKind.File,
                Title = displayName,
                IsSelectable = true,
                Tag = new InspectionFilePickResult(displayName, Alternative: "1", Version: null, FullPath: null),
            });
        }

        if (children.Count == 0)
            return null;

        var folderNode = new FileTreePickerWindow.PickerNode
        {
            Kind = FileTreePickerWindow.PickerNodeKind.Folder,
            Title = folder.Name,
            IsSelectable = false,
        };
        foreach (var child in children)
            folderNode.Children.Add(child);
        return folderNode;
    }

    internal static List<FileTreePickerWindow.PickerNode> BuildPickerRoots(
        IReadOnlyList<ActiveFolderInfo> folders,
        string? expectedProjectNumber)
    {
        var roots = new List<FileTreePickerWindow.PickerNode>();
        foreach (var folder in folders)
        {
            var node = MapFolder(folder, expectedProjectNumber);
            if (node is not null)
                roots.Add(node);
        }

        return roots;
    }

    private static FileTreePickerWindow.PickerNode? MapFolder(
        ActiveFolderInfo folder,
        string? expectedProjectNumber)
    {
        var children = new List<FileTreePickerWindow.PickerNode>();
        foreach (var child in folder.Children)
        {
            var mapped = MapFolder(child, expectedProjectNumber);
            if (mapped is not null)
                children.Add(mapped);
        }

        foreach (var file in folder.Files)
        {
            if (!string.IsNullOrWhiteSpace(expectedProjectNumber)
                && !string.Equals(
                    file.ProjectNumber.ToString(),
                    expectedProjectNumber,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    file.ProjectNumber.ToString().TrimStart('0'),
                    expectedProjectNumber.TrimStart('0'),
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(expectedProjectNumber, out var expectedInt)
                    || file.ProjectNumber != expectedInt)
                {
                    continue;
                }
            }

            var displayName = string.IsNullOrWhiteSpace(file.Extension)
                ? file.FileName
                : file.FileName + file.Extension;

            string? alt = file.Alternatives.Count > 0 ? file.Alternatives[0].AlternativeName : null;
            children.Add(new FileTreePickerWindow.PickerNode
            {
                Kind = FileTreePickerWindow.PickerNodeKind.File,
                Title = displayName,
                IsSelectable = true,
                Tag = new InspectionFilePickResult(displayName, alt, Version: null, FullPath: null),
            });
        }

        if (children.Count == 0)
            return null;

        var folderNode = new FileTreePickerWindow.PickerNode
        {
            Kind = FileTreePickerWindow.PickerNodeKind.Folder,
            Title = folder.Title,
            IsSelectable = false,
        };
        foreach (var child in children)
            folderNode.Children.Add(child);
        return folderNode;
    }
}
