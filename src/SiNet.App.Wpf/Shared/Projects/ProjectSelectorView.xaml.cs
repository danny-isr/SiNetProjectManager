using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Shared Project Selector control. Code-behind handles popup open/close and list selection only.
/// </summary>
public partial class ProjectSelectorView : UserControl
{
    public ProjectSelectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        LostKeyboardFocus += OnLostKeyboardFocus;
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
            viewModel.OpenResults();
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
