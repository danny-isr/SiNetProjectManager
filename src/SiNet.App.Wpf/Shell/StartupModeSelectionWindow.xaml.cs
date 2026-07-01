using System.Windows;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// The first-visible startup mode chooser window (see <c>docs/APP_SHELL.md</c> §2/§3). It is a modal
/// dialog shown <b>before any legacy startup gate</b> (credential vault, DB connection, schema
/// validation, role selector, splash). It has <b>no timeout and no auto-close</b>: it stays open until
/// the user presses "המשך", and closing with X is reported as <b>not confirmed</b> so the host can exit
/// instead of silently defaulting to Legacy. The default selection is New System.
/// </summary>
public partial class StartupModeSelectionWindow : Window
{
    private readonly StartupModeSelectionViewModel _viewModel;

    /// <summary>Creates the chooser with a default (New System) view model.</summary>
    public StartupModeSelectionWindow() : this(new StartupModeSelectionViewModel())
    {
    }

    /// <summary>Creates the chooser bound to the supplied view model.</summary>
    public StartupModeSelectionWindow(StartupModeSelectionViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();

        DataContext = _viewModel;

        // Confirm (Continue) closes the dialog with a positive result on the UI thread.
        _viewModel.ConfirmRequested += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }

    /// <summary>The mode the user selected (defaults to New System until changed).</summary>
    public StartupMode SelectedMode => _viewModel.SelectedMode;

    /// <summary><see langword="true"/> only when the user pressed Continue (not when closed with X).</summary>
    public bool Confirmed => _viewModel.Confirmed;

    /// <summary>
    /// Shows the chooser modally and reports whether the user confirmed a mode. This is the single entry
    /// point the host uses at the very start of <c>OnStartup</c>.
    /// </summary>
    /// <param name="owner">Optional owner window; usually none at startup.</param>
    /// <param name="selectedMode">The chosen <see cref="StartupMode"/> (default New System) when confirmed.</param>
    /// <returns>
    /// <see langword="true"/> when the user pressed Continue; <see langword="false"/> when the dialog was
    /// cancelled/closed with X (host should exit rather than assume Legacy).
    /// </returns>
    public static bool TryPromptForMode(Window? owner, out StartupMode selectedMode)
    {
        var window = new StartupModeSelectionWindow();
        if (owner is not null)
        {
            window.Owner = owner;
        }

        var confirmed = window.ShowDialog() == true && window.Confirmed;
        selectedMode = window.SelectedMode;
        return confirmed;
    }
}
