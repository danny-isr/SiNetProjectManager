using System.ComponentModel;
using System.Windows;
using SiNet.App.Wpf.Surfaces.Email;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// The clean New System shell window (see <c>docs/APP_SHELL.md</c>). Minimal chrome: top menu +
/// content host. Project selection lives inside surfaces that need it (e.g. Email), not in the shell bar.
/// </summary>
public partial class NewShellWindow : Window
{
    private readonly NewShellViewModel _viewModel;
    private readonly IEmailSurfaceHost? _emailSurfaceHost;

    public NewShellWindow(
        NewShellViewModel viewModel,
        IEmailSurfaceHost? emailSurfaceHost = null)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _emailSurfaceHost = emailSurfaceHost;
        DataContext = _viewModel;

        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_emailSurfaceHost?.TryBlockShellClose(this) == true)
        {
            e.Cancel = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }
}
