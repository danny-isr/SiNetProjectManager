using System.Windows;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Window for the native ProjectWork task-execution surface. Chrome only; task/completion logic
/// lives in <see cref="ProjectWorkWindowViewModel"/>.
/// </summary>
public partial class ProjectWorkWindowView : Window
{
    /// <summary>Design/standalone constructor.</summary>
    public ProjectWorkWindowView()
        : this(new ProjectWorkWindowViewModel())
    {
    }

    /// <summary>Primary constructor: binds to the supplied view model.</summary>
    public ProjectWorkWindowView(ProjectWorkWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The bound view model.</summary>
    public ProjectWorkWindowViewModel ViewModel { get; }

    /// <summary>Task-mode entry. Prefer <see cref="ApplyContextAsync"/> to await load completion.</summary>
    public void ApplyContext(WorkSurfaceContext? context) => ViewModel.ApplyContext(context);

    /// <summary>Task-mode entry that awaits context load (validates key + project; no fallback).</summary>
    public Task<bool> ApplyContextAsync(WorkSurfaceContext? context, CancellationToken cancellationToken = default)
        => ViewModel.ApplyContextAsync(context, cancellationToken);
}
