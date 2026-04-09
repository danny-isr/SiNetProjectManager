using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManager.Services;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManager.WPF_Window;

/// <summary>
/// Management Settings window - accessible only by administrators.
/// Contains settings that affect business logic and report calculations.
/// Reads/writes key-value settings from the centralized SystemSettings DB table.
/// Can be opened with a notification message (e.g., when default project is missing).
/// </summary>
public partial class ManagementSettingsWindow : Window
{
    private readonly SystemSettingsService _settingsService;
    private readonly StatusColorService? _colorService = StatusColorServiceLocator.Instance;
    private List<StatusColorItem> _statusColors = [];
    private readonly string? _notificationMessage;

    public ManagementSettingsWindow()
    {
        InitializeComponent();
        _settingsService = App.ServiceProvider.GetRequiredService<SystemSettingsService>();
        LoadSettingsAsync();
    }

    /// <summary>
    /// Opens the settings window with a notification highlighting the default project field.
    /// Used when the configured default project cannot be found in the database.
    /// </summary>
    public ManagementSettingsWindow(string notificationMessage) : this()
    {
        _notificationMessage = notificationMessage;

        if (!string.IsNullOrEmpty(_notificationMessage))
        {
            DefaultProjectWarning.Text = _notificationMessage;
            DefaultProjectWarning.Visibility = Visibility.Visible;
            DefaultProjectTitleTextBox.Focus();
        }
    }

    private async void LoadSettingsAsync()
    {
        try
        {
            var defaultProject = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.DefaultProjectTitle, "ניהול  משרד - כללי");
            var hourPrice = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.HourPriceDefault, "280");
            var folderId = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionTemplatesFolderId, string.Empty);

            DefaultProjectTitleTextBox.Text = defaultProject;
            HourPriceTextBox.Text = hourPrice;
            InspectionFolderIdTextBox.Text = folderId;

            // ACC Inbox settings (fall back to appsettings.json defaults)
            var inboxProjectName = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InboxProjectName,
                AppConfiguration.InboxProjectName);
            var inboxFolderName = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InboxFolderName,
                AppConfiguration.InboxFolderName);
            InboxProjectNameTextBox.Text = inboxProjectName;
            InboxFolderNameTextBox.Text = inboxFolderName;

            var reportsFolderId = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionReportsFolderId, string.Empty);
            ReportsFolderIdTextBox.Text = reportsFolderId;

            // Auto-validate folders sequentially (avoids GoogleAuthService race on shared token store)
            if (!string.IsNullOrWhiteSpace(folderId))
                await ValidateFolderIdAsync();

            if (!string.IsNullOrWhiteSpace(reportsFolderId))
                await ValidateReportsFolderIdAsync();

            // Load status label mappings
            await LoadStatusLabelsAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load system settings from DB — falling back to local file");

            // Fallback: read from legacy JSON file
            var legacy = ManagementSettingsManager.LoadSettings();
            DefaultProjectTitleTextBox.Text = legacy.DefaultProjectTitle;
            HourPriceTextBox.Text = legacy.HourPriceDefault.ToString("N0");
            InspectionFolderIdTextBox.Text = legacy.InspectionTemplatesFolderId;
            ReportsFolderIdTextBox.Text = legacy.InspectionReportsFolderId;

            // Fallback for ACC inbox: use appsettings.json values
            InboxProjectNameTextBox.Text = AppConfiguration.InboxProjectName;
            InboxFolderNameTextBox.Text = AppConfiguration.InboxFolderName;
        }

        LoadStatusColors();
    }

    private void LoadStatusColors()
    {
        if (_colorService == null) return;

        try
        {
            _statusColors = _colorService.GetAllStatusDefaults();
            StatusColorsItemsControl.ItemsSource = _statusColors;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load status colors for admin settings");
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var projectTitle = DefaultProjectTitleTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectTitle))
        {
            MessageBox.Show("נא להזין שם פרויקט ברירת מחדל", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            DefaultProjectTitleTextBox.Focus();
            return;
        }

        if (!decimal.TryParse(HourPriceTextBox.Text, out var hourPrice) || hourPrice <= 0)
        {
            MessageBox.Show("נא להזין מחיר שעה תקין (מספר חיובי)", "שגיאה", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Save to centralized DB table
            await _settingsService.SetAsync(
                SystemSettingKeys.DefaultProjectTitle,
                projectTitle,
                "שם הפרויקט שמיילים חדשים ישויכו אליו לפני שיוך ידני");

            await _settingsService.SetAsync(
                SystemSettingKeys.HourPriceDefault,
                hourPrice.ToString("G"),
                "מחיר שעה ברירת מחדל לחישוב שעות בדוחות R01");

            var folderId = InspectionFolderIdTextBox.Text?.Trim() ?? string.Empty;
            await _settingsService.SetAsync(
                SystemSettingKeys.InspectionTemplatesFolderId,
                folderId,
                "Google Drive Folder ID לתיקיית תבניות ביקורת");

            var reportsFolderId = ReportsFolderIdTextBox.Text?.Trim() ?? string.Empty;
            await _settingsService.SetAsync(
                SystemSettingKeys.InspectionReportsFolderId,
                reportsFolderId,
                "Google Drive Folder ID לתיקיית דוחות ביקורת");

            // Save ACC Inbox settings
            var inboxProjectName = InboxProjectNameTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(inboxProjectName))
            {
                await _settingsService.SetAsync(
                    SystemSettingKeys.InboxProjectName,
                    inboxProjectName,
                    "שם פרויקט ה-Inbox ב-ACC שאליו עולים מיילים וקבצים");
            }

            var inboxFolderName = InboxFolderNameTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(inboxFolderName))
            {
                await _settingsService.SetAsync(
                    SystemSettingKeys.InboxFolderName,
                    inboxFolderName,
                    "שם תיקיית ה-Inbox בתוך פרויקט ה-ACC");
            }

            // Save status label mappings
            await SaveStatusLabelsAsync();

            // Keep legacy JSON in sync (backward compatibility)
            SyncToLegacyJson(projectTitle, hourPrice, folderId, reportsFolderId);

            // Save status default colors
            SaveStatusColors();

            MessageBox.Show("ההגדרות נשמרו בהצלחה", "הצלחה", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בשמירת ההגדרות: {ex.Message}", "שגיאה", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Keeps the legacy <c>management_settings.json</c> in sync during the transition period.
    /// Can be removed once all consumers read from SystemSettings DB table.
    /// </summary>
    private static void SyncToLegacyJson(string projectTitle, decimal hourPrice, string folderId, string reportsFolderId)
    {
        try
        {
            var legacy = ManagementSettingsManager.LoadSettings();
            legacy.DefaultProjectTitle = projectTitle;
            legacy.HourPriceDefault = hourPrice;
            legacy.InspectionTemplatesFolderId = folderId;
            legacy.ReportsOutputRoot = string.Empty;
            legacy.InspectionReportsFolderId = reportsFolderId;
            ManagementSettingsManager.SaveSettings(legacy);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to sync legacy management_settings.json");
        }
    }

    private void SaveStatusColors()
    {
        if (_colorService == null) return;

        foreach (var item in _statusColors)
        {
            _colorService.SetDefaultColor(item.StatusId, item.ColorHex);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #region Status Label Mappings

    private async Task LoadStatusLabelsAsync()
    {
        var defaults = InspectionStatusKeys.DefaultLabels;

        StatusLabelPassedTextBox.Text = await _settingsService.GetOrDefaultAsync(
            SystemSettingKeys.StatusLabelPassed, defaults[InspectionStatusKeys.Passed]);
        StatusLabelFailedTextBox.Text = await _settingsService.GetOrDefaultAsync(
            SystemSettingKeys.StatusLabelFailed, defaults[InspectionStatusKeys.Failed]);
        StatusLabelRecurringFailedTextBox.Text = await _settingsService.GetOrDefaultAsync(
            SystemSettingKeys.StatusLabelRecurringFailed, defaults[InspectionStatusKeys.RecurringFailed]);
        StatusLabelNotApplicableTextBox.Text = await _settingsService.GetOrDefaultAsync(
            SystemSettingKeys.StatusLabelNotApplicable, defaults[InspectionStatusKeys.NotApplicable]);
    }

    private async Task SaveStatusLabelsAsync()
    {
        await _settingsService.SetAsync(
            SystemSettingKeys.StatusLabelPassed,
            StatusLabelPassedTextBox.Text?.Trim() ?? InspectionStatusKeys.DefaultLabels[InspectionStatusKeys.Passed],
            "תווית סטטוס: Passed");
        await _settingsService.SetAsync(
            SystemSettingKeys.StatusLabelFailed,
            StatusLabelFailedTextBox.Text?.Trim() ?? InspectionStatusKeys.DefaultLabels[InspectionStatusKeys.Failed],
            "תווית סטטוס: Failed");
        await _settingsService.SetAsync(
            SystemSettingKeys.StatusLabelRecurringFailed,
            StatusLabelRecurringFailedTextBox.Text?.Trim() ?? InspectionStatusKeys.DefaultLabels[InspectionStatusKeys.RecurringFailed],
            "תווית סטטוס: RecurringFailed");
        await _settingsService.SetAsync(
            SystemSettingKeys.StatusLabelNotApplicable,
            StatusLabelNotApplicableTextBox.Text?.Trim() ?? InspectionStatusKeys.DefaultLabels[InspectionStatusKeys.NotApplicable],
            "תווית סטטוס: NotApplicable");
    }

    private void RestoreStatusDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = InspectionStatusKeys.DefaultLabels;
        StatusLabelPassedTextBox.Text = defaults[InspectionStatusKeys.Passed];
        StatusLabelFailedTextBox.Text = defaults[InspectionStatusKeys.Failed];
        StatusLabelRecurringFailedTextBox.Text = defaults[InspectionStatusKeys.RecurringFailed];
        StatusLabelNotApplicableTextBox.Text = defaults[InspectionStatusKeys.NotApplicable];
    }

    #endregion

    #region Reports Folder Validation

    private async void ValidateReportsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await ValidateReportsFolderIdAsync();
    }

    private async void ReportsFolderIdTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var folderId = ReportsFolderIdTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            await ValidateReportsFolderIdAsync();
        }
        else
        {
            ReportsFolderPreviewLabel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ValidateReportsFolderIdAsync()
    {
        var folderId = ReportsFolderIdTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folderId))
        {
            ShowReportsFolderPreview("נא להזין מזהה תיקייה", isError: true);
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[Settings] Validating REPORTS folder ID: {folderId}");

        ShowReportsFolderPreview("⏳ בודק חיבור...", isError: false, isPending: true);
        ValidateReportsFolderButton.IsEnabled = false;

        try
        {
            var provider = CreateTemplateProvider();
            if (provider is null)
            {
                ShowReportsFolderPreview("❌ לא ניתן ליצור חיבור Google — בדוק appsettings.json", isError: true);
                return;
            }

            var folderName = await provider.GetFolderNameAsync(folderId);
            System.Diagnostics.Debug.WriteLine($"[Settings] REPORTS folder name resolved: '{folderName}' (ID={folderId})");
            var idSnippet = folderId.Length > 8 ? folderId[..8] + "…" : folderId;
            ShowReportsFolderPreview($"✅ מחובר לתיקייה: {folderName} ({idSnippet})", isError: false);
        }
        catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            ShowReportsFolderPreview("❌ תיקייה לא נמצאה — מזהה שגוי", isError: true);
        }
        catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            ShowReportsFolderPreview("❌ אין הרשאת גישה לתיקייה", isError: true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("folder", StringComparison.OrdinalIgnoreCase))
        {
            ShowReportsFolderPreview($"❌ {ex.Message}", isError: true);
        }
        catch (Exception ex)
        {
            ShowReportsFolderPreview($"❌ שגיאה: {ex.Message}", isError: true);
        }
        finally
        {
            ValidateReportsFolderButton.IsEnabled = true;
        }
    }

    private void ShowReportsFolderPreview(string text, bool isError, bool isPending = false)
    {
        ReportsFolderPreviewLabel.Text = text;
        ReportsFolderPreviewLabel.Foreground = isPending
            ? Brushes.Gray
            : isError
                ? new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F))
                : new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        ReportsFolderPreviewLabel.Visibility = Visibility.Visible;
    }

    #endregion

    #region Folder Validation

    /// <summary>
    /// Validates the folder ID when the user clicks the validate button.
    /// </summary>
    private async void ValidateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await ValidateFolderIdAsync();
    }

    /// <summary>
    /// Auto-validates when the folder ID text box loses focus and has content.
    /// </summary>
    private async void InspectionFolderIdTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var folderId = InspectionFolderIdTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            await ValidateFolderIdAsync();
        }
        else
        {
            FolderPreviewLabel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Calls <see cref="GoogleInspectionTemplateProvider.GetFolderNameAsync"/> to validate
    /// the folder ID and display the folder name or error message.
    /// </summary>
    private async Task ValidateFolderIdAsync()
    {
        var folderId = InspectionFolderIdTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folderId))
        {
            ShowFolderPreview("נא להזין מזהה תיקייה", isError: true);
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[Settings] Validating TEMPLATES folder ID: {folderId}");

        ShowFolderPreview("⏳ בודק חיבור...", isError: false, isPending: true);
        ValidateFolderButton.IsEnabled = false;

        try
        {
            var provider = CreateTemplateProvider();
            if (provider is null)
            {
                ShowFolderPreview("❌ לא ניתן ליצור חיבור Google — בדוק appsettings.json", isError: true);
                return;
            }

            var folderName = await provider.GetFolderNameAsync(folderId);
            System.Diagnostics.Debug.WriteLine($"[Settings] TEMPLATES folder name resolved: '{folderName}' (ID={folderId})");
            var idSnippet = folderId.Length > 8 ? folderId[..8] + "…" : folderId;
            ShowFolderPreview($"✅ מחובר לתיקייה: {folderName} ({idSnippet})", isError: false);
        }
        catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            ShowFolderPreview("❌ תיקייה לא נמצאה — מזהה שגוי", isError: true);
        }
        catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            ShowFolderPreview("❌ אין הרשאת גישה לתיקייה", isError: true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("folder", StringComparison.OrdinalIgnoreCase))
        {
            ShowFolderPreview($"❌ {ex.Message}", isError: true);
        }
        catch (Exception ex)
        {
            ShowFolderPreview($"❌ שגיאה: {ex.Message}", isError: true);
        }
        finally
        {
            ValidateFolderButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Updates the folder preview label with the given text and color.
    /// </summary>
    private void ShowFolderPreview(string text, bool isError, bool isPending = false)
    {
        FolderPreviewLabel.Text = text;
        FolderPreviewLabel.Foreground = isPending
            ? Brushes.Gray
            : isError
                ? new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F))  // Red
                : new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
        FolderPreviewLabel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Creates a <see cref="GoogleInspectionTemplateProvider"/> using centralized vault-based configuration.
    /// Returns <c>null</c> if Google credentials are not configured.
    /// </summary>
    private static GoogleInspectionTemplateProvider? CreateTemplateProvider()
    {
        if (string.IsNullOrWhiteSpace(AppConfiguration.GetGoogleClientSecretsPath()))
            return null;

        var authService = App.ServiceProvider.GetRequiredService<GoogleAuthService>();
        return new GoogleInspectionTemplateProvider(authService);
    }

    #endregion
}
