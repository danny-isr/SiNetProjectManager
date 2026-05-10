using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SiNetSQL.Services.ActiveFileQuery;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Windows;

public partial class FileTreePickerWindow : Window
{
    private readonly FilePickerSelectionMode _mode;
    private readonly List<PickerNode> _allSelectableLeaves = new();
    private readonly ObservableCollection<PickerNode> _roots = new();
    private bool _suppressCheckSync;

    /// <summary>Filter stats produced while building the tree.</summary>
    public sealed record FilterStats(
        int TotalFiles,
        int AvailableFiles,
        int SelectableLeafCount,
        int HiddenMissingFiles,
        int DisplayedFolders,
        int DisplayedFiles);

    public FilterStats Stats { get; }

    internal FileTreePickerWindow(FileTreePickerRequest request)
    {
        InitializeComponent();
        _mode = request.SelectionMode;

        HeaderText.Text = request.SelectionMode == FilePickerSelectionMode.Multiple
            ? "סמן את הקבצים/החלופות שיש לבחור."
            : "בחר קובץ/חלופה אחד.";

        var preset = new HashSet<(string FileName, string Alternative)>(
            request.AlreadySelected.Select(s => (s.FileName, s.Alternative ?? string.Empty)));

        int totalFiles = 0, availableFiles = 0, hiddenMissing = 0,
            displayedFolders = 0, displayedFiles = 0;

        foreach (var folder in request.Tree)
        {
            var n = BuildFolderNode(folder, preset,
                ref totalFiles, ref availableFiles, ref hiddenMissing,
                ref displayedFolders, ref displayedFiles);
            if (n != null) _roots.Add(n);
        }

        Stats = new FilterStats(
            TotalFiles: totalFiles,
            AvailableFiles: availableFiles,
            SelectableLeafCount: _allSelectableLeaves.Count,
            HiddenMissingFiles: hiddenMissing,
            DisplayedFolders: displayedFolders,
            DisplayedFiles: displayedFiles);

        Tree.ItemsSource = _roots;

        if (_allSelectableLeaves.Count == 0)
        {
            Tree.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            OkButton.IsEnabled = false;
        }
        else
        {
            UpdateOkEnabled();
        }
    }

    public IReadOnlyList<FilePickerSelection> Selected =>
        _allSelectableLeaves
            .Where(n => n.IsChecked)
            .Select(n => new FilePickerSelection(n.FileName!, n.Alternative ?? string.Empty))
            .ToList();

    // ───── Existence / availability rules ────────────────────────────────

    /// <summary>
    /// A version is "available" (i.e. <see cref="FileOpenServiceRegistry"/>
    /// has enough info to open it) when either:
    /// <list type="bullet">
    /// <item>It is an ACC item (<see cref="ActiveVersionInfo.AccItemId"/> is set), or</item>
    /// <item>It has a non-empty <see cref="ActiveVersionInfo.FullPath"/> that exists on disk.</item>
    /// </list>
    /// Theoretical / DB-only versions return <c>false</c> and are excluded.
    /// </summary>
    private static bool IsVersionAvailable(ActiveVersionInfo v)
    {
        if (!string.IsNullOrWhiteSpace(v.AccItemId)) return true;
        if (string.IsNullOrWhiteSpace(v.FullPath)) return false;
        try { return File.Exists(v.FullPath); }
        catch { return false; }
    }

    private static bool IsAlternativeAvailable(ActiveAlternativeInfo alt)
    {
        if (alt.Versions == null) return false;
        foreach (var v in alt.Versions)
            if (IsVersionAvailable(v)) return true;
        return false;
    }

    // ───── Tree construction ─────────────────────────────────────────────

    private PickerNode? BuildFolderNode(ActiveFolderInfo folder,
        HashSet<(string, string)> preset,
        ref int totalFiles, ref int availableFiles, ref int hiddenMissing,
        ref int displayedFolders, ref int displayedFiles)
    {
        var node = new PickerNode
        {
            Kind = PickerNodeKind.Folder,
            Title = folder.Title,
            Icon = "📁",
            IsSelectable = false,
            ShowCheckBox = false,
            IsExpanded = true,
            TitleWeight = FontWeights.SemiBold
        };

        foreach (var child in folder.Children)
        {
            var c = BuildFolderNode(child, preset,
                ref totalFiles, ref availableFiles, ref hiddenMissing,
                ref displayedFolders, ref displayedFiles);
            if (c != null) node.Children.Add(c);
        }

        foreach (var file in folder.Files)
        {
            totalFiles++;
            if (TryAppendFileNode(node, file, preset))
            {
                availableFiles++;
                displayedFiles++;
            }
            else
            {
                hiddenMissing++;
            }
        }

        if (!HasSelectableDescendant(node)) return null;

        displayedFolders++;
        return node;
    }

    /// <summary>
    /// Adds a file node and its available alternatives. Returns <c>false</c>
    /// when no alternative has any existing version — caller should treat the
    /// whole file as missing and not display it at all.
    /// </summary>
    private bool TryAppendFileNode(PickerNode parent, ActiveFileInfo file,
        HashSet<(string, string)> preset)
    {
        var fileTitle = string.IsNullOrEmpty(file.Extension)
            ? file.FileName
            : $"{file.FileName}{file.Extension}";

        // No alternatives: nothing concrete to open, hide.
        if (file.Alternatives == null || file.Alternatives.Count == 0)
            return false;

        var availableAlts = file.Alternatives
            .Where(IsAlternativeAvailable)
            .ToList();

        if (availableAlts.Count == 0)
            return false; // nothing real on disk / in ACC — hide

        var fileNode = new PickerNode
        {
            Kind = PickerNodeKind.File,
            Title = fileTitle,
            Icon = "📄",
            IsSelectable = false,
            ShowCheckBox = false,
            IsExpanded = true,
            TitleWeight = FontWeights.SemiBold
        };
        parent.Children.Add(fileNode);

        foreach (var alt in availableAlts)
        {
            var altName = alt.AlternativeName ?? string.Empty;
            var leaf = new PickerNode
            {
                Kind = PickerNodeKind.Alternative,
                Title = string.IsNullOrEmpty(altName) ? "(ברירת מחדל)" : altName,
                Icon = "🧩",
                IsSelectable = true,
                ShowCheckBox = true,
                FileName = file.FileName,
                Alternative = altName,
                IsChecked = preset.Contains((file.FileName, altName))
            };
            fileNode.Children.Add(leaf);
            _allSelectableLeaves.Add(leaf);
        }

        return true;
    }

    private static bool HasSelectableDescendant(PickerNode node)
    {
        if (node.IsSelectable) return true;
        foreach (var c in node.Children)
            if (HasSelectableDescendant(c)) return true;
        return false;
    }

    // ───── Selection wiring ──────────────────────────────────────────────

    private void OnItemChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerNode node) return;

        if (_mode == FilePickerSelectionMode.Single)
        {
            _suppressCheckSync = true;
            try
            {
                foreach (var leaf in _allSelectableLeaves)
                    if (!ReferenceEquals(leaf, node) && leaf.IsChecked)
                        leaf.IsChecked = false;
            }
            finally { _suppressCheckSync = false; }
        }

        UpdateOkEnabled();
    }

    private void OnItemUnchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync) return;
        UpdateOkEnabled();
    }

    private void OnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerNode node) return;
        if (!node.IsSelectable) return;

        if (_mode == FilePickerSelectionMode.Single)
        {
            _suppressCheckSync = true;
            try
            {
                foreach (var leaf in _allSelectableLeaves)
                    leaf.IsChecked = ReferenceEquals(leaf, node);
            }
            finally { _suppressCheckSync = false; }
            DialogResult = true;
            Close();
        }
        else
        {
            node.IsChecked = !node.IsChecked;
        }
    }

    private void UpdateOkEnabled()
    {
        var hasAny = _allSelectableLeaves.Any(n => n.IsChecked);
        OkButton.IsEnabled = _mode == FilePickerSelectionMode.Multiple || hasAny;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_mode == FilePickerSelectionMode.Single &&
            !_allSelectableLeaves.Any(n => n.IsChecked))
        {
            MessageBox.Show("יש לבחור פריט אחד.", Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        var q = FilterBox.Text?.Trim() ?? string.Empty;
        foreach (var r in _roots)
            ApplyFilter(r, q);
    }

    private static bool ApplyFilter(PickerNode node, string q)
    {
        var anyChildVisible = false;
        foreach (var c in node.Children)
            anyChildVisible |= ApplyFilter(c, q);

        var selfMatch = q.Length == 0 ||
            (node.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

        node.IsVisible = anyChildVisible || selfMatch;
        if (q.Length > 0 && anyChildVisible) node.IsExpanded = true;
        return node.IsVisible;
    }

    // ───── Tree node ─────────────────────────────────────────────────────

    internal enum PickerNodeKind { Folder, File, Alternative }

    internal sealed class PickerNode : INotifyPropertyChanged
    {
        public PickerNodeKind Kind { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;
        public bool IsSelectable { get; init; }
        public bool ShowCheckBox { get; init; }
        public FontWeight TitleWeight { get; init; } = FontWeights.Normal;

        public string? FileName { get; init; }
        public string? Alternative { get; init; }

        public ObservableCollection<PickerNode> Children { get; } = new();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible == value) return; _isVisible = value; OnPropertyChanged(); }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked == value) return; _isChecked = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
    }
}
