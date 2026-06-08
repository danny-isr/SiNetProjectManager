using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Floating window for managing project decisions with inheritance and versioning.
/// </summary>
public partial class ProjectDecisionsWindow : Window
{
    private ProjectDecisionsViewModel? _viewModel;

    public ProjectDecisionsWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<ProjectDecisionsViewModel>();
        DataContext = _viewModel;
    }

    private void EditDecision_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.BeginEdit();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _viewModel?.Dispose();
        base.OnClosed(e);
    }
}
