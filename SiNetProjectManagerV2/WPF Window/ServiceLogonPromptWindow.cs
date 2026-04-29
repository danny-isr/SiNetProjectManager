using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Minimal credential prompt for re-configuring the SiOfficeAccService Windows
/// service to log on as a specific Windows account (instead of LocalSystem).
/// Pre-filled with <c>DOMAIN\\CurrentUser</c> so the operator only types the
/// password in the typical case.
/// </summary>
internal sealed class ServiceLogonPromptWindow : Window
{
    private readonly TextBox _account;
    private readonly PasswordBox _password;

    public string Account => _account.Text.Trim();
    public string Password => _password.Password;

    public ServiceLogonPromptWindow(string defaultAccount)
    {
        Title = "הגדרת חשבון לשירות";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FlowDirection = FlowDirection.RightToLeft;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var info = new TextBlock
        {
            Text = "השירות יוגדר לרוץ תחת המשתמש שלך כדי שיוכל לקרוא את הסודות שכתבת.\n" +
                   "למשתמש חייבת להיות הרשאת 'Log on as a service'.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetColumnSpan(info, 2);
        Grid.SetRow(info, 0);
        grid.Children.Add(info);

        var lblAccount = new TextBlock { Text = "חשבון:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        Grid.SetRow(lblAccount, 1); Grid.SetColumn(lblAccount, 0);
        grid.Children.Add(lblAccount);

        _account = new TextBox { Text = defaultAccount, Margin = new Thickness(0, 4, 0, 4) };
        Grid.SetRow(_account, 1); Grid.SetColumn(_account, 1);
        grid.Children.Add(_account);

        var lblPassword = new TextBlock { Text = "סיסמה:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        Grid.SetRow(lblPassword, 2); Grid.SetColumn(lblPassword, 0);
        grid.Children.Add(lblPassword);

        _password = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        Grid.SetRow(_password, 2); Grid.SetColumn(_password, 1);
        grid.Children.Add(_password);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "אישור", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "בטל", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_account.Text) || string.IsNullOrWhiteSpace(_password.Password))
            {
                MessageBox.Show("יש למלא חשבון וסיסמה.", "שדה חסר", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        };
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        Grid.SetColumnSpan(btnPanel, 2);
        Grid.SetRow(btnPanel, 3);
        grid.Children.Add(btnPanel);

        Content = grid;
        _password.Focus();
    }
}
