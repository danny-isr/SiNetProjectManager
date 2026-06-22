using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2.Services;
using SiNetProjectManagerV2.Services.Migration;
using SiNetSQL.Data;
using SiNetSQL.Services;
using SiNetSQL.Services.InspectionSync;
using SiNetSQL.Models;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2;

/// <summary>
/// Migration PoC window with two tabs:
/// <list type="bullet">
///   <item><b>Tab 1 — Extraction</b>: Smart content extraction from final inspection reports.</item>
///   <item><b>Tab 2 — Task Generation</b>: Scan a Google Index Sheet, pre-check DB config, and create tasks.</item>
/// </list>
/// </summary>
public partial class MigrationPocWindow : Window
{
    // ── Task Generation state (preserved between Scan → Preview → Generate steps) ──
    private IndexSheetResult? _lastScanResult;
    private MigrationPreviewResult? _lastPreviewResult;

    // ── Template picker state ──
    private List<InspectionTemplateItem> _availableTemplates = [];
    private InspectionTemplateItem? _selectedTemplate;
    private bool _suppressTemplateBoxUpdate;

    // ── Project picker state ──
    private List<Project> _availableProjects = [];
    private Project? _selectedProject;

    // ── Index sheet cache (loaded once, reused for project filtering + report resolution) ──
    private List<IndexSheetReportLink> _indexSheetLinks = [];

    // ── New Migration Preview State ──
    private List<SystemUserLookupItem> _systemUsers = [];
    private List<ReviewerMappingItem> _reviewerMappings = [];

    public MigrationPocWindow()
    {
        InitializeComponent();
    }

    // ════════════════════════════════════════════════════════════════
    //  Window Initialization
    // ════════════════════════════════════════════════════════════════

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadIndexSheetLinksAsync();
        await LoadProjectsAsync();
        await LoadTemplatesAsync();
        await PreFillDefaultReportAsync();
        await LoadSystemUsersAsync();
    }

    private async Task LoadSystemUsersAsync()
    {
        try
        {
            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();
            var allUsers = await context.Siusers
                .Where(u => u.IsActive && u.IsDomainGroup != true)
                .Select(u => new SystemUserLookupItem { UserId = u.Id, DisplayName = u.DisplayName ?? u.Name ?? string.Empty })
                .OrderBy(u => u.DisplayName)
                .ToListAsync();
            
            _systemUsers = allUsers;
        }
        catch (Exception ex)
        {
            AppendToLog($"[Users] Error loading system users: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the index sheet once and caches all project rows with hyperlinks.
    /// Used for both project filtering and report resolution.
    /// </summary>
    private async Task LoadIndexSheetLinksAsync()
    {
        try
        {
            var indexSheetId = GetIndexSheetId();
            if (string.IsNullOrWhiteSpace(indexSheetId)) return;

            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            StatusLabel.Text = "🔄 קורא גיליון אינדקס...";

            var reader = new IndexSheetReader(authService);
            _indexSheetLinks = await reader.ReadReportHyperlinksAsync(
                indexSheetId, log: msg => AppendToLog($"[Index] {msg}"));

            AppendToLog($"[Index] Loaded {_indexSheetLinks.Count} rows with report hyperlinks from index sheet.");
        }
        catch (Exception ex)
        {
            AppendToLog($"[Index] Error loading index sheet: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads available templates from the admin-configured Google Drive folder
    /// (same folder used by the Floating Inspection window).
    /// </summary>
    private async Task LoadTemplatesAsync()
    {
        try
        {
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            var provider = new GoogleInspectionTemplateProvider(authService);

            var settingsService = App.ServiceProvider.GetRequiredService<SystemSettingsService>();
            var folderId = await settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionTemplatesFolderId, string.Empty);

            if (string.IsNullOrWhiteSpace(folderId))
            {
                AppendToLog("[Templates] Folder ID not configured — set InspectionTemplatesFolderId in Management Settings.");
                return;
            }

            StatusLabel.Text = "🔄 טוען תבניות...";

            _availableTemplates = await provider.GetAvailableTemplatesAsync(folderId, CancellationToken.None);
            TemplateComboBox.ItemsSource = _availableTemplates;

            // Auto-select "דוח תנועה תבנית" for testing convenience
            var defaultTemplate = _availableTemplates.FirstOrDefault(t =>
                t.Name.Contains("תנועה", StringComparison.OrdinalIgnoreCase));
            if (defaultTemplate != null)
                TemplateComboBox.SelectedItem = defaultTemplate;

            StatusLabel.Text = _availableTemplates.Count > 0
                ? $"✅ {_availableTemplates.Count} תבניות נמצאו"
                : "לא נמצאו תבניות בתיקייה";
        }
        catch (Exception ex)
        {
            AppendToLog($"[Templates] Error loading templates: {ex.Message}");
            StatusLabel.Text = "⚠ שגיאה בטעינת תבניות — השתמשו בהזנה ידנית";
            // Show manual fallback
            TemplateIdBox.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Loads projects by cross-referencing index sheet rows with the DB.
    /// Only projects that appear in both the index sheet and DB are shown.
    /// Falls back to all active projects if the index sheet has no data.
    /// Pre-selects the active project, or defaults to project 2774.
    /// </summary>
    private async Task LoadProjectsAsync()
    {
        try
        {
            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();

            var allProjects = await context.Projects
                .Where(p => p.EndOfProject != true)
                .OrderBy(p => p.NameAndNumber)
                .ToListAsync();

            if (_indexSheetLinks.Count > 0)
            {
                // Only show projects that have at least one row in the index sheet
                _availableProjects = allProjects.Where(p => FindMatchingIndexRows(p).Count > 0).ToList();
                AppendToLog($"[Projects] {_availableProjects.Count}/{allProjects.Count} DB projects matched index sheet rows.");
            }
            else
            {
                _availableProjects = allProjects;
            }

            ProjectComboBox.ItemsSource = _availableProjects;

            // Pre-select the active project, or fall back to project 2774
            var activeProject = ActiveProjectContext.Instance.ActiveProject;
            Project? match = null;

            if (activeProject != null)
                match = _availableProjects.FirstOrDefault(p => p.Id == activeProject.Id);

            match ??= _availableProjects.FirstOrDefault(p => p.Number.HasValue && (int)p.Number.Value == 2774);

            if (match != null)
                ProjectComboBox.SelectedItem = match;
        }
        catch (Exception ex)
        {
            AppendToLog($"[Projects] Error loading projects: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds all index sheet rows that match a given DB project.
    /// Primary: extract leading number from ProjectRef and compare to project.Number.
    /// Fallback: exact string equality (only when no number available).
    /// </summary>
    private List<IndexSheetReportLink> FindMatchingIndexRows(Project project)
    {
        if (_indexSheetLinks.Count == 0) return [];

        var projectNumber = project.Number?.ToString("0") ?? "";

        return _indexSheetLinks.Where(l =>
        {
            // Strategy 1 (primary): compare leading number from sheet with DB project number
            var refNumber = ExtractLeadingNumber(l.ProjectRef);
            if (!string.IsNullOrEmpty(refNumber) && !string.IsNullOrEmpty(projectNumber))
                return refNumber == projectNumber;

            // Strategy 2: word-boundary regex (handles "פרויקט 2774" where number is not leading)
            if (!string.IsNullOrEmpty(projectNumber))
            {
                var pattern = new Regex(@"(?<!\d)" + Regex.Escape(projectNumber) + @"(?!\d)");
                return pattern.IsMatch(l.ProjectRef);
            }

            // Strategy 3: exact string match (only when project has no number)
            var projectName = project.NameAndNumber ?? project.Title ?? "";
            return !string.IsNullOrWhiteSpace(projectName) &&
                   l.ProjectRef.Equals(projectName, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    /// <summary>Extracts leading digits from a string (e.g. "2774 - נתניה" → "2774").</summary>
    private static string ExtractLeadingNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var m = Regex.Match(text.Trim(), @"^\d+");
        return m.Success ? m.Value : "";
    }

    private async void ProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedProject = ProjectComboBox.SelectedItem as Project;
        if (_selectedProject == null) return;

        // Update the active project context so PreFillDefaultReportAsync picks it up
        ActiveProjectContext.Instance.SetActiveProject(_selectedProject);
        await PreFillDefaultReportAsync();
    }

    /// <summary>
    /// Pre-fills the Report ID field using the cached index sheet data.
    /// Falls back to the DB's SourceFileUrn if the index sheet has no match.
    /// </summary>
    private async Task PreFillDefaultReportAsync()
    {
        try
        {
            var activeProject = ActiveProjectContext.Instance.ActiveProject;
            if (activeProject == null) return;

            // ── Try cached index sheet hyperlinks ──
            var resolved = ResolveReportFromCachedIndex(activeProject);
            if (resolved != null)
            {
                ReportIdBox.Text = resolved;
                return;
            }

            // ── Fallback: read from DB ──
            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();

            var latestReport = await context.Set<SiNetSQL.Models.InspectionReport>()
                .Where(r => r.ProjectId == activeProject.Id && r.SourceFileUrn != null)
                .OrderByDescending(r => r.ReportNumber)
                .FirstOrDefaultAsync();

            if (latestReport?.SourceFileUrn != null)
            {
                var extractedId = ReportContentExtractor.ExtractSpreadsheetId(latestReport.SourceFileUrn);
                ReportIdBox.Text = extractedId;
                AppendToLog($"[Default] Pre-filled report from DB: #{latestReport.ReportNumber} — {extractedId}");
            }
        }
        catch (Exception ex)
        {
            AppendToLog($"[Default] Could not pre-fill report: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the latest report spreadsheet ID from the cached index sheet data.
    /// Returns null if no matching project row or no hyperlinks.
    /// </summary>
    private string? ResolveReportFromCachedIndex(Project project)
    {
        var matching = FindMatchingIndexRows(project);
        if (matching.Count == 0)
        {
            var projectName = project.NameAndNumber ?? project.Title ?? "";
            var projectNumber = project.Number?.ToString("0") ?? "";
            AppendToLog($"[Index] No row matched project '{projectName}' (#{projectNumber}).");
            return null;
        }

        // Take the second report version (index 1) from the last matching row for testing
        var best = matching.Last();
        var versions = best.ReportSpreadsheetIds;
        int pickIndex = Math.Min(1, versions.Count - 1); // second if available, else last
        var selectedId = versions[pickIndex];

        AppendToLog($"[Index] Resolved report for '{best.ProjectRef}' row {best.RowIndex + 1}: " +
            $"{versions.Count} version(s), using index {pickIndex} → {selectedId}");

        return selectedId;
    }

    /// <summary>
    /// Gets the index sheet ID from config or from Tab 2's input box.
    /// </summary>
    private string? GetIndexSheetId()
    {
        // 1. Try Tab 2's input (user may have typed it there)
        var fromTab2 = IndexSheetIdBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(fromTab2))
            return ReportContentExtractor.ExtractSpreadsheetId(fromTab2);

        // 2. Try config
        try
        {
            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!System.IO.File.Exists(configPath)) return null;

            var json = System.IO.File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<MigrationGoogleConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var indexId = config?.GoogleReports?.IndexSheetId;
            if (!string.IsNullOrWhiteSpace(indexId))
            {
                // Also pre-fill Tab 2's input for convenience
                IndexSheetIdBox.Text = indexId;
                return indexId;
            }
        }
        catch { /* ignore config errors */ }

        return null;
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedTemplate = TemplateComboBox.SelectedItem as InspectionTemplateItem;

        if (_selectedTemplate != null)
        {
            _suppressTemplateBoxUpdate = true;
            TemplateIdBox.Text = _selectedTemplate.SpreadsheetId;
            TemplateIdBox.Visibility = Visibility.Collapsed;
            _suppressTemplateBoxUpdate = false;
        }
    }

    private void TemplateIdBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTemplateBoxUpdate) return;

        // If user types manually, clear dropdown selection
        if (_selectedTemplate != null)
        {
            _selectedTemplate = null;
            TemplateComboBox.SelectedItem = null;
        }
    }

    private async void RefreshTemplates_Click(object sender, RoutedEventArgs e)
    {
        TemplateComboBox.ItemsSource = null;
        _selectedTemplate = null;
        await LoadTemplatesAsync();
    }

    private void ToggleManualTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateIdBox.Visibility == Visibility.Collapsed)
        {
            TemplateIdBox.Visibility = Visibility.Visible;
            TemplateComboBox.SelectedItem = null;
            _selectedTemplate = null;
            TemplateIdBox.Focus();
        }
        else
        {
            TemplateIdBox.Visibility = Visibility.Collapsed;
            TemplateIdBox.Text = "";
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  TAB 1: Content Extraction
    // ════════════════════════════════════════════════════════════════

    private async void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        var templateInput = _selectedTemplate?.SpreadsheetId ?? TemplateIdBox.Text.Trim();
        var reportInput = ReportIdBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(templateInput) || string.IsNullOrWhiteSpace(reportInput))
        {
            MessageBox.Show("יש להזין מזהה/קישור גם לתבנית וגם לדוח הסופי.", "שדות חסרים",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var templateId = ReportContentExtractor.ExtractSpreadsheetId(templateInput);
        var reportId = ReportContentExtractor.ExtractSpreadsheetId(reportInput);

        ExtractButton.IsEnabled = false;
        StatusLabel.Text = "🔄 מתחבר ל-Google Sheets...";
        LogBox.Text = "";
        ResultsGrid.ItemsSource = null;

        try
        {
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            StatusLabel.Text = "🔍 שלב 1: סורק תבנית...";

            var extractor = new ReportContentExtractor(authService);
            var result = await extractor.ExtractAsync(templateId, reportId);

            if (result.IsSuccess)
            {
                ResultsGrid.ItemsSource = result.Sections;

                var splitCount = result.Sections.Count(s => s.WasSplit);
                var resolvedCount = result.Sections.Count(s => s.IsResolved);
                var datedCount = result.Sections.Count(s => s.ClosedDate != null);
                StatusLabel.Text = $"✅ חולצו {result.Sections.Count} שורות " +
                    $"({splitCount} פוצלו, {resolvedCount} נסגרו, {datedCount} עם תאריך), " +
                    $"{result.GeneralFields.Count} שדות כלליים.";

                var log = new System.Text.StringBuilder();
                log.AppendLine($"Template: {templateId}");
                log.AppendLine($"Report:   {reportId}");
                log.AppendLine($"Total rows extracted: {result.Sections.Count}");
                log.AppendLine($"  Split from merged cells: {splitCount}");
                log.AppendLine($"  Resolved/Closed (gray): {resolvedCount}");
                log.AppendLine($"  With closure date: {datedCount}");
                log.AppendLine($"General fields: {result.GeneralFields.Count}");
                log.AppendLine();

                if (result.GeneralFields.Count > 0)
                {
                    log.AppendLine("── General Fields ──");
                    foreach (var (key, value) in result.GeneralFields)
                        log.AppendLine($"  {key} = {value}");
                    log.AppendLine();
                }

                if (result.Warnings.Count > 0)
                {
                    log.AppendLine("── Warnings ──");
                    foreach (var w in result.Warnings)
                        log.AppendLine($"  ⚠ {w}");
                }

                log.AppendLine();
                log.AppendLine("── JSON Output ──");
                var jsonOutput = JsonSerializer.Serialize(result.Sections, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                log.AppendLine(jsonOutput);

                LogBox.Text = log.ToString();
                System.Diagnostics.Debug.WriteLine("═══ Migration PoC — Extraction Result ═══");
                System.Diagnostics.Debug.WriteLine(log.ToString());
            }
            else
            {
                StatusLabel.Text = $"❌ שגיאה: {result.ErrorMessage}";
                LogBox.Text = $"ERROR: {result.ErrorMessage}\n\nWarnings:\n{string.Join("\n", result.Warnings)}";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"❌ שגיאה: {ex.Message}";
            LogBox.Text = $"EXCEPTION: {ex}";
            System.Diagnostics.Debug.WriteLine($"[MigrationPoC] ERROR: {ex}");
        }
        finally
        {
            ExtractButton.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  TAB 1: AI Extraction (Gemini)
    // ════════════════════════════════════════════════════════════════

    private async void AiExtractButton_Click(object sender, RoutedEventArgs e)
    {
        var templateInput = _selectedTemplate?.SpreadsheetId ?? TemplateIdBox.Text.Trim();
        var reportInput = ReportIdBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(templateInput) || string.IsNullOrWhiteSpace(reportInput))
        {
            MessageBox.Show("יש להזין מזהה/קישור גם לתבנית וגם לדוח הסופי.", "שדות חסרים",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var geminiApiKey = AppConfiguration.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            MessageBox.Show("מפתח Gemini API לא מוגדר.\nהגדירו אותו דרך חלון הגדרת מפתחות.",
                "הגדרת AI חסרה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var geminiModel = ReadGeminiSetting("GeminiModel") ?? "gemini-2.5-flash";
        _ = int.TryParse(ReadGeminiSetting("GeminiTimeoutSeconds"), out var timeoutSec);
        if (timeoutSec <= 0) timeoutSec = 300;

        var templateId = ReportContentExtractor.ExtractSpreadsheetId(templateInput);
        var reportId = ReportContentExtractor.ExtractSpreadsheetId(reportInput);

        ExtractButton.IsEnabled = false;
        AiExtractButton.IsEnabled = false;
        StatusLabel.Text = "🤖 מתחיל חילוץ AI...";
        var liveLog = new System.Text.StringBuilder();
        liveLog.AppendLine("═══ Gemini AI Extraction — Live Log ═══");
        liveLog.AppendLine($"⏱ Start: {DateTime.Now:HH:mm:ss}");
        liveLog.AppendLine($"📋 Model: {geminiModel} | Timeout: {timeoutSec}s");
        liveLog.AppendLine($"📄 Template: {templateId}");
        liveLog.AppendLine($"📄 Report:   {reportId}");
        liveLog.AppendLine();
        LogBox.Text = liveLog.ToString();
        ResultsGrid.ItemsSource = null;

        try
        {
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            var geminiService = new GeminiExtractionService(authService, geminiApiKey, geminiModel, timeoutSec);
            var result = await geminiService.ExtractWithAiAsync(
                templateId, reportId,
                onProgress: msg => Dispatcher.Invoke(() =>
                {
                    StatusLabel.Text = msg;
                    liveLog.AppendLine($"  {msg}");
                    LogBox.Text = liveLog.ToString();
                }));

            if (result.IsSuccess)
            {
                ResultsGrid.ItemsSource = result.Sections;

                var withNotes = result.Sections.Count(s => !string.IsNullOrWhiteSpace(s.NoteText));
                var resolvedCount = result.Sections.Count(s => s.IsResolved);
                StatusLabel.Text = $"🤖 ✅ AI חילץ {result.Sections.Count} סעיפים " +
                    $"({withNotes} עם הערות, {resolvedCount} נסגרו).";

                liveLog.AppendLine();
                liveLog.AppendLine($"✅ SUCCESS — {result.Sections.Count} sections");
                liveLog.AppendLine();

                if (result.GeneralFields.Count > 0)
                {
                    var reportFields = result.GeneralFields
                        .Where(kv => !kv.Key.StartsWith('_'))
                        .ToList();
                    var aiMeta = result.GeneralFields
                        .Where(kv => kv.Key.StartsWith('_'))
                        .ToList();

                    if (reportFields.Count > 0)
                    {
                        liveLog.AppendLine("── שדות חופשיים (General Fields) ──");
                        foreach (var (key, value) in reportFields)
                            liveLog.AppendLine($"  {key} = {value}");
                        liveLog.AppendLine();
                    }

                    if (aiMeta.Count > 0)
                    {
                        liveLog.AppendLine("── AI Metadata ──");
                        foreach (var (key, value) in aiMeta)
                            liveLog.AppendLine($"  {key} = {value}");
                        liveLog.AppendLine();
                    }
                }

                if (result.Warnings.Count > 0)
                {
                    liveLog.AppendLine("── Service Diagnostics ──");
                    foreach (var w in result.Warnings)
                        liveLog.AppendLine($"  {w}");
                    liveLog.AppendLine();
                }

                // ── Save extraction result to JSON cache ──
                await SaveExtractionToCacheAsync(result, reportId, liveLog);

                liveLog.AppendLine("── JSON Output ──");
                var jsonOutput = JsonSerializer.Serialize(result.Sections, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                liveLog.AppendLine(jsonOutput);

                LogBox.Text = liveLog.ToString();
                AppLogger.Info($"[GeminiAI-UI] Success: {result.Sections.Count} sections, {withNotes} with notes");
            }
            else
            {
                StatusLabel.Text = "🤖 ❌ שגיאה בחילוץ AI";

                liveLog.AppendLine();
                liveLog.AppendLine("═══ ❌ ERROR ═══");
                liveLog.AppendLine();
                liveLog.AppendLine($"הודעה: {result.ErrorMessage}");
                liveLog.AppendLine();

                if (result.Warnings.Count > 0)
                {
                    liveLog.AppendLine("── Service Diagnostics (collected before error) ──");
                    foreach (var w in result.Warnings)
                        liveLog.AppendLine($"  {w}");
                    liveLog.AppendLine();
                }

                var diagPath = Path.Combine(
                    Environment.ExpandEnvironmentVariables("%APPDATA%"),
                    "SiNet", "Logs", "GeminiDiag");
                liveLog.AppendLine($"📂 Check diagnostics: {diagPath}");

                LogBox.Text = liveLog.ToString();
                AppLogger.Warn($"[GeminiAI-UI] Error result: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "🤖 ❌ שגיאה";
            AppLogger.Error(ex, "[GeminiAI-UI] Unhandled exception in AiExtractButton_Click");

            liveLog.AppendLine();
            liveLog.AppendLine("═══ ❌ EXCEPTION ═══");
            liveLog.AppendLine();
            liveLog.AppendLine($"סוג: {ex.GetType().FullName}");
            liveLog.AppendLine($"הודעה: {ex.Message}");
            if (ex.InnerException != null)
            {
                liveLog.AppendLine($"פנימי: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            liveLog.AppendLine();
            liveLog.AppendLine("── Full Stack Trace ──");
            liveLog.AppendLine(ex.ToString());
            liveLog.AppendLine();

            var diagPath = Path.Combine(
                Environment.ExpandEnvironmentVariables("%APPDATA%"),
                "SiNet", "Logs", "GeminiDiag");
            liveLog.AppendLine($"📂 Check diagnostics: {diagPath}");

            LogBox.Text = liveLog.ToString();
        }
        finally
        {
            ExtractButton.IsEnabled = true;
            AiExtractButton.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  TAB 1: Batch AI Extraction (all projects × all versions)
    // ════════════════════════════════════════════════════════════════

    private async void BatchExtractButton_Click(object sender, RoutedEventArgs e)
    {
        // ── Validate prerequisites ──
        var templateInput = _selectedTemplate?.SpreadsheetId ?? TemplateIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(templateInput))
        {
            MessageBox.Show("יש לבחור תבנית לפני הרצת batch.", "תבנית חסרה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_indexSheetLinks.Count == 0)
        {
            MessageBox.Show("גיליון אינדקס לא נטען — אין נתוני פרויקטים.", "אינדקס חסר",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var geminiApiKey = AppConfiguration.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            MessageBox.Show("מפתח Gemini API לא מוגדר.\nהגדירו אותו דרך חלון הגדרת מפתחות.",
                "הגדרת AI חסרה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // ── Build work items: (project, link, versionIndex, spreadsheetId) ──
        var workItems = new List<(Project Project, IndexSheetReportLink Link, int VersionIdx, string SpreadsheetId)>();
        foreach (var project in _availableProjects)
        {
            var matching = FindMatchingIndexRows(project);
            foreach (var link in matching)
            {
                for (int v = 0; v < link.ReportSpreadsheetIds.Count; v++)
                    workItems.Add((project, link, v, link.ReportSpreadsheetIds[v]));
            }
        }

        if (workItems.Count == 0)
        {
            MessageBox.Show("לא נמצאו גרסאות דוחות לחילוץ.", "אין נתונים",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // ── Skip already cached ──
        var pending = workItems.Where(w =>
        {
            var projNum = w.Project.Number?.ToString("0") ?? "";
            return !string.IsNullOrWhiteSpace(projNum)
                && !ExtractionCacheService.Exists(projNum, w.VersionIdx + 1, w.Link.ReportNumber);
        }).ToList();

        var skipped = workItems.Count - pending.Count;

        var confirm = MessageBox.Show(
            $"נמצאו {workItems.Count} גרסאות דוח ({_availableProjects.Count} פרויקטים).\n" +
            $"כבר בקאש: {skipped}\nלחילוץ: {pending.Count}\n\nלהמשיך?",
            "אישור Batch Extraction",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        // ── Setup UI ──
        var geminiModel = ReadGeminiSetting("GeminiModel") ?? "gemini-2.5-flash";
        _ = int.TryParse(ReadGeminiSetting("GeminiTimeoutSeconds"), out var timeoutSec);
        if (timeoutSec <= 0) timeoutSec = 300;
        _ = int.TryParse(ReadGeminiSetting("GeminiBatchConcurrency"), out var maxConcurrency);
        if (maxConcurrency <= 0) maxConcurrency = 4;

        var templateId = ReportContentExtractor.ExtractSpreadsheetId(templateInput);

        ExtractButton.IsEnabled = false;
        AiExtractButton.IsEnabled = false;
        BatchExtractButton.IsEnabled = false;
        ResultsGrid.ItemsSource = null;

        var batchLog = new System.Text.StringBuilder();
        batchLog.AppendLine("═══ Batch AI Extraction ═══");
        batchLog.AppendLine($"⏱ Start: {DateTime.Now:HH:mm:ss}");
        batchLog.AppendLine($"📋 Template: {templateId}");
        batchLog.AppendLine($"📊 Total: {workItems.Count} versions, {skipped} cached, {pending.Count} to extract");
        batchLog.AppendLine();
        LogBox.Text = batchLog.ToString();

        int success = 0, failed = 0;
        var logLock = new object();

        try
        {
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            var geminiService = new GeminiExtractionService(authService, geminiApiKey, geminiModel, timeoutSec);

            // ── Read template once — reuse for all iterations ──
            StatusLabel.Text = "📋 קורא תבנית...";
            batchLog.AppendLine("📋 Reading template once for reuse...");
            LogBox.Text = batchLog.ToString();

            string cachedTemplateText;
            try
            {
                cachedTemplateText = await geminiService.ReadTemplateTextAsync(templateId);
                batchLog.AppendLine($"✅ Template cached: {cachedTemplateText.Length:N0} chars");
                batchLog.AppendLine($"🔀 Concurrency: {maxConcurrency} parallel requests");
                batchLog.AppendLine();
                LogBox.Text = batchLog.ToString();
            }
            catch (Exception ex)
            {
                batchLog.AppendLine($"❌ Failed to read template: {ex.Message}");
                LogBox.Text = batchLog.ToString();
                StatusLabel.Text = "❌ שגיאה בקריאת תבנית";
                AppLogger.Error(ex, "[Batch] Failed to read template");
                return;
            }

            // ── Parallel extraction with SemaphoreSlim throttle ──
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            int completed = 0;

            var tasks = pending.Select((item, i) => Task.Run(async () =>
            {
                var (project, link, versionIdx, reportId) = item;
                var projNum = project.Number?.ToString("0") ?? "?";
                var projName = project.NameAndNumber ?? project.Title ?? projNum;
                var versionDisplay = $"V{versionIdx + 1}/{link.ReportSpreadsheetIds.Count}";

                await semaphore.WaitAsync();
                try
                {
                    Dispatcher.Invoke(() =>
                        StatusLabel.Text = $"⚡ [{Interlocked.Increment(ref completed)}/{pending.Count}] {projName} — דוח {link.ReportNumber} {versionDisplay}");

                    lock (logLock)
                    {
                        batchLog.AppendLine($"── [{completed}/{pending.Count}] Project: {projName} | Report: {link.ReportNumber} | {versionDisplay} ──");
                    }
                    Dispatcher.Invoke(() => LogBox.Text = batchLog.ToString());

                    try
                    {
                        var result = await geminiService.ExtractWithAiAsync(
                            templateId, reportId, cachedTemplateText,
                            onProgress: msg => Dispatcher.Invoke(() =>
                            {
                                StatusLabel.Text = $"⚡ {projName} — {msg}";
                            }));

                        if (result.IsSuccess)
                        {
                            var savedPath = await ExtractionCacheService.SaveAsync(
                                result, projNum, versionIdx + 1, link.ReportNumber);

                            Interlocked.Increment(ref success);
                            lock (logLock)
                            {
                                batchLog.AppendLine($"  ✅ {result.Sections.Count} sections → {Path.GetFileName(savedPath)}");
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref failed);
                            lock (logLock)
                            {
                                batchLog.AppendLine($"  ❌ {result.ErrorMessage}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        lock (logLock)
                        {
                            batchLog.AppendLine($"  ❌ Exception: {ex.Message}");
                        }
                        AppLogger.Error(ex, $"[Batch] Failed: project={projNum}, report={link.ReportNumber}, version={versionIdx + 1}");
                    }

                    lock (logLock)
                    {
                        batchLog.AppendLine();
                    }
                    Dispatcher.Invoke(() => LogBox.Text = batchLog.ToString());
                }
                finally
                {
                    semaphore.Release();
                }
            })).ToArray();

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            batchLog.AppendLine($"❌ Batch aborted: {ex.Message}");
            AppLogger.Error(ex, "[Batch] Unhandled exception in BatchExtractButton_Click");
        }
        finally
        {
            ExtractButton.IsEnabled = true;
            AiExtractButton.IsEnabled = true;
            BatchExtractButton.IsEnabled = true;
        }

        batchLog.AppendLine("═══ Batch Complete ═══");
        batchLog.AppendLine($"⏱ End: {DateTime.Now:HH:mm:ss}");
        batchLog.AppendLine($"✅ Success: {success} | ❌ Failed: {failed} | ⏭ Skipped (cached): {skipped}");
        batchLog.AppendLine($"📂 Cache: {ExtractionCacheService.GetCacheRoot()}");
        LogBox.Text = batchLog.ToString();

        StatusLabel.Text = $"⚡ Batch הושלם — {success} ✅, {failed} ❌, {skipped} ⏭ cached";
        AppLogger.Info($"[Batch] Complete: success={success}, failed={failed}, skipped={skipped}");
    }

    /// <summary>Reads the Gemini API key from the credential vault (with fallback to appsettings.json).</summary>
    private static string? ReadGeminiApiKey() => AppConfiguration.GeminiApiKey;

    /// <summary>Reads a top-level setting from appsettings.json as a string (handles both string and numeric JSON values).</summary>
    private static string? ReadGeminiSetting(string key)
    {
        var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (!System.IO.File.Exists(configPath)) return null;

        var json = System.IO.File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : prop.GetRawText();
    }

    // ════════════════════════════════════════════════════════════════
    //  TAB 2: Task Generation — Step 1: Scan & Build Preview
    // ════════════════════════════════════════════════════════════════

    private async void ScanSheetButton_Click(object sender, RoutedEventArgs e)
    {
        var indexInput = IndexSheetIdBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(indexInput))
        {
            MessageBox.Show("יש להזין מזהה/קישור לגיליון האינדקס.", "שדות חסרים",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var spreadsheetId = ReportContentExtractor.ExtractSpreadsheetId(indexInput);

        ScanSheetButton.IsEnabled = false;
        GenerateTasksButton.IsEnabled = false;
        ConfigSummaryPanel.Visibility = Visibility.Collapsed;
        TaskStatusLabel.Text = "🔄 סורק גיליון אינדקס...";
        PreviewGrid.ItemsSource = null;
        _lastScanResult = null;
        _lastPreviewResult = null;

        try
        {
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            // ── Phase 1: Read index sheet ──
            var reader = new IndexSheetReader(authService);
            var scanResult = await reader.ReadAsync(spreadsheetId);

            if (!scanResult.IsSuccess)
            {
                TaskStatusLabel.Text = $"❌ {scanResult.ErrorMessage}";
                AppendToLog($"[Scan] ERROR: {scanResult.ErrorMessage}");
                return;
            }

            _lastScanResult = scanResult;

            AppendToLog($"[Scan] Rows: {scanResult.Rows.Count}");
            AppendToLog($"[Scan] Unique statuses: {string.Join(", ", scanResult.UniqueStatuses)}");
            foreach (var w in scanResult.Warnings)
                AppendToLog($"[Scan] {w}");

            // ── Phase 2: Resolve projects & build preview ──
            TaskStatusLabel.Text = "⚙ מזהה פרויקטים ובודק הגדרות...";

            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var taskService = new MigrationTaskService(contextFactory);
            var previewResult = await taskService.BuildPreviewAsync(
                scanResult.Rows, scanResult.UniqueStatuses);

            if (!previewResult.IsSuccess)
            {
                TaskStatusLabel.Text = $"❌ {previewResult.ErrorMessage}";
                AppendToLog($"[Preview] ERROR: {previewResult.ErrorMessage}");
                return;
            }

            _lastPreviewResult = previewResult;

            // ── Display preview table ──
            PreviewGrid.ItemsSource = previewResult.Rows;

            var migratable = previewResult.Rows.Count(r => r.CanMigrate);
            var unresolved = previewResult.Rows.Count(r => r.ResolvedProjectId == null && !r.IsApproved);
            var approved = previewResult.Rows.Count(r => r.IsApproved);

            // ── Config summary ──
            var summary = $"✅ TaskType: '{previewResult.TaskTypeName}' (Id={previewResult.TaskTypeId}" +
                $"{(previewResult.TaskTypeCreated ? ", חדש" : "")}) | " +
                $"סטטוסים חדשים: {previewResult.NewStatusesCreated} | " +
                $"שורות: {previewResult.Rows.Count} סה\"כ, {migratable} להעברה, {approved} מאושרות, {unresolved} ❌ לא נמצאו";

            ConfigSummaryText.Text = summary;
            ConfigSummaryPanel.Visibility = Visibility.Visible;

            foreach (var w in previewResult.Warnings)
                AppendToLog($"[Preview] {w}");

            if (unresolved > 0)
            {
                TaskStatusLabel.Text = $"⚠ {unresolved} פרויקטים לא נמצאו — בדקו את הטבלה ובחרו שורות לאישור.";
            }
            else
            {
                TaskStatusLabel.Text = $"✅ סריקה הושלמה — {migratable} משימות מוכנות. עברו על הטבלה ולחצו 'אשר וצור'.";
            }

            GenerateTasksButton.IsEnabled = migratable > 0;
        }
        catch (Exception ex)
        {
            TaskStatusLabel.Text = $"❌ שגיאה: {ex.Message}";
            AppendToLog($"[Scan] EXCEPTION: {ex}");
            System.Diagnostics.Debug.WriteLine($"[MigrationPoC] Scan ERROR: {ex}");
        }
        finally
        {
            ScanSheetButton.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  TAB 2: Task Generation — Step 2: Approve & Create Tasks
    // ════════════════════════════════════════════════════════════════

    private async void GenerateTasksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastScanResult == null || _lastPreviewResult == null)
        {
            MessageBox.Show("יש לבצע סריקת גיליון קודם.", "חסרה סריקה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedRows = _lastPreviewResult.Rows.Where(r => r.IsSelected).ToList();
        if (selectedRows.Count == 0)
        {
            MessageBox.Show("לא נבחרו שורות ליצירת משימות.\nסמנו שורות בעמודת 'בחר' בטבלה.",
                "אין שורות", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"ייווצרו {selectedRows.Count} משימות חדשות מתוך {_lastPreviewResult.Rows.Count} שורות.\n\nלהמשיך?",
            "אישור יצירת משימות",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        GenerateTasksButton.IsEnabled = false;
        ScanSheetButton.IsEnabled = false;
        TaskStatusLabel.Text = "⚡ יוצר משימות...";

        try
        {
            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var taskService = new MigrationTaskService(contextFactory);

            var result = await taskService.CommitTasksAsync(
                selectedRows,
                _lastScanResult.Rows,
                _lastPreviewResult.TaskTypeId,
                _lastPreviewResult.StatusNameToId);

            if (result.IsSuccess)
            {
                var parts = new List<string>();
                parts.Add($"נוצרו: {result.TasksCreated}");
                if (result.TasksDuplicate > 0)
                    parts.Add($"כפילויות: {result.TasksDuplicate}");
                if (result.TasksSkipped > 0)
                    parts.Add($"דולגו: {result.TasksSkipped}");
                if (result.TasksFailed > 0)
                    parts.Add($"נכשלו: {result.TasksFailed}");

                var icon = result.TasksFailed > 0 ? "⚠" : "✅";
                TaskStatusLabel.Text = $"{icon} הושלם — {string.Join(", ", parts)}. בדקו לוג מערכת לפירוט.";
                AppendToLog($"[Tasks] Process complete. Created={result.TasksCreated}, " +
                    $"Duplicates={result.TasksDuplicate}, Skipped={result.TasksSkipped}, Failed={result.TasksFailed}. " +
                    $"See System Log for details.");
            }
            else
            {
                TaskStatusLabel.Text = $"❌ {result.ErrorMessage}";
                AppendToLog($"[Tasks] FAILED: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            TaskStatusLabel.Text = $"❌ שגיאה: {ex.Message}";
            AppendToLog($"[Tasks] EXCEPTION: {ex}");
            System.Diagnostics.Debug.WriteLine($"[MigrationPoC] Task generation ERROR: {ex}");
        }
        finally
        {
            GenerateTasksButton.IsEnabled = false;
            ScanSheetButton.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Shared Helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a <see cref="GoogleAuthService"/> using centralized configuration.
    /// Reads Google client secrets from the credential vault (with fallback to file).
    /// Shows error dialogs and returns null on failure.
    /// </summary>
    private GoogleAuthService? CreateGoogleAuthService()
    {
        if (string.IsNullOrWhiteSpace(AppConfiguration.GetGoogleClientSecretsPath()))
        {
            MessageBox.Show("Google OAuth credentials לא מוגדרים.\nהגדירו אותם דרך חלון הגדרת מפתחות.",
                "Config Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        return App.ServiceProvider.GetRequiredService<GoogleAuthService>();
    }

    /// <summary>
    /// Saves an AI extraction result to the JSON cache on disk.
    /// Resolves project number and version index from the cached index sheet data.
    /// </summary>
    private async Task SaveExtractionToCacheAsync(
        ReportExtractionResult result,
        string reportSpreadsheetId,
        System.Text.StringBuilder liveLog)
    {
        try
        {
            var projectNumber = _selectedProject?.Number?.ToString("0") ?? "";
            if (string.IsNullOrWhiteSpace(projectNumber))
            {
                liveLog.AppendLine("⚠ [Cache] No project number — skipping JSON save.");
                return;
            }

            // Resolve version index and report number from the index sheet cache
            var (versionIndex, reportNumber) = ResolveVersionInfo(reportSpreadsheetId);

            var savedPath = await ExtractionCacheService.SaveAsync(
                result, projectNumber, versionIndex, reportNumber);

            liveLog.AppendLine($"💾 [Cache] Saved → {savedPath}");
            liveLog.AppendLine();
        }
        catch (Exception ex)
        {
            liveLog.AppendLine($"⚠ [Cache] Save failed: {ex.Message}");
            liveLog.AppendLine();
            AppLogger.Error(ex, "[ExtractionCache] Failed to save JSON cache");
        }
    }

    /// <summary>
    /// Determines the 1-based version index and report number for a given report spreadsheet ID
    /// by searching the cached index sheet links.
    /// Falls back to version 1 and report "0" if not found.
    /// </summary>
    private (int VersionIndex, string ReportNumber) ResolveVersionInfo(string reportSpreadsheetId)
    {
        if (_selectedProject == null || _indexSheetLinks.Count == 0)
            return (1, "0");

        var matching = FindMatchingIndexRows(_selectedProject);
        foreach (var link in matching)
        {
            var idx = link.ReportSpreadsheetIds.IndexOf(reportSpreadsheetId);
            if (idx >= 0)
                return (idx + 1, link.ReportNumber);
        }

        // Fallback: not found in index — use version 1
        return (1, matching.LastOrDefault()?.ReportNumber ?? "0");
    }

    /// <summary>Appends a line to the shared log box.</summary>
    private void AppendToLog(string line)
    {
        LogBox.Text += line + "\n";
    }

    // ════════════════════════════════════════════════════════════════
    //  TAB 3: New Google Sheet Review Migration Preview (Phase 1)
    // ════════════════════════════════════════════════════════════════

    private async void ScanReviewersButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var indexSheetId = NewIndexSheetIdBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(indexSheetId))
            {
                indexSheetId = GetIndexSheetId();
            }

            if (string.IsNullOrWhiteSpace(indexSheetId))
            {
                MessageBox.Show("נא להזין מזהה גיליון אינדקס או להגדיר בהגדרות המערכת.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ScanReviewersButton.IsEnabled = false;
            NewPreviewStatusLabel.Text = "🔄 סורק בודקים מהגיליון...";

            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var workflowQueryService = App.ServiceProvider.GetRequiredService<WorkflowQueryService>();
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            var previewService = new GoogleSheetReviewMigrationPreviewService(contextFactory, workflowQueryService, authService);

            var reviewers = await previewService.GetDistinctReviewersAsync(indexSheetId, msg => AppendToLog($"[Preview] {msg}"));

            _reviewerMappings.Clear();
            foreach (var r in reviewers)
            {
                // Simple auto-match heuristic by name exact match
                var autoMatch = _systemUsers.FirstOrDefault(u => u.DisplayName.Equals(r, StringComparison.OrdinalIgnoreCase));
                
                _reviewerMappings.Add(new ReviewerMappingItem
                {
                    SheetReviewerName = r,
                    AvailableUsers = _systemUsers,
                    SelectedUserId = autoMatch?.UserId,
                    SelectedUserDisplayName = autoMatch?.DisplayName,
                    MappingStatus = autoMatch != null ? "AutoMatched" : "NotMapped",
                    WarningMessage = autoMatch == null ? "ManualRequired" : ""
                });
            }

            ReviewerMappingGrid.ItemsSource = null;
            ReviewerMappingGrid.ItemsSource = _reviewerMappings;

            NewPreviewStatusLabel.Text = $"✅ נמצאו {_reviewerMappings.Count} בודקים ייחודיים. נא למפות למשתמשי מערכת.";
            BuildPreviewButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            NewPreviewStatusLabel.Text = "⚠ שגיאה בסריקת בודקים";
            AppendToLog($"[Preview] Error scanning reviewers: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanReviewersButton.IsEnabled = true;
        }
    }

    private async void BuildPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var indexSheetId = NewIndexSheetIdBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(indexSheetId))
            {
                indexSheetId = GetIndexSheetId();
            }

            BuildPreviewButton.IsEnabled = false;
            NewPreviewStatusLabel.Text = "🔄 מנתח ויוצר Preview קריאה בלבד...";

            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var workflowQueryService = App.ServiceProvider.GetRequiredService<WorkflowQueryService>();
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            var mappingDict = _reviewerMappings
                .Where(m => m.SelectedUserId.HasValue)
                .ToDictionary(m => m.SheetReviewerName, m => m.SelectedUserId!.Value);

            var previewService = new GoogleSheetReviewMigrationPreviewService(contextFactory, workflowQueryService, authService);

            var rows = await previewService.BuildPreviewAsync(indexSheetId, mappingDict, msg => AppendToLog($"[Preview] {msg}"));

            NewPreviewGrid.ItemsSource = null;
            NewPreviewGrid.ItemsSource = rows;

            var readyCount = rows.Count(r => r.IsCommitAllowed);
            NewPreviewStatusLabel.Text = $"✅ הניתוח הושלם. {readyCount} שורות מוכנות (אך Commit לא ממומש בשלב 1).";
        }
        catch (Exception ex)
        {
            NewPreviewStatusLabel.Text = "⚠ שגיאה ביצירת Preview";
            AppendToLog($"[Preview] Error building preview: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BuildPreviewButton.IsEnabled = true;
        }
    }

    /// <summary>Minimal config class for GoogleReports section deserialization.</summary>
    private sealed class MigrationGoogleConfig
    {
        public MigrationGoogleReportsSection GoogleReports { get; set; } = new();
    }

    private sealed class MigrationGoogleReportsSection
    {
        public string ClientSecretsPath { get; set; } = "credentials.json";
        public string TokenStorePath { get; set; } = "%APPDATA%\\SiNet\\GoogleTokens";
        public string ApplicationName { get; set; } = "SiNet Reports";
        public string IndexSheetId { get; set; } = "";
    }
}
