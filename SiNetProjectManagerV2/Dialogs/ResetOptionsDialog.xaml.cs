using System.Windows;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Consolidated DEV-only confirmation dialog for <c>DevDataResetService</c>.
/// Replaces the three sequential <see cref="MessageBox"/> prompts that were
/// previously shown in <c>MainWindow.DevResetData_Click</c>:
/// <list type="number">
///   <item>Initial reset confirmation (was default No → now checkbox default checked).</item>
///   <item>Final irreversible-action confirmation (was default No → now checkbox default checked).</item>
///   <item>Wipe Bootstrap / SystemSettings (was default No → checkbox default unchecked).</item>
/// </list>
/// Adds a fourth option: <c>ResetUserSettings</c> (default unchecked) so that
/// <c>UserGroups</c>, <c>UserGroupMemberships</c> and default-assignee data are preserved
/// across resets, keeping the Workflow preflight from blocking new workflows.
/// </summary>
public partial class ResetOptionsDialog : Window
{
    /// <summary>True if the user pressed "הפעל איפוס" with both required confirmations checked.</summary>
    public bool UserApproved { get; private set; }

    /// <summary>Whether the user wants the Bootstrap / SystemSettings table wiped.</summary>
    public bool WipeSystemSettings { get; private set; }

    /// <summary>
    /// Whether the user wants user-related settings (UserGroups / Memberships / default
    /// assignees) wiped. Default is <c>false</c> to preserve workflow assignment data.
    /// </summary>
    public bool ResetUserSettings { get; private set; }

    public ResetOptionsDialog(string databaseName, string windowsUser)
    {
        InitializeComponent();

        DbInfoText.Text = $"מסד נתונים: {databaseName}    משתמש: {windowsUser}";
    }

    private void RunReset_Click(object sender, RoutedEventArgs e)
    {
        // Both confirmation checkboxes must be checked to proceed — they replace the
        // two sequential Yes/No confirmations from the legacy flow.
        if (OptInitiateResetCheck.IsChecked != true ||
            OptConfirmIrreversibleCheck.IsChecked != true)
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
