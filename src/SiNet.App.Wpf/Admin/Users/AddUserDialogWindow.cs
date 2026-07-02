using System.Windows;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>Native New System dialog for <see cref="AddUserView"/>.</summary>
public sealed class AddUserDialogWindow : Window
{
    private readonly AddUserViewModel _viewModel;

    public AddUserDialogWindow(AddUserViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Title = "הוספת משתמש — מערכת חדשה";
        Width = 480;
        Height = 520;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Content = new AddUserView { DataContext = _viewModel };
        _viewModel.RequestClose += OnRequestClose;
        Closed += (_, _) => _viewModel.RequestClose -= OnRequestClose;
        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void OnRequestClose(bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }
}
