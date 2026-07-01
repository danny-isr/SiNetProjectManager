using System.Windows;
using System.Windows.Controls;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Shared, reusable Project Selector control (see <c>docs/PROJECTS.md</c> §5/§13).
/// <para>
/// It is intentionally <b>not Email-specific</b>: it can be hosted by any window that needs project
/// search/selection. It binds to a <see cref="ProjectSelectorViewModel"/> (which exposes
/// <see cref="ProjectSummaryDto"/> rows only — never EF entities) and, on selection, publishes the
/// chosen project to the shared <c>ICurrentProjectContext</c>. The code-behind carries no business
/// logic; it only triggers the initial load when hosted.
/// </para>
/// </summary>
public partial class ProjectSelectorView : UserControl
{
    /// <summary>
    /// Creates the control. When a <see cref="ProjectSelectorViewModel"/> is set as the
    /// <see cref="FrameworkElement.DataContext"/>, the control loads its project list on first render.
    /// </summary>
    public ProjectSelectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectSelectorViewModel viewModel && viewModel.Projects.Count == 0)
        {
            await viewModel.LoadAsync().ConfigureAwait(true);
        }
    }
}
