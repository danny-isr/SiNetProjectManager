using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPFUserControl;

public partial class WorkflowClosedViewerView : UserControl
{
    public WorkflowClosedViewerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private WorkflowClosedViewerViewModel? ViewModel => DataContext as WorkflowClosedViewerViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is WorkflowClosedViewerViewModel existing)
        {
            await existing.LoadAsync();
            return;
        }

        var sp = App.ServiceProvider;
        if (sp is null)
        {
            return;
        }

        var vm = sp.GetRequiredService<WorkflowClosedViewerViewModel>();
        DataContext = vm;
        await vm.LoadAsync();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.SelectedNode = e.NewValue as WorkflowViewerNode;
    }
}

