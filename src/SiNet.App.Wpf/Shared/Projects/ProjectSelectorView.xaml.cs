using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.Application.Projects;
using SiNet.App.Wpf.Infrastructure;

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

    public static readonly DependencyProperty PopupWidthProperty =
        DependencyProperty.Register(
            nameof(PopupWidth),
            typeof(double),
            typeof(ProjectSelectorView),
            new PropertyMetadata(360d));

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

    private Point? _controlResizeMouseScreen;
    private double _controlResizeStartWidth;
    private Point? _popupResizeMouseScreen;
    private double _popupResizeStartWidth;

    public ProjectSelectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        LostKeyboardFocus += OnLostKeyboardFocus;
        BindWidthPropertiesToViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        BindWidthPropertiesToViewModel();

    /// <summary>
    /// All hosts share ControlWidth/PopupWidth from the VM (persisted in settings.json).
    /// </summary>
    private void BindWidthPropertiesToViewModel()
    {
        if (DataContext is not ProjectSelectorViewModel)
        {
            return;
        }

        SetBinding(SearchBoxWidthProperty, new Binding(nameof(ProjectSelectorViewModel.ControlWidth))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        SetBinding(PopupWidthProperty, new Binding(nameof(ProjectSelectorViewModel.PopupWidth))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
    }

    /// <summary>Width of the search TextBox + ▼ toggle group.</summary>
    public double SearchBoxWidth
    {
        get => (double)GetValue(SearchBoxWidthProperty);
        set => SetValue(SearchBoxWidthProperty, value);
    }

    /// <summary>Width of the results popup (independent of <see cref="SearchBoxWidth"/>).</summary>
    public double PopupWidth
    {
        get => (double)GetValue(PopupWidthProperty);
        set => SetValue(PopupWidthProperty, value);
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
        if (DataContext is not ProjectSelectorViewModel viewModel || viewModel.Projects.Count != 0)
        {
            return;
        }

        try
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppErrorReporter.Report(ex, "ProjectSelectorView.OnLoaded");
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

    private void ControlWidthThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _controlResizeMouseScreen = GetMouseScreenPosition((IInputElement)sender);
        _controlResizeStartWidth = SearchBoxWidth;
    }

    private void ControlWidthThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        // Do not use e.HorizontalChange under RTL: the grip sits on the edge that moves when
        // Width changes, so layout feedback makes deltas jump to Clamp min/max.
        if (_controlResizeMouseScreen is null || sender is not IInputElement thumb)
        {
            return;
        }

        var deltaX = GetMouseScreenPosition(thumb).X - _controlResizeMouseScreen.Value.X;
        ApplyControlWidth(ComputeWidthFromMouseDelta(_controlResizeStartWidth, deltaX, FlowDirection));
    }

    private void ControlWidthThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _controlResizeMouseScreen = null;
        FlushSelectorWidths();
    }

    private void PopupWidthThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _popupResizeMouseScreen = GetMouseScreenPosition((IInputElement)sender);
        _popupResizeStartWidth = PopupWidth;
    }

    private void PopupWidthThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_popupResizeMouseScreen is null || sender is not IInputElement thumb)
        {
            return;
        }

        var deltaX = GetMouseScreenPosition(thumb).X - _popupResizeMouseScreen.Value.X;
        ApplyPopupWidth(ComputeWidthFromMouseDelta(_popupResizeStartWidth, deltaX, FlowDirection));
    }

    private void PopupWidthThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _popupResizeMouseScreen = null;
        FlushSelectorWidths();
    }

    private void ApplyControlWidth(double width)
    {
        SearchBoxWidth = width;
        // Set VM directly — do not rely only on TwoWay DP binding for persistence.
        if (DataContext is ProjectSelectorViewModel vm)
        {
            vm.ControlWidth = width;
        }
    }

    private void ApplyPopupWidth(double width)
    {
        PopupWidth = width;
        if (DataContext is ProjectSelectorViewModel vm)
        {
            vm.PopupWidth = width;
        }
    }

    private void FlushSelectorWidths()
    {
        if (DataContext is ProjectSelectorViewModel vm)
        {
            vm.FlushPersistWidths();
        }
    }

    /// <summary>
    /// Grip is on the visual left in RTL (mirrored last column) and on the right in LTR.
    /// Screen X always increases to the right — invert delta for RTL so drag follows the mouse.
    /// </summary>
    internal static double ComputeWidthFromMouseDelta(
        double startWidth,
        double mouseDeltaXScreen,
        FlowDirection flowDirection,
        double min = 160,
        double max = 900)
    {
        var width = flowDirection == FlowDirection.RightToLeft
            ? startWidth - mouseDeltaXScreen
            : startWidth + mouseDeltaXScreen;
        return Math.Clamp(width, min, max);
    }

    private static Point GetMouseScreenPosition(IInputElement relativeTo)
    {
        var visual = (Visual)relativeTo;
        return visual.PointToScreen(Mouse.GetPosition(relativeTo));
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
