using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Shared Project Selector control. Code-behind handles popup focus/list selection only — no business logic.
/// </summary>
public partial class ProjectSelectorView : UserControl
{
    private bool _isSelectingFromList;

    public ProjectSelectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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
            viewModel.IsResultsOpen = true;
        }
    }

    private void SearchBox_OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isSelectingFromList)
        {
            return;
        }

        if (e.NewFocus is DependencyObject newFocus
            && (IsWithinResultsPopup(newFocus) || ResultsPopup.IsMouseOver))
        {
            return;
        }

        if (DataContext is ProjectSelectorViewModel viewModel)
        {
            // Defer closing so a mouse click on the popup can complete before focus leaves the search box.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isSelectingFromList && !ResultsPopup.IsMouseOver && !ResultsList.IsMouseOver)
                {
                    viewModel.IsResultsOpen = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
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
                viewModel.IsResultsOpen = false;
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

    private bool IsWithinResultsPopup(DependencyObject element)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ResultsList) || ReferenceEquals(element, ResultsPopup.Child))
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void ResultsList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSelectingFromList = true;
    }

    private void ResultsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ProjectSelectorViewModel viewModel)
        {
            return;
        }

        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ProjectSummaryDto project)
        {
            _isSelectingFromList = false;
            return;
        }

        viewModel.SelectProjectCommand.Execute(project);
        ResultsList.SelectedItem = null;
        _isSelectingFromList = false;
        SearchBox.Focus();
    }
}
