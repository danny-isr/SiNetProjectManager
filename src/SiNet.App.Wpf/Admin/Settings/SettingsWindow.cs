using System.Windows;

namespace SiNet.App.Wpf.Admin.Settings;

/// <summary>Native New System settings window (personal or system-admin scope).</summary>
public sealed class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Title = viewModel.Scope == SettingsSurfaceScope.Personal
            ? "הגדרות אישיות — מערכת חדשה"
            : "הגדרות מערכת — מערכת חדשה";
        Width = 820;
        Height = 720;
        MinWidth = 640;
        MinHeight = 520;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Content = new SettingsView { DataContext = _viewModel };
        _viewModel.RequestClose += OnRequestClose;
        Closed += (_, _) => _viewModel.RequestClose -= OnRequestClose;
        Closing += (_, _) => _viewModel.RollbackAppearanceIfNeeded();
        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    private void OnRequestClose(bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }
}
