using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Window code-behind for the visual clone of the legacy <c>FloatingInspectionView</c>.
/// <para>
/// <b>Visual-clone slice only.</b> The code-behind contains <i>view-level chrome only</i> — header
/// drag-to-move and close — matching the borderless floating window of the original. It deliberately
/// carries no business logic: no DB, no report generation, no Gmail/planner, no ACC/file actions, and
/// no workflow mutation. All behavior is exposed through the thin
/// <see cref="InspectionWindowViewModel"/> whose commands are stubbed.
/// </para>
/// <para>
/// The parameterless constructor exists so the window can be shown with fake design-time data during
/// this slice; the typed constructor is the path a DI host / work-surface launcher will use later.
/// </para>
/// </summary>
public partial class InspectionWindowView : Window
{
    /// <summary>Design/standalone constructor: shows the clone with fake in-memory data.</summary>
    public InspectionWindowView()
        : this(new InspectionWindowViewModel())
    {
    }

    /// <summary>Primary constructor: binds to the supplied thin view model.</summary>
    public InspectionWindowView(InspectionWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The bound view model (thin, stubbed; visual-clone only).</summary>
    public InspectionWindowViewModel ViewModel { get; }

    /// <summary>
    /// Task-mode entry. Prefer <see cref="ApplyContextAsync"/> when the caller can await load completion.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context) => ViewModel.ApplyContext(context);

    /// <summary>Task-mode entry that awaits exact report load (no first/last fallback).</summary>
    public Task<bool> ApplyContextAsync(WorkSurfaceContext? context, CancellationToken cancellationToken = default)
        => ViewModel.ApplyContextAsync(context, cancellationToken);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
