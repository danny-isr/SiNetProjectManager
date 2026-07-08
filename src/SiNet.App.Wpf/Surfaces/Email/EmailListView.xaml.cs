using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiNet.App.Wpf.Surfaces.Email;

public partial class EmailListView : UserControl
{
    public EmailListView()
    {
        InitializeComponent();
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnGroupEmailListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedValue is not string selectedId)
        {
            return;
        }

        if (DataContext is EmailListViewModel viewModel
            && !string.Equals(viewModel.SelectedEmailId, selectedId, StringComparison.Ordinal))
        {
            viewModel.SelectedEmailId = selectedId;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollEmailListByWheelDelta(e.Delta);
        e.Handled = true;
    }

    private void ScrollEmailListByWheelDelta(int delta)
    {
        if (EmailListScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var offset = EmailListScrollViewer.VerticalOffset - delta;
        EmailListScrollViewer.ScrollToVerticalOffset(
            Math.Clamp(offset, 0, EmailListScrollViewer.ScrollableHeight));
    }
}
