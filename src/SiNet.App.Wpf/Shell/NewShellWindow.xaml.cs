using System.Windows;
using SiNet.App.Wpf.Shared.Projects;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// The clean New System shell window (see <c>docs/APP_SHELL.md</c>). It is intentionally minimal and
/// carries no business logic in code-behind: it wires the shared Project Selector into the header.
/// It does NOT open the legacy <c>MainWindow</c> and does NOT load the legacy menu.
/// </summary>
public partial class NewShellWindow : Window
{
    private readonly NewShellViewModel _viewModel;
    private readonly ProjectSelectorView? _projectSelector;

    /// <summary>
    /// Creates the shell.
    /// </summary>
    /// <param name="viewModel">The shell view model (migrated-only menu + header/status/window title).</param>
    /// <param name="projectSelector">
    /// The shared, reusable Project Selector view (already bound to its view model by the host) to host
    /// in the current-project bar. Optional — omitted when the Project Context is unavailable.
    /// </param>
    public NewShellWindow(NewShellViewModel viewModel, ProjectSelectorView? projectSelector = null)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        if (projectSelector is not null)
        {
            _projectSelector = projectSelector;
            ProjectSelectorHost.Content = projectSelector;
        }

        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();

        if (_projectSelector?.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
