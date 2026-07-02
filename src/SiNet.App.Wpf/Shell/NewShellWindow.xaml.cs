using System.Windows;
using System.Windows.Threading;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// The clean New System shell window (see <c>docs/APP_SHELL.md</c>). It is intentionally minimal and
/// carries no business logic in code-behind: it wires the shared Project Selector into the header and
/// reflects the shared <see cref="ICurrentProjectContext"/> into the shell's current-project display.
/// It does NOT open the legacy <c>MainWindow</c> and does NOT load the legacy menu.
/// </summary>
public partial class NewShellWindow : Window
{
    private readonly NewShellViewModel _viewModel;
    private readonly ICurrentProjectContext? _currentProjectContext;
    private readonly ProjectSelectorView? _projectSelector;

    /// <summary>
    /// Creates the shell.
    /// </summary>
    /// <param name="viewModel">The shell view model (migrated-only menu + header/status).</param>
    /// <param name="projectSelector">
    /// The shared, reusable Project Selector view (already bound to its view model by the host) to host
    /// in the current-project bar. Optional — omitted when the Project Context is unavailable.
    /// </param>
    /// <param name="currentProjectContext">
    /// The shared Current Project context used to update the header's current-project text. Optional.
    /// </param>
    public NewShellWindow(
        NewShellViewModel viewModel,
        ProjectSelectorView? projectSelector = null,
        ICurrentProjectContext? currentProjectContext = null)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _currentProjectContext = currentProjectContext;

        DataContext = _viewModel;

        if (projectSelector is not null)
        {
            _projectSelector = projectSelector;
            ProjectSelectorHost.Content = projectSelector;
        }

        if (_currentProjectContext is not null)
        {
            // Seed the initial display and observe changes. The event may fire off the UI thread, so
            // marshal to the dispatcher before touching the view model (per ICurrentProjectContext).
            UpdateProjectDisplay(_currentProjectContext.CurrentProject);
            _currentProjectContext.CurrentProjectChanged += OnCurrentProjectChanged;
            Closed += OnClosed;
        }
        else
        {
            Closed += OnClosed;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_currentProjectContext is not null)
        {
            _currentProjectContext.CurrentProjectChanged -= OnCurrentProjectChanged;
        }

        if (_projectSelector?.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateProjectDisplay(e.Project);
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateProjectDisplay(e.Project)), DispatcherPriority.Background);
        }
    }

    private void UpdateProjectDisplay(ProjectSummaryDto? project)
    {
        var display = project is null
            ? null
            : $"{project.ProjectName} — {project.ProjectNumber}";

        _viewModel.SetCurrentProjectDisplay(display);
    }
}
