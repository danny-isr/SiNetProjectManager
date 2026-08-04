using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// First visible UI at application startup (see <c>docs/APP_SHELL.md</c> §3). Default = New System.
/// </summary>
public partial class StartupModeSelectionWindow : Window
{
    private readonly StartupModeSelectionViewModel _viewModel = new();

    public StartupModeSelectionWindow()
    {
        // V2 host App.xaml does not merge App.Wpf theme dictionaries — ensure Si* resources exist.
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Shows the modal chooser. Returns the selected mode, or <see langword="null"/> if cancelled.
    /// </summary>
    public static StartupMode? TryPromptForMode()
    {
        var window = new StartupModeSelectionWindow();
        return window.ShowDialog() == true ? window._viewModel.SelectedMode : null;
    }

    private void Continue_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
