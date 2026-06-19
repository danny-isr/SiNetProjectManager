using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2.Services;
using SiNetSQL.Services;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Scans all inspection templates in the configured Google Drive folder,
/// runs <see cref="TemplateTagValidator"/> on each, and displays a consolidated report.
/// </summary>
public partial class TemplateValidationWindow : Window
{
    public TemplateValidationWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Nothing to auto-run; user clicks "Scan" button.
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        EmptyState.Visibility = Visibility.Collapsed;
        ResultsTree.Visibility = Visibility.Collapsed;
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        StatusText.Text = "🔄 מתחבר ל-Google Drive...";

        try
        {
            var authService = CreateGoogleAuthService();
            if (authService is null)
            {
                StatusText.Text = "❌ חיבור Google נכשל";
                return;
            }

            var provider = new GoogleInspectionTemplateProvider(authService);

            var settingsService = App.ServiceProvider.GetRequiredService<SystemSettingsService>();
            var folderId = await settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionTemplatesFolderId, string.Empty);

            var diagnosticService = App.ServiceProvider.GetRequiredService<GoogleDriveFolderDiagnosticService>();
            StatusText.Text = "🔄 בודק גישה לתיקיית תבניות...";
            var diagResult = await diagnosticService.DiagnoseAsync(folderId, isTemplateFolder: true, CancellationToken.None);
            
            if (diagResult.Status != DiagnosticStatus.OK)
            {
                string msg = diagResult.Status switch
                {
                    DiagnosticStatus.NotConfigured => "תיקיית תבניות לא מוגדרת — הגדר InspectionTemplatesFolderId בהגדרות ניהול.",
                    DiagnosticStatus.GoogleNotConfigured => "חיבור Google לא מוגדר במערכת.",
                    DiagnosticStatus.NotAuthenticated => "משתמש לא מחובר לחשבון Google.",
                    DiagnosticStatus.NoAccess => $"אין הרשאת גישה לתיקייה. משתמש מחובר: {diagResult.ConnectedEmail}",
                    DiagnosticStatus.NotFound => "התיקייה לא נמצאה ב-Google Drive. ייתכן שנמחקה.",
                    DiagnosticStatus.InvalidType => "ה-ID שהוגדר אינו שייך לתיקייה.",
                    DiagnosticStatus.EmptyFolder => "לא נמצאו תבניות (Spreadsheets) בתיקייה.",
                    _ => $"שגיאה בגישה לתיקייה: {diagResult.TechnicalDetails}"
                };
                StatusText.Text = $"❌ {msg}";
                return;
            }

            // 1. Fetch all templates
            StatusText.Text = "🔄 טוען רשימת תבניות...";
            var templates = await provider.GetAvailableTemplatesAsync(folderId, CancellationToken.None);

            if (templates.Count == 0)
            {
                StatusText.Text = "⚠️ לא נמצאו תבניות בתיקייה.";
                return;
            }

            // 2. Scan each template
            ScanProgress.IsIndeterminate = false;
            ScanProgress.Minimum = 0;
            ScanProgress.Maximum = templates.Count;
            ScanProgress.Value = 0;

            var reportItems = new List<TemplateReportItem>();
            int totalErrors = 0;
            int totalWarnings = 0;
            int scanned = 0;

            foreach (var template in templates)
            {
                scanned++;
                ScanProgress.Value = scanned;
                StatusText.Text = $"🔄 סורק {scanned}/{templates.Count}: {template.Name}";

                var item = await ScanSingleTemplateAsync(provider, template);
                reportItems.Add(item);
                totalErrors += item.ErrorCount;
                totalWarnings += item.WarningCount;
            }

            // 3. Sort: errors first, then warnings, then clean
            reportItems.Sort((a, b) =>
            {
                if (a.HasErrors != b.HasErrors) return a.HasErrors ? -1 : 1;
                if (a.HasWarnings != b.HasWarnings) return a.HasWarnings ? -1 : 1;
                return string.Compare(a.TemplateName, b.TemplateName, StringComparison.Ordinal);
            });

            // 4. Display
            ResultsTree.ItemsSource = reportItems;
            ResultsTree.Visibility = Visibility.Visible;
            ScanProgress.Visibility = Visibility.Collapsed;

            var cleanCount = reportItems.Count(r => !r.HasErrors && !r.HasWarnings);
            StatusText.Text = $"✅ סריקה הושלמה — {templates.Count} תבניות נבדקו.";
            SummaryText.Text = $"סה\"כ: {templates.Count} תבניות | " +
                               $"✅ {cleanCount} תקינות | " +
                               $"❌ {totalErrors} שגיאות | " +
                               $"⚠️ {totalWarnings} אזהרות";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ שגיאה: {ex.Message}";
            ScanProgress.Visibility = Visibility.Collapsed;
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Scans a single template and returns a report item with errors/warnings.
    /// </summary>
    private static async Task<TemplateReportItem> ScanSingleTemplateAsync(
        GoogleInspectionTemplateProvider provider,
        InspectionTemplateItem template)
    {
        var item = new TemplateReportItem { TemplateName = template.Name };

        try
        {
            var scanResult = await provider.ScanAndParseTemplateAsync(
                template.SpreadsheetId, CancellationToken.None);

            // Tag count summary
            item.TotalTagCount = scanResult.AllTags.Count;
            item.StatusTagCount = scanResult.AllTags.Count(t => t.IsStatusTag);
            item.NoteInputTagCount = scanResult.AllTags.Count(t => t.IsNoteInputTag);
            item.GeneralTagCount = scanResult.AllTags.Count(t => t.IsGeneralTag);
            item.SyncRowCount = scanResult.SyncRows.Count;

            // Add info line about tag counts
            item.Children.Add(new TemplateReportDetail
            {
                Icon = "ℹ️",
                RuleCode = "INFO",
                Message = $"נמצאו {item.StatusTagCount} תגי הגדרה, " +
                          $"{item.NoteInputTagCount} תגי קלט, " +
                          $"{item.GeneralTagCount} תגים כלליים, " +
                          $"{item.SyncRowCount} שורות סנכרון.",
                IsInfo = true
            });

            // Validation errors
            foreach (var error in scanResult.ValidationErrors)
            {
                var isWarning = error.RuleCode is "MISSING_HEADER_AND_INPUT"
                    or "CHAPTER_TITLE_MISMATCH";

                item.Children.Add(new TemplateReportDetail
                {
                    Icon = isWarning ? "⚠️" : "❌",
                    RuleCode = error.RuleCode,
                    Message = error.Message,
                    IsError = !isWarning,
                    IsInfo = false
                });

                if (isWarning) item.WarningCount++;
                else item.ErrorCount++;
            }

            // Additional structural checks beyond TemplateTagValidator

            // Check for orphan note-input tags (no matching status tag at all)
            var statusCodes = new HashSet<string>(
                scanResult.AllTags.Where(t => t.IsStatusTag).Select(t => t.SectionCode),
                StringComparer.Ordinal);

            var noteInputCodes = scanResult.AllTags
                .Where(t => t.IsNoteInputTag)
                .Select(t => t.SectionCode)
                .Distinct(StringComparer.Ordinal);

            foreach (var code in noteInputCodes)
            {
                if (!statusCodes.Contains(code))
                {
                    // Already reported by MISSING_HEADER — skip duplicate
                    continue;
                }
            }

            // Check for sections in odd column positions (potential template layout issue)
            var rightmostCol = scanResult.AllTags
                .Where(t => t.IsStatusTag)
                .Select(t => t.Col)
                .DefaultIfEmpty(-1)
                .Max();

            if (rightmostCol > 3) // Tags beyond column D might indicate layout issues
            {
                item.Children.Add(new TemplateReportDetail
                {
                    Icon = "⚠️",
                    RuleCode = "WIDE_LAYOUT",
                    Message = $"תגי הגדרה נמצאו עד עמודה {(char)('A' + rightmostCol)} — ייתכן שיש בעיית מבנה בתבנית.",
                    IsInfo = false
                });
                item.WarningCount++;
            }

            // If no errors/warnings at all
            if (item.ErrorCount == 0 && item.WarningCount == 0)
            {
                item.Children.Add(new TemplateReportDetail
                {
                    Icon = "✅",
                    RuleCode = "OK",
                    Message = "התבנית תקינה — כל התגים מותאמים ועוברים ולידציה.",
                    IsInfo = true
                });
            }
        }
        catch (Exception ex)
        {
            item.Children.Add(new TemplateReportDetail
            {
                Icon = "💥",
                RuleCode = "SCAN_ERROR",
                Message = $"שגיאה בסריקת תבנית: {ex.Message}",
                IsError = true
            });
            item.ErrorCount++;
        }

        return item;
    }

    /// <summary>
    /// Creates a <see cref="GoogleAuthService"/> using centralized configuration.
    /// </summary>
    private GoogleAuthService? CreateGoogleAuthService()
    {
        if (string.IsNullOrWhiteSpace(AppConfiguration.GetGoogleClientSecretsPath()))
        {
            MessageBox.Show(
                "Google OAuth credentials לא מוגדרים.\nהגדירו אותם דרך חלון הגדרת מפתחות.",
                "Config Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        return App.ServiceProvider.GetRequiredService<GoogleAuthService>();
    }
}

// ─────────────────────────────────────────────────────────────
//  Report View Models
// ─────────────────────────────────────────────────────────────

/// <summary>Report item for a single template (parent node in the TreeView).</summary>
public sealed class TemplateReportItem
{
    public required string TemplateName { get; init; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int TotalTagCount { get; set; }
    public int StatusTagCount { get; set; }
    public int NoteInputTagCount { get; set; }
    public int GeneralTagCount { get; set; }
    public int SyncRowCount { get; set; }
    public List<TemplateReportDetail> Children { get; } = [];

    public bool HasErrors => ErrorCount > 0;
    public bool HasWarnings => WarningCount > 0;
    public bool IsExpanded => HasErrors || HasWarnings;

    public string Icon => HasErrors ? "❌" : HasWarnings ? "⚠️" : "✅";

    public string Summary => HasErrors
        ? $"{ErrorCount} שגיאות"
        : HasWarnings
            ? $"{WarningCount} אזהרות"
            : "תקינה";

    public string TagCountDisplay =>
        $"({StatusTagCount} הגדרות, {NoteInputTagCount} קלט, {GeneralTagCount} כלליים)";
}

/// <summary>Single error/warning/info line within a template report.</summary>
public sealed class TemplateReportDetail
{
    public required string Icon { get; init; }
    public required string RuleCode { get; init; }
    public required string Message { get; init; }
    public bool IsError { get; init; }
    public bool IsInfo { get; init; }
}
