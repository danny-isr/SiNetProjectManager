using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiNet.App.Wpf.Shared.Pickers;

/// <summary>
/// Shared hierarchical file-tree picker (same UX as V2 <c>FileTreePickerWindow</c>).
/// This copy exposes the pre-built-tree API used by email attachment tagging.
/// </summary>
public partial class FileTreePickerWindow : Window
{
    private readonly FilePickerSelectionMode _mode;
    private readonly List<PickerNode> _allSelectableLeaves = new();
    private readonly ObservableCollection<PickerNode> _roots = new();
    private bool _suppressCheckSync;
    private Action<int?>? _onTypeFilterChanged;
    private bool _suppressTypeFilterEvent;

    public sealed record TypeFilterOption(int? TypeProjId, string Title);

    public FileTreePickerWindow(
        IEnumerable<PickerNode> roots,
        FilePickerSelectionMode mode,
        string headerText)
    {
        InitializeComponent();
        _mode = mode;
        HeaderText.Text = headerText ?? string.Empty;

        int displayedFolders = 0, displayedFiles = 0;
        foreach (var r in roots)
        {
            _roots.Add(r);
            CollectStats(r, ref displayedFolders, ref displayedFiles);
            CollectSelectableLeaves(r);
        }

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

    public IReadOnlyList<object?> SelectedTags =>
        _allSelectableLeaves
            .Where(n => n.IsChecked)
            .Select(n => n.Tag)
            .ToList();

    public void ConfigureTypeFilter(
        IReadOnlyList<TypeFilterOption> options,
        Action<int?> onChanged,
        int? initialTypeProjId = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _onTypeFilterChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        TypeFilterPanel.Visibility = Visibility.Visible;
        _suppressTypeFilterEvent = true;
        try
        {
            TypeFilterBox.ItemsSource = options;
            TypeFilterBox.SelectedItem = options.FirstOrDefault(o => o.TypeProjId == initialTypeProjId)
                                         ?? options.FirstOrDefault();
        }
        finally
        {
            _suppressTypeFilterEvent = false;
        }
    }

    public void ReplaceRoots(IEnumerable<PickerNode> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        _roots.Clear();
        _allSelectableLeaves.Clear();

        int displayedFolders = 0, displayedFiles = 0;
        foreach (var r in roots)
        {
            _roots.Add(r);
            CollectStats(r, ref displayedFolders, ref displayedFiles);
            CollectSelectableLeaves(r);
        }

        var empty = _allSelectableLeaves.Count == 0;
        Tree.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            OkButton.IsEnabled = false;
        }
        else
        {
            UpdateOkEnabled();
        }

        var q = FilterBox.Text?.Trim() ?? string.Empty;
        if (q.Length > 0)
        {
            foreach (var r in _roots)
            {
                ApplyFilter(r, q);
            }
        }
    }

    private void OnTypeFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTypeFilterEvent || _onTypeFilterChanged is null)
        {
            return;
        }

        if (TypeFilterBox.SelectedItem is TypeFilterOption option)
        {
            _onTypeFilterChanged(option.TypeProjId);
        }
    }

    private void CollectSelectableLeaves(PickerNode node)
    {
        if (node.IsSelectable)
        {
            _allSelectableLeaves.Add(node);
        }

        foreach (var c in node.Children)
        {
            CollectSelectableLeaves(c);
        }
    }

    private static void CollectStats(PickerNode node, ref int folders, ref int files)
    {
        if (node.Kind == PickerNodeKind.Folder)
        {
            folders++;
        }
        else if (node.Kind == PickerNodeKind.File)
        {
            files++;
        }

        foreach (var c in node.Children)
        {
            CollectStats(c, ref folders, ref files);
        }
    }

    private void OnItemChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync)
        {
            return;
        }

        if (sender is not FrameworkElement fe || fe.DataContext is not PickerNode node)
        {
            return;
        }

        if (_mode == FilePickerSelectionMode.Single)
        {
            _suppressCheckSync = true;
            try
            {
                foreach (var leaf in _allSelectableLeaves)
                {
                    if (!ReferenceEquals(leaf, node) && leaf.IsChecked)
                    {
                        leaf.IsChecked = false;
                    }
                }
            }
            finally
            {
                _suppressCheckSync = false;
            }
        }

        UpdateOkEnabled();
    }

    private void OnItemUnchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync)
        {
            return;
        }

        UpdateOkEnabled();
    }

    private void OnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is not FrameworkElement fe || fe.DataContext is not PickerNode node)
        {
            return;
        }

        if (!node.IsSelectable)
        {
            return;
        }

        if (_mode == FilePickerSelectionMode.Single)
        {
            _suppressCheckSync = true;
            try
            {
                foreach (var leaf in _allSelectableLeaves)
                {
                    leaf.IsChecked = ReferenceEquals(leaf, node);
                }
            }
            finally
            {
                _suppressCheckSync = false;
            }

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
        {
            ApplyFilter(r, q);
        }
    }

    private static bool ApplyFilter(PickerNode node, string q)
    {
        var anyChildVisible = false;
        foreach (var c in node.Children)
        {
            anyChildVisible |= ApplyFilter(c, q);
        }

        var selfMatch = q.Length == 0 ||
            (node.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

        node.IsVisible = anyChildVisible || selfMatch;
        if (q.Length > 0 && anyChildVisible)
        {
            node.IsExpanded = true;
        }

        return node.IsVisible;
    }

    public enum PickerNodeKind
    {
        Folder,
        File,
        Alternative,
    }

    public sealed class PickerNode : INotifyPropertyChanged
    {
        public PickerNodeKind Kind { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;
        public bool IsSelectable { get; init; }
        public bool ShowCheckBox { get; init; }
        public FontWeight TitleWeight { get; init; } = FontWeights.Normal;
        public object? Tag { get; init; }
        public ObservableCollection<PickerNode> Children { get; } = new();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }

                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value)
                {
                    return;
                }

                _isVisible = value;
                OnPropertyChanged();
            }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }

                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
    }
}

public enum FilePickerSelectionMode
{
    Single,
    Multiple,
}
