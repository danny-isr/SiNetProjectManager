using System.Windows;
using System.Windows.Input;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Window code-behind for the visual clone of the legacy <c>EmailManagementView</c>.
/// <para>
/// <b>Visual-clone slice only.</b> The code-behind contains <i>view-level chrome only</i> — header
/// drag-to-move and close — matching the borderless window shell used by the other new surfaces. It
/// deliberately carries no business logic: no DB, no email loading, no Gmail/Outlook, no file-system
/// access, no project linking, no task creation, and no workflow mutation. All behavior is exposed
/// through the thin <see cref="EmailWindowViewModel"/> whose commands are stubbed.
/// </para>
/// <para>
/// The parameterless constructor exists so the window can be shown with fake design-time data during
/// this slice; the typed constructor is the path a DI host / work-surface launcher will use later.
/// </para>
/// </summary>
public partial class EmailWindowView : Window
{
    /// <summary>Design/standalone constructor: shows the clone with fake in-memory data.</summary>
    public EmailWindowView()
        : this(new EmailWindowViewModel())
    {
    }

    /// <summary>Primary constructor: binds to the supplied thin view model.</summary>
    public EmailWindowView(EmailWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The bound view model (thin, stubbed; visual-clone only).</summary>
    public EmailWindowViewModel ViewModel { get; }

    /// <summary>
    /// Placeholder workflow-first entry point. A later slice will use this to open the window from a
    /// task; for now it only forwards the context to the view model, which records it without starting
    /// or mutating any workflow.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context) => ViewModel.ApplyContext(context);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
