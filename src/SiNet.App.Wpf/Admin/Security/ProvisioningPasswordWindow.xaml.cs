using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.Security;

public partial class ProvisioningPasswordWindow : Window
{
    public ProvisioningPasswordWindow(bool requireConfirmation, string title)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Title = title;
        ConfirmPanel.Visibility = requireConfirmation ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string EnteredPassword => PasswordBox.Password;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            MessageBox.Show(this, "יש להזין סיסמה.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PasswordBox.Password.Length < 6)
        {
            MessageBox.Show(this, "הסיסמה חייבת להכיל לפחות 6 תווים.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ConfirmPanel.Visibility == Visibility.Visible
            && PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            MessageBox.Show(this, "הסיסמאות אינן תואמות.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            ConfirmPasswordBox.Clear();
            ConfirmPasswordBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
