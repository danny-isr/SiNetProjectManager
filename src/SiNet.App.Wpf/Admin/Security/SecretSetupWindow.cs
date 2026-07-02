using System.Windows;

namespace SiNet.App.Wpf.Admin.Security;

/// <summary>Native New System window for vault secret setup (replaces legacy SecretSetupWindow in NewShell).</summary>
public sealed class SecretSetupWindow : Window
{
    private readonly SecretSetupViewModel _viewModel;

    public SecretSetupWindow(SecretSetupViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Title = "מפתחות וסודות — מערכת חדשה";
        Width = 760;
        Height = 680;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Content = new SecretSetupView { DataContext = _viewModel };
        _viewModel.RequestClose += OnRequestClose;
        Closed += (_, _) => _viewModel.RequestClose -= OnRequestClose;
        Loaded += async (_, _) => await _viewModel.LoadAsync().ConfigureAwait(true);
    }

    private void OnRequestClose(bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }
}
