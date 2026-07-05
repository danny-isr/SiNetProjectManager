using System.Windows;

namespace SiNet.App.Wpf.DevTools;

/// <summary>
/// New System dev-reset confirmation dialog (ported from legacy V2 dialog; no SiNetSQL dependency).
/// </summary>
public partial class ResetOptionsDialog : Window
{
    public bool UserApproved { get; private set; }
    public bool WipeSystemSettings { get; private set; }
    public bool ResetUserSettings { get; private set; }
    public bool IncludeDemoTasks { get; private set; }

    public ResetOptionsDialog(string databaseName, string windowsUser)
    {
        InitializeComponent();
        DbInfoText.Text = $"מסד נתונים: {databaseName}    משתמש: {windowsUser}";
    }

    private void RunReset_Click(object sender, RoutedEventArgs e)
    {
        if (OptInitiateResetCheck.IsChecked != true || OptConfirmIrreversibleCheck.IsChecked != true)
        {
            MessageBox.Show(
                "כדי לבצע איפוס יש לסמן את שתי תיבות האישור הראשונות.",
                "אישור חסר",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        WipeSystemSettings = OptWipeSystemSettingsCheck.IsChecked == true;
        ResetUserSettings = OptResetUserSettingsCheck.IsChecked == true;
        IncludeDemoTasks = OptIncludeDemoTasksCheck.IsChecked == true;
        UserApproved = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        UserApproved = false;
        DialogResult = false;
        Close();
    }
}
