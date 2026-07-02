using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Shared, embeddable Project Selector (see <c>docs/PROJECTS.md</c> §5). Host windows configure layout
/// via dependency properties; selection publishes through <see cref="ProjectSelectorViewModel"/> →
/// <see cref="ICurrentProjectContext"/>. No Email/Shell/Task/Workflow logic lives here.
/// </summary>
public partial class ProjectSelectorView : UserControl
{
    public static readonly DependencyProperty SearchBoxWidthProperty =
        DependencyProperty.Register(
            nameof(SearchBoxWidth),
            typeof(double),
            typeof(ProjectSelectorView),
            new PropertyMetadata(340d));

    public static readonly DependencyProperty CompactModeProperty =
        DependencyProperty.Register(
            nameof(CompactMode),
            typeof(bool),
            typeof(ProjectSelectorView),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowFiltersProperty =
        DependencyProperty.Register(
            nameof(ShowFilters),
            typeof(bool),
            typeof(ProjectSelectorView),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowUserFilterProperty =
        DependencyProperty.Register(
            nameof(ShowUserFilter),
            typeof(bool),
            typeof(ProjectSelectorView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowIncludeClosedProperty =
        DependencyProperty.Register(
            nameof(ShowIncludeClosed),
            typeof(bool),
            typeof(ProjectSelectorView),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowExpandedResultsToggleProperty =
        DependencyProperty.Register(
            nameof(ShowExpandedResultsToggle),
            typeof(bool),
            typeof(ProjectSelectorView),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowRefreshButtonProperty =
        DependencyProperty.Register(
            nameof(ShowRefreshButton),
            typeof(bool),
            typeof(ProjectSelectorView),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowStatusMessageProperty =
        DependencyProperty.Register(
            nameof(ShowStatusMessage),
            typeof(bool),
            typeof(ProjectSelectorView),
            new PropertyMetadata(true));

    public ProjectSelectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        LostKeyboardFocus += OnLostKeyboardFocus;
    }

    /// <summary>Width of the search TextBox + ▼ toggle group.</summary>
    public double SearchBoxWidth
    {
        get => (double)GetValue(SearchBoxWidthProperty);
        set => SetValue(SearchBoxWidthProperty, value);
    }

    /// <summary>When <see langword="true"/>, uses smaller controls and margins for toolbar embedding.</summary>
    public bool CompactMode
    {
        get => (bool)GetValue(CompactModeProperty);
        set => SetValue(CompactModeProperty, value);
    }

    /// <summary>Show Job Type and Status filter combos.</summary>
    public bool ShowFilters
    {
        get => (bool)GetValue(ShowFiltersProperty);
        set => SetValue(ShowFiltersProperty, value);
    }

    /// <summary>Show user filter (deferred — default hidden).</summary>
    public bool ShowUserFilter
    {
        get => (bool)GetValue(ShowUserFilterProperty);
        set => SetValue(ShowUserFilterProperty, value);
    }

    /// <summary>Show the include-closed-projects checkbox.</summary>
    public bool ShowIncludeClosed
    {
        get => (bool)GetValue(ShowIncludeClosedProperty);
        set => SetValue(ShowIncludeClosedProperty, value);
    }

    /// <summary>Show the show-full-list checkbox.</summary>
    public bool ShowExpandedResultsToggle
    {
        get => (bool)GetValue(ShowExpandedResultsToggleProperty);
        set => SetValue(ShowExpandedResultsToggleProperty, value);
    }

    /// <summary>Show the reload button.</summary>
    public bool ShowRefreshButton
    {
        get => (bool)GetValue(ShowRefreshButtonProperty);
        set => SetValue(ShowRefreshButtonProperty, value);
    }

    /// <summary>Show the inline status hint.</summary>
    public bool ShowStatusMessage
    {
        get => (bool)GetValue(ShowStatusMessageProperty);
        set => SetValue(ShowStatusMessageProperty, value);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectSelectorViewModel viewModel && viewModel.Projects.Count == 0)
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
    }

    private void SearchBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectSelectorViewModel viewModel)
        {
            viewModel.HandleSearchBoxGotFocus();
        }
    }

    private void SearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ProjectSelectorViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.CloseResults();
                e.Handled = true;
                break;
            case Key.Down when viewModel.Projects.Count > 0:
                viewModel.IsResultsOpen = true;
                if (ResultsList.SelectedIndex < 0)
                {
                    ResultsList.SelectedIndex = 0;
                }

                ResultsList.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not ProjectSelectorViewModel viewModel || !viewModel.IsResultsOpen)
        {
            return;
        }

        if (e.NewFocus is DependencyObject newFocus && IsInsideSelectorOrPopup(newFocus))
        {
            return;
        }

        viewModel.CloseResults();
    }

    private void Selector_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ProjectSelectorViewModel viewModel || !viewModel.IsResultsOpen)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInsideSelectorOrPopup(source))
        {
            return;
        }

        viewModel.CloseResults();
    }

    private bool IsInsideSelectorOrPopup(DependencyObject element)
    {
        if (IsDescendantOf(element, this))
        {
            return true;
        }

        return ResultsPopup.Child is DependencyObject popupRoot && IsDescendantOf(element, popupRoot);
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor))
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void ResultsList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ProjectSelectorViewModel viewModel)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not ProjectSummaryDto project)
        {
            return;
        }

        viewModel.SelectProjectCommand.Execute(project);
        ResultsList.SelectedItem = null;
        SearchBox.Focus();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
