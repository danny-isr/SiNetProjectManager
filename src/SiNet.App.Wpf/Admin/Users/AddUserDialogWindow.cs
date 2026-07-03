using System.Windows;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>Native New System dialog for <see cref="AddUserView"/>.</summary>
public sealed class AddUserDialogWindow : Window
{
    private readonly AddUserViewModel _viewModel;

    public AddUserDialogWindow(AddUserViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Title = "הוספת משתמש — מערכת חדשה";
        Width = 520;
        Height = 640;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new AddUserView { DataContext = _viewModel };
        _viewModel.RequestClose += OnRequestClose;
        Closed += (_, _) => _viewModel.RequestClose -= OnRequestClose;
        Loaded += async (_, _) =>
        {
            try
            {
                await _viewModel.InitializeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppErrorReporter.Report(ex, "AddUserDialogWindow.OnLoaded");
            }
        };
    }

    private void OnRequestClose(bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }
}
