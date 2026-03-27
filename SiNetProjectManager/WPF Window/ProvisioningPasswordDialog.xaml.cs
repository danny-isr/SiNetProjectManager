using System.Windows;
using System.Windows.Input;

namespace SiNetProjectManager.WPF_Window;

/// <summary>
/// Simple password prompt dialog for provisioning package export/import.
/// Set <see cref="RequireConfirmation"/> = true for export (shows confirm field).
/// </summary>
public partial class ProvisioningPasswordDialog : Window
{
    /// <summary>
    /// When true, shows a confirmation password field (for export).
    /// </summary>
    public bool RequireConfirmation
    {
        get => PanelConfirm.Visibility == Visibility.Visible;
        set => PanelConfirm.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The password entered by the user.</summary>
    public string EnteredPassword => TxtPassword.Password;

    public ProvisioningPasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TxtPassword.Focus();
    }

    /// <summary>
    /// Configures the dialog for export mode (with confirmation).
    /// </summary>
    public static ProvisioningPasswordDialog ForExport()
    {
        return new ProvisioningPasswordDialog
        {
            RequireConfirmation = true,
            Title = "ייצוא חבילת הגדרות מוצפנת"
        };
    }

    /// <summary>
    /// Configures the dialog for import mode (no confirmation).
    /// </summary>
    public static ProvisioningPasswordDialog ForImport()
    {
        return new ProvisioningPasswordDialog
        {
            RequireConfirmation = false,
            Title = "ייבוא חבילת הגדרות"
        };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryAccept();
    }

    private void TryAccept()
    {
        if (string.IsNullOrWhiteSpace(TxtPassword.Password))
        {
            MessageBox.Show("יש להזין סיסמה.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (TxtPassword.Password.Length < 6)
        {
            MessageBox.Show("הסיסמה חייבת להכיל לפחות 6 תווים.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RequireConfirmation && TxtPassword.Password != TxtConfirm.Password)
        {
            MessageBox.Show("הסיסמאות אינן תואמות.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtConfirm.Clear();
            TxtConfirm.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }
}
