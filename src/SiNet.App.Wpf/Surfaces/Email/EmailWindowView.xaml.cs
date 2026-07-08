using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Window code-behind for the first read-only New System slice of the legacy <c>EmailManagementView</c>.
/// <para>
/// The code-behind still contains <i>view-level chrome only</i> — header drag-to-move and close —
/// matching the borderless window shell used by the other new surfaces. It deliberately carries no
/// business logic: no DB, no Gmail SDK calls, no file-system access, no project linking, no task
/// creation, and no workflow mutation. All behavior is exposed through <see cref="EmailWindowViewModel"/>.
/// </para>
/// <para>
/// The parameterless constructor exists so the window can still be shown with fake design-time data;
/// the typed constructor is the path a DI host / work-surface launcher uses for the real slice.
/// </para>
/// </summary>
public partial class EmailWindowView : Window
{
    /// <summary>Design/standalone constructor: shows the window with fake in-memory data.</summary>
    public EmailWindowView()
        : this(new EmailWindowViewModel())
    {
    }

    /// <summary>Primary constructor: binds to the supplied New System view model.</summary>
    public EmailWindowView(EmailWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The bound view model for the read-only Gmail slice.</summary>
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

    private void EmailWindowView_OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.TryBlockCloseForBackgroundWork(this))
        {
            e.Cancel = true;
        }
    }
}
