using System.Windows;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.DevTools;

/// <summary>
/// DEBUG-only UI to temporarily change the current user's role/active flags in the DB.
/// </summary>
public partial class DebugAuthorizationRoleSelectorWindow : Window
{
    private readonly IDebugAuthorizationRoleOverrideService _overrideService;

    public DebugAuthorizationRoleSelectorWindow(IDebugAuthorizationRoleOverrideService overrideService)
    {
        InitializeComponent();
        _overrideService = overrideService ?? throw new ArgumentNullException(nameof(overrideService));
        Loaded += OnLoaded;
        WireWarnings();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        var current = await _overrideService.GetCurrentUserAsync().ConfigureAwait(true);
        LoginNameText.Text = $"LoginName: {current.WindowsLogin}";

        if (!current.UserFound)
        {
            DisplayNameText.Text = "DisplayName: (User not found in DB)";
            CurrentRoleText.Text = "Current Role: N/A";
            IsActiveText.Text = "Is Active: N/A";
            RbNoChange.IsChecked = true;
            DisableOptions();
            return;
        }

        DisplayNameText.Text = $"DisplayName: {current.DisplayName}";
        CurrentRoleText.Text = $"Current Role: {current.Role}";
        IsActiveText.Text = $"Is Active: {current.IsActive}";
    }

    private void DisableOptions()
    {
        RbAdmin.IsEnabled = false;
        RbManagement.IsEnabled = false;
        RbEmployee.IsEnabled = false;
        RbUnauthorized.IsEnabled = false;
        RbInactive.IsEnabled = false;
    }

    private void WireWarnings()
    {
        RbUnauthorized.Checked += (_, _) => WarningTextUnauthorized.Visibility = Visibility.Visible;
        RbUnauthorized.Unchecked += (_, _) => WarningTextUnauthorized.Visibility = Visibility.Collapsed;
        RbInactive.Checked += (_, _) => WarningTextInactive.Visibility = Visibility.Visible;
        RbInactive.Unchecked += (_, _) => WarningTextInactive.Visibility = Visibility.Collapsed;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var restored = await _overrideService.RestoreOriginalAsync().ConfigureAwait(true);
            if (!restored)
            {
                MessageBox.Show(
                    "לא נמצא גיבוי מקורי, או שהגיבוי לא תואם למשתמש הנוכחי.",
                    "Restore",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(
                "התפקיד והסטטוס המקוריים שוחזרו בהצלחה.",
                "Restore",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RbNoChange.IsChecked = true;
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"שחזור נכשל:\n{ex.Message}",
                "Restore Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var choice = ResolveChoice();
            await _overrideService.ApplyChoiceAsync(choice).ConfigureAwait(true);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"החלת שינוי ההרשאה נכשלה:\n{ex.Message}",
                "Apply Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private DebugAuthorizationRoleChoice ResolveChoice()
    {
        if (RbAdmin.IsChecked == true)
            return DebugAuthorizationRoleChoice.Administrator;
        if (RbManagement.IsChecked == true)
            return DebugAuthorizationRoleChoice.Management;
        if (RbEmployee.IsChecked == true)
            return DebugAuthorizationRoleChoice.Employee;
        if (RbUnauthorized.IsChecked == true)
            return DebugAuthorizationRoleChoice.Unauthorized;
        if (RbInactive.IsChecked == true)
            return DebugAuthorizationRoleChoice.Inactive;
        return DebugAuthorizationRoleChoice.NoChange;
    }
}
