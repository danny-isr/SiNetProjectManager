using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.Services;
using SiNetSQL.Data;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiNetSQL.Services.AccBootstrap;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.WPF_Window;

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

            // Office Management Project for project-independent workflows
            var officeProjectId = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.OfficeManagementProjectId, "136");
            OfficeProjectIdTextBox.Text = officeProjectId;
            await PreviewOfficeProjectNameAsync(officeProjectId);

            // ACC Inbox folder (project name derived from Office Management project above)
            var inboxFolderName = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InboxFolderName,
                AppConfiguration.InboxFolderName);
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

            // Load stamp template path
            var stampPath = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.StampTemplatePath, string.Empty);
            StampTemplatePathTextBox.Text = stampPath;

            // Load Ollama AI settings
            await LoadOllamaSettingsAsync();

            // Load ACC project template (selection happens against a freshly fetched list).
            await LoadAccTemplateAsync();

            // Load ACC bootstrap-admin email (dedicated service account, not a SIUser).
            var bootstrapAdminEmail = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.AccBootstrapAdminEmail, string.Empty);
            AccBootstrapAdminEmailTextBox.Text = bootstrapAdminEmail;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load system settings from DB");
            MessageBox.Show(
                $"שגיאה בטעינת הגדרות מהדטאבייס: {ex.Message}",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // Validate office project ID
        var officeProjectIdText = OfficeProjectIdTextBox.Text?.Trim() ?? "136";
        if (!int.TryParse(officeProjectIdText, out var officeProjectId) || officeProjectId <= 0)
        {
            MessageBox.Show("נא להזין מספר פרויקט ניהול משרד תקין (מספר חיובי)", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            OfficeProjectIdTextBox.Focus();
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

            // Save office management project ID
            await _settingsService.SetAsync(
                SystemSettingKeys.OfficeManagementProjectId,
                officeProjectId.ToString(),
                "מספר פרויקט ניהול משרד — משמש כברירת מחדל עבור תהליכים ללא פרויקט");

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

            // Save ACC Inbox folder name (project name derived from Office Management project)
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

            // Save stamp template path
            var stampPath = StampTemplatePathTextBox.Text?.Trim() ?? string.Empty;
            await _settingsService.SetAsync(
                SystemSettingKeys.StampTemplatePath,
                stampPath,
                "נתיב לקובץ תבנית חותמת (DWF/PDF) לחתימה על סרטוטים מאושרים");

            // Save Ollama AI model
            await SaveOllamaModelAsync();

            // Save ACC project template selection
            await SaveAccTemplateAsync();

            // Save ACC bootstrap-admin email (trim, persist as-is — empty disables the override).
            var bootstrapAdminEmail = AccBootstrapAdminEmailTextBox.Text?.Trim() ?? string.Empty;
            await _settingsService.SetAsync(
                SystemSettingKeys.AccBootstrapAdminEmail,
                bootstrapAdminEmail,
                "אימייל חשבון השירות שמשמש כאדמין ביצירת פרויקטים ב-ACC. הרשאות החשבון הזה לא משתנות אוטומטית.");

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

    private void BrowseStampTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "בחר קובץ תבנית חותמת DWF (אופציונלי)",
            Filter = "DWF Files (*.dwf)|*.dwf|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            StampTemplatePathTextBox.Text = dialog.FileName;
        }
    }

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

    #region Ollama AI Settings

    /// <summary>
    /// Loads the saved Ollama model and populates the ComboBox with available models from the local server.
    /// </summary>
    private async Task LoadOllamaSettingsAsync()
    {
        // Load saved base URL from DB (fallback to appsettings.json default)
        var savedBaseUrl = await _settingsService.GetOrDefaultAsync(
            SystemSettingKeys.OllamaBaseUrl,
            AppConfiguration.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
        OllamaBaseUrlTextBox.Text = savedBaseUrl;

        // Load saved model preference from DB (fallback to appsettings.json default)
        var savedModel = await _settingsService.GetOrDefaultAsync(
            SystemSettingKeys.OllamaModel,
            AppConfiguration.Configuration["Ollama:Model"] ?? "gemma3:4b");

        try
        {
            // Fetch available models from local Ollama server
            var ollamaService = App.ServiceProvider.GetService<OllamaService>();
            if (ollamaService is null)
            {
                OllamaModelComboBox.Items.Add(savedModel);
                OllamaModelComboBox.Text = savedModel;
                OllamaStatusLabel.Text = "⚠️ שירות AI לא זמין";
                return;
            }

            var models = await FetchOllamaModelsAsync(savedBaseUrl);
            if (models.Count > 0)
            {
                foreach (var model in models)
                    OllamaModelComboBox.Items.Add(model);

                OllamaStatusLabel.Text = $"✅ {models.Count} מודלים זמינים";
            }
            else
            {
                OllamaModelComboBox.Items.Add(savedModel);
                OllamaStatusLabel.Text = "⚠️ שרת Ollama לא מגיב";
            }
        }
        catch
        {
            OllamaModelComboBox.Items.Add(savedModel);
            OllamaStatusLabel.Text = "❌ שרת Ollama לא זמין";
        }

        OllamaModelComboBox.Text = savedModel;
    }

    /// <summary>
    /// Saves the selected Ollama model to DB and updates the running OllamaService instance.
    /// </summary>
    private async Task SaveOllamaModelAsync()
    {
        // Save base URL
        var baseUrl = OllamaBaseUrlTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            await _settingsService.SetAsync(
                SystemSettingKeys.OllamaBaseUrl,
                baseUrl,
                "כתובת שרת Ollama (למשל: http://192.168.1.50:11434)");
        }

        // Save model
        var model = OllamaModelComboBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(model)) return;

        await _settingsService.SetAsync(
            SystemSettingKeys.OllamaModel,
            model,
            "שם מודל AI מקומי (Ollama) לבדיקת הערות ביקורת");

        // Update the running singleton immediately
        var ollamaService = App.ServiceProvider.GetService<OllamaService>();
        if (ollamaService is not null)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
                ollamaService.BaseUrl = baseUrl;
            ollamaService.Model = model;
        }
    }

    /// <summary>
    /// Fetches the list of locally available model names from the Ollama <c>/api/tags</c> endpoint.
    /// </summary>
    private static async Task<List<string>> FetchOllamaModelsAsync(string baseUrl)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var response = await client.GetStringAsync($"{baseUrl}/api/tags").ConfigureAwait(false);
        using var doc = JsonDocument.Parse(response);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var modelsArray))
        {
            foreach (var model in modelsArray.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var nameElement))
                    models.Add(nameElement.GetString() ?? "");
            }
        }

        return models;
    }

    /// <summary>
    /// Tests Ollama connectivity with the currently selected model.
    /// </summary>
    private async void OllamaTestButton_Click(object sender, RoutedEventArgs e)
    {
        var model = OllamaModelComboBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            OllamaStatusLabel.Text = "❌ נא לבחור מודל";
            return;
        }

        OllamaTestButton.IsEnabled = false;
        OllamaStatusLabel.Text = "⏳ בודק חיבור...";

        var testUrl = OllamaBaseUrlTextBox.Text?.Trim() ?? "http://localhost:11434";

        try
        {
            // Test connectivity directly against the URL in the TextBox (not the running singleton)
            using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                using var pingResponse = await testClient.GetAsync($"{testUrl}/api/tags");
                if (!pingResponse.IsSuccessStatusCode)
                {
                    OllamaStatusLabel.Text = $"❌ שרת ב-{testUrl} החזיר שגיאה ({(int)pingResponse.StatusCode})";
                    return;
                }
            }
            catch (Exception)
            {
                OllamaStatusLabel.Text = $"❌ לא ניתן להתחבר ל-{testUrl} — ודא שהשרת פועל";
                return;
            }

            // Verify the selected model exists in the available list
            var models = await FetchOllamaModelsAsync(testUrl);
            if (models.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                OllamaStatusLabel.Text = $"✅ מודל {model} זמין ומוכן";
            }
            else
            {
                OllamaStatusLabel.Text = $"⚠️ שרת פועל אך מודל {model} לא נמצא. הריצו: ollama pull {model}";
            }
        }
        catch (Exception ex)
        {
            OllamaStatusLabel.Text = $"❌ שגיאה: {ex.Message}";
        }
        finally
        {
            OllamaTestButton.IsEnabled = true;
        }
    }

    #endregion

    #region Office Project Preview

    /// <summary>
    /// Looks up the project name for the given ID and shows it next to the textbox.
    /// Also syncs the project title as the ACC Inbox project name (InboxProjectName setting).
    /// </summary>
    private async Task PreviewOfficeProjectNameAsync(string? projectIdText)
    {
        if (!int.TryParse(projectIdText, out var projectId) || projectId <= 0)
        {
            OfficeProjectNamePreview.Text = string.Empty;
            return;
        }

        try
        {
            var dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var project = await db.Projects
                .AsNoTracking()
                .Where(p => p.Id == projectId)
                .Select(p => new { p.NameAndNumber, p.Title })
                .FirstOrDefaultAsync();

            if (project is not null)
            {
                OfficeProjectNamePreview.Text = $"✅ {project.NameAndNumber}";

                // Sync project title as ACC Inbox project name
                if (!string.IsNullOrWhiteSpace(project.Title))
                {
                    await _settingsService.SetAsync(
                        SystemSettingKeys.InboxProjectName,
                        project.Title,
                        "שם פרויקט ה-Inbox ב-ACC — נגזר אוטומטית מפרויקט ניהול משרד");
                }
            }
            else
            {
                OfficeProjectNamePreview.Text = "❌ פרויקט לא נמצא";
            }
        }
        catch
        {
            OfficeProjectNamePreview.Text = "❌ שגיאה בחיפוש";
        }
    }

    #endregion

    #region User Groups

    private void ManageGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new UserGroupManagementWindow { Owner = this };
        window.ShowDialog();
    }

    #endregion

    #region ACC Project Template

    private List<TemplateItem> _accTemplates = new();

    private sealed class TemplateItem
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public override string ToString() => Name;
    }

    private async Task LoadAccTemplateAsync()
    {
        try
        {
            var saved = await _settingsService.GetAsync(SystemSettingKeys.AccProjectTemplateName) ?? string.Empty;

            // Always show the saved value first so the user can see what is currently set,
            // even before fetching the live list (which requires Autodesk auth).
            _accTemplates = string.IsNullOrWhiteSpace(saved)
                ? new List<TemplateItem>()
                : new List<TemplateItem> { new() { Id = "", Name = saved } };

            AccTemplateComboBox.ItemsSource = _accTemplates;
            AccTemplateComboBox.SelectedValue = string.IsNullOrWhiteSpace(saved) ? null : saved;
            AccTemplateStatusLabel.Text = string.IsNullOrWhiteSpace(saved)
                ? "לא הוגדרה תבנית — פרויקטים חדשים ייווצרו ללא תבנית."
                : $"תבנית נוכחית: {saved}. לחצי 'רענן רשימה' כדי לטעון מ-ACC.";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load ACC template setting");
            AccTemplateStatusLabel.Text = $"שגיאה בטעינת ההגדרה: {ex.Message}";
        }
    }

    private async void RefreshTemplatesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshTemplatesButton.IsEnabled = false;
        AccTemplateStatusLabel.Text = "טוען רשימת תבניות מ-ACC...";
        try
        {
            var provisioning = App.ServiceProvider.GetRequiredService<IAccProjectProvisioningService>();
            var templates = await provisioning.ListAvailableTemplatesAsync(CancellationToken.None);

            var saved = await _settingsService.GetAsync(SystemSettingKeys.AccProjectTemplateName) ?? string.Empty;

            _accTemplates = templates
                .Select(t => new TemplateItem { Id = t.Id, Name = t.Name })
                .ToList();

            AccTemplateComboBox.ItemsSource = _accTemplates;
            if (!string.IsNullOrWhiteSpace(saved) &&
                _accTemplates.Any(t => string.Equals(t.Name, saved, StringComparison.Ordinal)))
            {
                AccTemplateComboBox.SelectedValue = saved;
            }

            AccTemplateStatusLabel.Text = _accTemplates.Count == 0
                ? "לא נמצאו תבניות בחשבון. צרי תבנית ב-ACC ואז לחצי 'רענן רשימה'."
                : $"נטענו {_accTemplates.Count} תבניות. בחרי תבנית ולחצי 'שמור'.";
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to list ACC templates");
            AccTemplateStatusLabel.Text = $"שגיאה: {ex.Message}";
        }
        finally
        {
            RefreshTemplatesButton.IsEnabled = true;
        }
    }

    private async Task SaveAccTemplateAsync()
    {
        var selected = AccTemplateComboBox.SelectedValue as string ?? string.Empty;
        await _settingsService.SetAsync(
            SystemSettingKeys.AccProjectTemplateName,
            selected,
            "שם תבנית ה-ACC ליצירת פרויקטים חדשים — מקור הרשאות התיקיות");
    }

    #endregion
}
