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
using SiNetProjectManagerV2.Services.Migration.Models;
using SiNetSQL.Services.Workflow;

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

    /// <summary>
    /// Template compatibility results from the last preview run.
    /// Keyed by "ProjectNumber|VersionIndex|ReportNumber" for double-click lookup.
    /// </summary>
    private IReadOnlyDictionary<string, TemplateCompatibilityResult>? _lastCompatibilityResults;

    /// <summary>Whether the last preview run had TemplateError (template provided but unreadable).</summary>
    private bool _lastPreviewHadTemplateError;

    // ── Phase 2 Import state ──
    /// <summary>Preview rows from the last BuildPreviewAsync run, stored for Phase 2 import.</summary>
    private List<GoogleSheetReviewMigrationPreviewRow>? _lastPreviewRows;

    /// <summary>Template sync rows from the last template scan, used by Phase 2 import.</summary>
    private IReadOnlyList<TemplateSyncRow>? _lastTemplateSyncRows;

    // ── Deterministic Extraction Preview state ──
    private ReportExtractionResult? _lastDeterministicResult;

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
                .Select(u => new SystemUserLookupItem { UserId = u.Id, DisplayName = u.Name ?? string.Empty })
                .OrderBy(u => u.DisplayName)
                .ToListAsync();

            _systemUsers = allUsers;

            if (_systemUsers.Count == 0)
                AppendToLog("[Users] ⚠ No active system users loaded — reviewer auto-matching will not work.");
            else
                AppendToLog($"[Users] Loaded {_systemUsers.Count} active system users.");
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

            // Also populate the target template dropdown in Tab 3 (same list)
            TargetTemplateComboBox.ItemsSource = _availableTemplates;

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

        // ── Cache reuse check ──────────────────────────────────────────────
        // Before sending to AI, check if a valid JSON already exists for this
        // report+template combination. If found, show the cached result instead.
        var cachedEnvelope = await ExtractionCacheService.FindBySpreadsheetIdsAsync(reportId, templateId);
        if (cachedEnvelope != null)
        {
            AppendToLog($"[Cache] ✅ Valid cache found for report={reportId}");
            AppendToLog($"[Cache]    Project: {cachedEnvelope.ProjectNumber}, Report: {cachedEnvelope.ReportNumber}, Version: {cachedEnvelope.VersionIndex}");
            AppendToLog($"[Cache]    Extracted: {cachedEnvelope.ExtractedAtUtc:yyyy-MM-dd HH:mm} UTC, Sections: {cachedEnvelope.SectionCount}");
            AppendToLog($"[Cache] ⏭ Skipping AI extraction — reusing cached result.");

            ResultsGrid.ItemsSource = cachedEnvelope.Sections;
            StatusLabel.Text = $"✅ [מטמון] {cachedEnvelope.SectionCount} סעיפים (פרויקט {cachedEnvelope.ProjectNumber}, דוח {cachedEnvelope.ReportNumber} גרסה {cachedEnvelope.VersionIndex})";

            var cacheMsg = $"נמצא מטמון JSON תקין עבור הדוח הזה:\n" +
                           $"  פרויקט: {cachedEnvelope.ProjectNumber}\n" +
                           $"  דוח: {cachedEnvelope.ReportNumber}, גרסה: {cachedEnvelope.VersionIndex}\n" +
                           $"  תאריך חילוץ: {cachedEnvelope.ExtractedAtUtc:yyyy-MM-dd HH:mm} UTC\n" +
                           $"  סעיפים: {cachedEnvelope.SectionCount}\n\n" +
                           $"לא נשלח ל-AI. להפעיל AI מחדש בכל זאת?";
            var rerun = MessageBox.Show(cacheMsg, "מטמון קיים — AI לא נדרש", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (rerun != MessageBoxResult.Yes) return;

            AppendToLog($"[Cache] User chose to re-run AI extraction despite valid cache.");
        }
        else
        {
            AppendToLog($"[Cache] No valid cache for report={reportId} + template={templateId}. Sending to AI.");
        }
        // ──────────────────────────────────────────────────────────────────

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

    // ════════════════════════════════════════════════════
    //  TAB 3: Deterministic Extraction Preview (read-only, no DB, no AI)
    //  SUSPENDED — not part of the main migration preview flow.
    //  Candidate for future cleanup after testing.
    //  The double-click preview feature uses NewPreviewGrid in the
    //  "Migration Preview" tab, not DeterministicResultsGrid here.
    // ════════════════════════════════════════════════════

    private async void DeterministicExtractButton_Click(object sender, RoutedEventArgs e)
    {
        var templateInput = DetTemplateIdBox.Text.Trim();
        var reportInput   = DetReportIdBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(templateInput) || string.IsNullOrWhiteSpace(reportInput))
        {
            MessageBox.Show("יש להזין מזהה/קישור גם לתבנית וגם לדוח הסופי.",
                "שדות חסרים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var templateId = ReportContentExtractor.ExtractSpreadsheetId(templateInput);
        var reportId   = ReportContentExtractor.ExtractSpreadsheetId(reportInput);

        DeterministicExtractButton.IsEnabled = false;
        SaveDeterministicCacheButton.IsEnabled = false;
        _lastDeterministicResult = null;
        DeterministicResultsGrid.ItemsSource = null;
        DetSummaryLabel.Text = "";
        DetStatusLabel.Text = "🔄 מתחבר ל-Google Sheets...";

        try
        {
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            DetStatusLabel.Text = "🔍 שלב 1: סורק תבנית...";

            var extractor = new ReportContentExtractor(authService);
            var result = await extractor.ExtractAsync(templateId, reportId);

            if (result.IsSuccess)
            {
                _lastDeterministicResult = result;
                DeterministicResultsGrid.ItemsSource = result.Sections;

                var splitCount    = result.Sections.Count(s => s.WasSplit);
                var resolvedCount = result.Sections.Count(s => s.IsResolved);
                var datedCount    = result.Sections.Count(s => s.ClosedDate != null);

                DetStatusLabel.Text = $"✅ חולצו {result.Sections.Count} שורות";

                var summary = new System.Text.StringBuilder();
                summary.Append($"סה\"כ: {result.Sections.Count} שורות");
                summary.Append($"  |  פוצלו: {splitCount}");
                summary.Append($"  |  נסגרו (אפור): {resolvedCount}");
                summary.Append($"  |  עם תאריך: {datedCount}");
                summary.Append($"  |  שדות כלליים: {result.GeneralFields.Count}");
                if (result.Warnings.Count > 0)
                    summary.Append($"  |  ⚠ {result.Warnings.Count} אזהרות");
                DetSummaryLabel.Text = summary.ToString();

                SaveDeterministicCacheButton.IsEnabled = true;

                AppendToLog($"[Det] חולץ דטרמיניסטי: {result.Sections.Count} שורות | " +
                            $"פוצלו={splitCount} נסגרו={resolvedCount} תאריכים={datedCount}");
                if (result.Warnings.Count > 0)
                {
                    AppendToLog("[Det] אזהרות:");
                    foreach (var w in result.Warnings)
                        AppendToLog($"  ⚠ {w}");
                }
            }
            else
            {
                DetStatusLabel.Text = $"❌ שגיאה: {result.ErrorMessage}";
                DetSummaryLabel.Text = "";
                AppendToLog($"[Det] שגיאה בחילוץ: {result.ErrorMessage}");
                foreach (var w in result.Warnings)
                    AppendToLog($"  ⚠ {w}");
            }
        }
        catch (Exception ex)
        {
            DetStatusLabel.Text = $"❌ שגיאה: {ex.Message}";
            DetSummaryLabel.Text = "";
            AppendToLog($"[Det] EXCEPTION: {ex.Message}");
            AppLogger.Error(ex, "[Det-UI] Unhandled exception in DeterministicExtractButton_Click");
        }
        finally
        {
            DeterministicExtractButton.IsEnabled = true;
        }
    }

    private async void SaveDeterministicCacheButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDeterministicResult == null)
        {
            MessageBox.Show("אין תוצאת חילוץ בזיכרון. יש לחלץ תחילה.",
                "אין נתונים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var projectNumber = DetProjectNumberBox.Text.Trim();
        var reportNumber  = DetReportNumberBox.Text.Trim();
        var versionText   = DetVersionIndexBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(projectNumber) ||
            string.IsNullOrWhiteSpace(reportNumber)  ||
            !int.TryParse(versionText, out var versionIndex) || versionIndex < 1)
        {
            MessageBox.Show("יש למלא מספר פרויקט, מספר דוח, ואינדקס גרסה (מספר שלם ≥ 1).",
                "שדות חסרים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Guard: warn if a file with this key already exists (no silent overwrite)
        var existing = await ExtractionCacheService.LoadAsync(projectNumber, versionIndex, reportNumber);
        if (existing != null)
        {
            var answer = MessageBox.Show(
                $"קובץ JSON עבור פרויקט {projectNumber}, דוח {reportNumber}, גרסה {versionIndex} כבר קיים.\n" +
                "הקובץ החדש יישמר עם סיומת מספרית (לא יידרס). להמשיך?",
                "קובץ קיים", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        SaveDeterministicCacheButton.IsEnabled = false;
        try
        {
            var savedPath = await ExtractionCacheService.SaveAsync(
                _lastDeterministicResult, projectNumber, versionIndex, reportNumber);

            DetSummaryLabel.Text = $"💾 נשמר: {savedPath}";
            AppendToLog($"[Det] JSON נשמר: {savedPath}");

            MessageBox.Show($"הקובץ נשמר בהצלחה:\n{savedPath}",
                "שמירה הושלמה", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DetSummaryLabel.Text = $"❌ שגיאת שמירה: {ex.Message}";
            AppendToLog($"[Det] שגיאה בשמירת JSON: {ex.Message}");
            AppLogger.Error(ex, "[Det-UI] Exception in SaveDeterministicCacheButton_Click");
        }
        finally
        {
            SaveDeterministicCacheButton.IsEnabled = _lastDeterministicResult != null;
        }
    }

    private async void DeterministicResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // The clicked row is only used to identify which report/version to show.
        // We open the FULL report preview — not just the selected section.

        // ── 1. Try _lastDeterministicResult (in-memory, highest priority) ──
        if (_lastDeterministicResult != null)
        {
            var projectNumber = DetProjectNumberBox.Text.Trim();
            var reportNumber  = DetReportNumberBox.Text.Trim();
            var versionText   = DetVersionIndexBox.Text.Trim();
            int.TryParse(versionText, out var versionIndex);

            var envelope = new ExtractionCacheEnvelope
            {
                ProjectNumber        = projectNumber,
                ReportNumber         = reportNumber,
                VersionIndex         = versionIndex,
                TemplateSpreadsheetId = _lastDeterministicResult.TemplateSpreadsheetId,
                ReportSpreadsheetId  = _lastDeterministicResult.ReportSpreadsheetId,
                ExtractedAtUtc       = DateTime.UtcNow,
                SectionCount         = _lastDeterministicResult.Sections.Count,
                Sections             = _lastDeterministicResult.Sections,
                GeneralFields        = _lastDeterministicResult.GeneralFields,
                Warnings             = _lastDeterministicResult.Warnings
            };

            var win = new Dialogs.FullReportFillPreviewWindow(envelope) { Owner = this };
            win.Show();
            return;
        }

        // ── 2. Try JSON cache if identity fields are filled ──
        var pn = DetProjectNumberBox.Text.Trim();
        var rn = DetReportNumberBox.Text.Trim();
        var vt = DetVersionIndexBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(pn) && !string.IsNullOrWhiteSpace(rn) && int.TryParse(vt, out var vi) && vi >= 1)
        {
            DetStatusLabel.Text = "🔄 טוען מטמון JSON...";
            try
            {
                var cached = await ExtractionCacheService.LoadAsync(pn, vi, rn);
                if (cached != null)
                {
                    DetStatusLabel.Text = $"✅ נטען מ-JSON cache";
                    var win = new Dialogs.FullReportFillPreviewWindow(cached) { Owner = this };
                    win.Show();
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[Det-UI] Exception loading JSON cache for full preview");
            }
            finally
            {
                // Restore status if it wasn't updated by a successful load
                if (DetStatusLabel.Text == "🔄 טוען מטמון JSON...")
                    DetStatusLabel.Text = "";
            }
        }

        // ── 3. Nothing found — show clear message ──
        MessageBox.Show(
            "לא ניתן לאתר את תוצאת החילוץ המלאה עבור הדוח/גרסה הנבחרים.\n\n" +
            "יש לחלץ תחילה (כפתור 'חלץ דטרמיניסטי') או לוודא שקיים JSON cache\n" +
            "ושדות מספר פרויקט / מספר דוח / אינדקס גרסה מלאים.",
            "לא נמצאה תוצאת חילוץ",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ════════════════════════════════════════════════════
    //  TAB 1: JSON Cache Management
    // ════════════════════════════════════════════════════

    private async void ExportJsonCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "ייצוא מטמון JSON",
            Filter = "ZIP Archive (*.zip)|*.zip",
            FileName = $"ExtractionCache_{DateTime.Now:yyyyMMdd_HHmm}.zip"
        };
        if (dlg.ShowDialog() != true) return;

        var targetPath = dlg.FileName;
        if (File.Exists(targetPath))
        {
            MessageBox.Show($"הקובץ כבר קיים: {targetPath}\nמחק אותו קודם או בחר שם אחר.", "קובץ קיים", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExportJsonCacheButton.IsEnabled = false;
        StatusLabel.Text = "📦 מייצא מטמון...";
        AppendToLog($"[Cache] Export started → {targetPath}");
        AppendToLog($"[Cache] Cache root: {ExtractionCacheService.GetCacheRoot()}");

        try
        {
            int count = await ExtractionCacheService.ExportToZipAsync(targetPath);
            StatusLabel.Text = $"✅ יוצאו {count} קבצים.";
            AppendToLog($"[Cache] ✅ Export complete: {count} files → {targetPath}");
            MessageBox.Show($"יוצאו {count} קבצי JSON ל:\n{targetPath}", "ייצוא הושלם", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "⚠ שגיאה בייצוא";
            AppendToLog($"[Cache] ❌ Export error: {ex.Message}");
            MessageBox.Show(ex.Message, "שגיאה בייצוא", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportJsonCacheButton.IsEnabled = true;
        }
    }

    private async void ImportJsonCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "ייבוא מטמון JSON",
            Filter = "ZIP Archive (*.zip)|*.zip"
        };
        if (dlg.ShowDialog() != true) return;

        var sourcePath = dlg.FileName;
        var confirm = MessageBox.Show(
            $"ייבוא מטמון JSON מ:\n{sourcePath}\n\nקבצים קיימים לא יוחלפו (יידלגו).\nהמשך?",
            "אישור ייבוא", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        ImportJsonCacheButton.IsEnabled = false;
        StatusLabel.Text = "📥 מייבא מטמון...";
        AppendToLog($"[Cache] Import started ← {sourcePath}");

        try
        {
            var result = await ExtractionCacheService.ImportFromZipAsync(sourcePath);
            StatusLabel.Text = $"✅ יובאו {result.Imported}, דולגו {result.Skipped}.";
            AppendToLog($"[Cache] ✅ Import complete: {result.Imported} imported, {result.Skipped} skipped (already exist), {result.Invalid} invalid.");
            if (result.Skipped > 0)
                AppendToLog($"[Cache] Skipped: {string.Join(", ", result.SkippedPaths.Take(10))}{(result.SkippedPaths.Count > 10 ? " ..." : "")}");

            MessageBox.Show(
                $"ייבוא הושלם:\n  יובאו: {result.Imported}\n  דולגו (קיימים): {result.Skipped}\n  לא תקינים: {result.Invalid}",
                "ייבוא הושלם", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "⚠ שגיאה בייבוא";
            AppendToLog($"[Cache] ❌ Import error: {ex.Message}");
            MessageBox.Show(ex.Message, "שגיאה בייבוא", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportJsonCacheButton.IsEnabled = true;
        }
    }

    private async void ValidateJsonCacheButton_Click(object sender, RoutedEventArgs e)
    {
        ValidateJsonCacheButton.IsEnabled = false;
        StatusLabel.Text = "🔍 בודק מטמון...";
        AppendToLog($"[Cache] Validate started. Root: {ExtractionCacheService.GetCacheRoot()}");

        try
        {
            var (total, valid, invalid, invalidPaths) = await ExtractionCacheService.ValidateCacheAsync();
            StatusLabel.Text = $"🔍 {total} קבצים: {valid} תקינים, {invalid} פגומים.";
            AppendToLog($"[Cache] Validate: {total} total, {valid} valid, {invalid} invalid.");
            if (invalid > 0)
            {
                AppendToLog("[Cache] Invalid files:");
                foreach (var p in invalidPaths.Take(20))
                    AppendToLog($"  ❌ {p}");
                if (invalidPaths.Count > 20)
                    AppendToLog($"  ... and {invalidPaths.Count - 20} more.");
            }
            MessageBox.Show(
                $"אימות מטמון הושלם:\n  סך הכל: {total}\n  תקינים: {valid}\n  פגומים: {invalid}",
                "אימות מטמון", MessageBoxButton.OK,
                invalid > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "⚠ שגיאה באימות";
            AppendToLog($"[Cache] ❌ Validate error: {ex.Message}");
            MessageBox.Show(ex.Message, "שגיאה באימות", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ValidateJsonCacheButton.IsEnabled = true;
        }
    }

    private void OpenCacheFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var cacheRoot = ExtractionCacheService.GetCacheRoot();
        AppendToLog($"[Cache] Cache root: {cacheRoot}");
        if (!Directory.Exists(cacheRoot))
        {
            MessageBox.Show($"תיקיית המטמון עדיין לא קיימת:\n{cacheRoot}",
                "תיקייה לא קיימת", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        System.Diagnostics.Process.Start("explorer.exe", cacheRoot);
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
            BuildPreviewButton.IsEnabled = false;
            _reviewerMappings.Clear();
            ReviewerMappingGrid.ItemsSource = null;
            NewPreviewStatusLabel.Text = "🔄 סורק בודקים מהגיליון...";

            AppendToLog($"[Preview] ── סרוק בודקים ──");
            AppendToLog($"[Preview] Sheet ID: {indexSheetId}");
            AppendToLog($"[Preview] System users loaded: {_systemUsers.Count}");

            if (_systemUsers.Count == 0)
                AppendToLog("[Preview] ⚠ אין משתמשי מערכת טעונים — auto-match לא יפעל. נסה לפתוח את החלון מחדש.");

            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var workflowQueryService = App.ServiceProvider.GetRequiredService<WorkflowQueryService>();
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            var previewService = new GoogleSheetReviewMigrationPreviewService(contextFactory, workflowQueryService, authService);

            var reviewers = await previewService.GetDistinctReviewersAsync(indexSheetId!, msg => AppendToLog($"[Preview] {msg}"));

            if (reviewers.Count == 0)
            {
                NewPreviewStatusLabel.Text = "⚠ לא נמצאו בודקים בגיליון. בדוק את ה-LogBox לפרטים.";
                AppendToLog("[Preview] ⚠ אפס בודקים נמצאו — ה-Preview לא יכול להמשיך ללא מיפוי. בדוק שעמודת הבודק קיימת בגיליון.");
                BuildPreviewButton.IsEnabled = false;
                return;
            }

            int autoMatchCount = 0;
            foreach (var r in reviewers)
            {
                var autoMatch = _systemUsers.FirstOrDefault(u => u.DisplayName.Equals(r, StringComparison.OrdinalIgnoreCase));
                if (autoMatch == null)
                {
                    // Partial-word match fallback: find a user whose display name contains the sheet name or vice versa
                    autoMatch = _systemUsers.FirstOrDefault(u =>
                        u.DisplayName.Contains(r, StringComparison.OrdinalIgnoreCase) ||
                        r.Contains(u.DisplayName, StringComparison.OrdinalIgnoreCase));
                }

                if (autoMatch != null) autoMatchCount++;

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

            ReviewerMappingGrid.ItemsSource = _reviewerMappings;

            AppendToLog($"[Preview] מיפוי: {autoMatchCount}/{reviewers.Count} בודקים זוהו אוטומטית.");
            NewPreviewStatusLabel.Text = $"✅ נמצאו {_reviewerMappings.Count} בודקים ({autoMatchCount} זוהו אוטומטית). נא למפות את הנותרים ולחץ Build Preview.";
            BuildPreviewButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            NewPreviewStatusLabel.Text = "⚠ שגיאה בסריקת בודקים";
            AppendToLog($"[Preview] Error scanning reviewers: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            BuildPreviewButton.IsEnabled = false;
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
            _lastCompatibilityResults = null;
            _lastPreviewHadTemplateError = false;

            var contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var workflowQueryService = App.ServiceProvider.GetRequiredService<WorkflowQueryService>();
            var authService = CreateGoogleAuthService();
            if (authService == null) return;

            // ── Load target template sections (if selected) ──
            IReadOnlyList<TemplateSyncRow>? targetTemplateSections = null;
            IReadOnlySet<string> templateGeneralFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedTargetTemplate = TargetTemplateComboBox.SelectedItem as InspectionTemplateItem;
            if (selectedTargetTemplate != null)
            {
                var templateId = selectedTargetTemplate.SpreadsheetId;

                try
                {
                    NewPreviewStatusLabel.Text = "🔄 טוען תבנית יעד...";
                    TargetTemplateStatusLabel.Text = "🔄 טוען...";
                    var templateProvider = new GoogleInspectionTemplateProvider(authService);
                    var scanResult = await templateProvider.ScanAndParseTemplateAsync(templateId);
                    targetTemplateSections = scanResult.SyncRows;

                    // Capture the general-field labels from the template scan so the full
                    // report preview can filter GeneralFields to only those that exist in
                    // the selected target template.
                    templateGeneralFieldKeys = scanResult.AllTags
                        .Where(t => t.IsGeneralTag && !string.IsNullOrWhiteSpace(t.GeneralTagLabel))
                        .Select(t => t.GeneralTagLabel!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    AppendToLog($"[Template] General field keys found in template: {templateGeneralFieldKeys.Count}");

                    if (targetTemplateSections.Count == 0)
                    {
                        TargetTemplateStatusLabel.Text = "⚠ תבנית ריקה";
                        TargetTemplateStatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E65100"));
                        AppendToLog($"[Template] Target template '{selectedTargetTemplate.Name}' loaded but contains 0 sections: {templateId}");
                        _lastPreviewHadTemplateError = true;
                    }
                    else
                    {
                        TargetTemplateStatusLabel.Text = $"✅ {targetTemplateSections.Count} סעיפים";
                        TargetTemplateStatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E7D32"));
                        AppendToLog($"[Template] Target template '{selectedTargetTemplate.Name}' loaded: {targetTemplateSections.Count} sections from {templateId}");
                    }
                }
                catch (Exception templateEx)
                {
                    TargetTemplateStatusLabel.Text = "❌ שגיאה בטעינת תבנית";
                    TargetTemplateStatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D32F2F"));
                    AppendToLog($"[Template] Error loading target template '{selectedTargetTemplate.Name}': {templateEx.Message}");
                    _lastPreviewHadTemplateError = true;
                }
            }
            else
            {
                TargetTemplateStatusLabel.Text = "";
            }

            var mappingDict = _reviewerMappings
                .Where(m => m.SelectedUserId.HasValue)
                .ToDictionary(m => m.SheetReviewerName, m => m.SelectedUserId!.Value);

            var previewService = new GoogleSheetReviewMigrationPreviewService(contextFactory, workflowQueryService, authService);

            NewPreviewStatusLabel.Text = "🔄 מנתח ויוצר Preview...";
            var rows = await previewService.BuildPreviewAsync(
                indexSheetId!, mappingDict, targetTemplateSections, msg => AppendToLog($"[Preview] {msg}"));

            // If template was provided but loading failed, mark all rows as TemplateError
            if (_lastPreviewHadTemplateError && selectedTargetTemplate != null)
            {
                foreach (var row in rows)
                {
                    row.TemplateValidationStatus = "TemplateError";
                    row.TemplateWarnings = "Target template could not be loaded — template validation failed.";
                }
            }

            // Store compatibility results for double-click access.
            // Enrich each result with the template general-field keys so the full report
            // preview can show only GeneralFields that exist in the selected target template.
            if (templateGeneralFieldKeys.Count > 0)
            {
                _lastCompatibilityResults = previewService.CompatibilityResults
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => new TemplateCompatibilityResult
                        {
                            Entries = kvp.Value.Entries,
                            ImportEligibleGeneralFieldKeys = templateGeneralFieldKeys
                        });
            }
            else
            {
                _lastCompatibilityResults = previewService.CompatibilityResults;
            }

            NewPreviewGrid.ItemsSource = null;
            NewPreviewGrid.ItemsSource = rows;

            // Store for Phase 2 import.
            _lastPreviewRows = rows;
            _lastTemplateSyncRows = targetTemplateSections;

            var readyCount = rows.Count(r => r.IsCommitAllowed);
            var templateStatus = targetTemplateSections != null
                ? $" | תבנית: {rows.Count(r => r.TemplateValidationStatus == "FullMatch")} תואמים, {rows.Count(r => r.TemplateValidationStatus == "PartialMatch")} חלקי, {rows.Count(r => r.TemplateValidationStatus == "NoMatch")} לא תואם"
                : "";
            NewPreviewStatusLabel.Text = $"✅ הניתוח הושלם. {readyCount} שורות מוכנות.{templateStatus}";
            UpdateImportButtonState();
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

    // ═══════════════════════════════════════════════════════════════════
    //  TAB 3: New Preview Grid — double-click to show filled report data
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Double-click on NewPreviewGrid opens the full report fill preview for the selected
    /// report/version row.  The clicked row is used only to identify the report identity
    /// (ResolvedProjectNumber + ReportNumber + VersionIndex).  No extraction is run;
    /// the window is populated exclusively from the existing JSON cache if available.
    /// No DB write, no workflow creation, no AI, no automatic extraction.
    /// </summary>
    private async void NewPreviewGrid_MouseDoubleClick(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (NewPreviewGrid.SelectedItem is not
            SiNetProjectManagerV2.Services.Migration.Models.GoogleSheetReviewMigrationPreviewRow row)
            return;

        // Determine the project number key used for cache lookup.
        // Prefer the resolved project number; fall back to the sheet value if not resolved.
        var projectNumber = !string.IsNullOrWhiteSpace(row.ResolvedProjectNumber)
            ? row.ResolvedProjectNumber
            : row.ProjectNumberFromSheet.Trim();

        var reportNumber  = row.ReportNumber;
        var versionIndex  = row.VersionIndex;

        if (string.IsNullOrWhiteSpace(projectNumber) ||
            string.IsNullOrWhiteSpace(reportNumber)  ||
            versionIndex < 1)
        {
            MessageBox.Show(
                "לא ניתן לקבוע את זהות הדוח/גרסה מהשורה הנבחרת.\n"
                + "אנא בדוק שהפרויקט זוהה במערכת ושקיים JSON cache.",
                "מידע חסר",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NewPreviewStatusLabel.Text = "🔄 טוען JSON cache...";
        try
        {
            var envelope = await ExtractionCacheService.LoadAsync(
                projectNumber, versionIndex, reportNumber);

            if (envelope == null)
            {
                NewPreviewStatusLabel.Text = "";
                MessageBox.Show(
                    $"לא נמצא JSON cache עבור פרויקט '{projectNumber}' דוח {reportNumber} גרסה {versionIndex}.\n\n"
                    + "תוכן דוח מלא יוצג רק לאחר שייבוא/חילוץ JSON cache לאותה גרסה.",
                    "אין נתונים ממולאים",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Filter: show only sections that have actual data
            var filledSections = envelope.Sections
                .Where(s => !string.IsNullOrWhiteSpace(s.NoteText)
                         || !string.IsNullOrWhiteSpace(s.StatusKey)
                         || !string.IsNullOrWhiteSpace(s.DesignerResponse))
                .ToList();

            // Build a display envelope containing only filled sections
            var displayEnvelope = new ExtractionCacheEnvelope
            {
                ProjectNumber         = envelope.ProjectNumber,
                ReportNumber          = envelope.ReportNumber,
                VersionIndex          = envelope.VersionIndex,
                TemplateSpreadsheetId = envelope.TemplateSpreadsheetId,
                ReportSpreadsheetId   = envelope.ReportSpreadsheetId,
                ExtractedAtUtc        = envelope.ExtractedAtUtc,
                SectionCount          = filledSections.Count,
                Sections              = filledSections,
                GeneralFields         = envelope.GeneralFields,
                Warnings              = envelope.Warnings,
            };

            NewPreviewStatusLabel.Text =
                $"✅ נטען cache: {filledSections.Count} סעיפים עם נתונים";

            // Look up template compatibility result for this row
            TemplateCompatibilityResult? templateCompat = null;
            var compatKey = $"{projectNumber}|{versionIndex}|{reportNumber}";
            _lastCompatibilityResults?.TryGetValue(compatKey, out templateCompat);

            var cachePath = ExtractionCacheService.GetProjectCacheFolder(projectNumber);
            var win = new Dialogs.FullReportFillPreviewWindow(displayEnvelope, cachePath, templateCompat)
            {
                Owner = this
            };
            win.Show();
        }
        catch (Exception ex)
        {
            NewPreviewStatusLabel.Text = "";
            AppendToLog($"[Preview] Error loading JSON cache for preview: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TAB 3: Phase 2 Import — selected rows only
    // ═══════════════════════════════════════════════════════════════════

    private void NewPreviewGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateImportButtonState();
    }

    /// <summary>
    /// Enable the import button only when all Phase 2 prerequisites are met.
    /// </summary>
    private void UpdateImportButtonState()
    {
        if (ImportReportsSelectedButton is null) return;

        // Conditions:
        // 1. Preview rows loaded
        // 2. Target template selected
        // 3. Template compatibility results exist
        // 4. Template sync rows available
        bool baseConditions =
            _lastPreviewRows is { Count: > 0 } &&
            _selectedTemplate is not null &&
            _lastCompatibilityResults is not null &&
            _lastTemplateSyncRows is { Count: > 0 };

        if (!baseConditions)
        {
            ImportReportsSelectedButton.IsEnabled = false;
            ImportReportsAllButton.IsEnabled = false;
            ImportStatusLabel.Text = "";
            return;
        }

        // ── Import All: count all eligible rows ──
        var allEligible = _lastPreviewRows!
            .Where(IsRowEligibleForPhase2Import)
            .ToList();

        ImportReportsAllButton.IsEnabled = allEligible.Count > 0;

        // ── Import Selected: check selected rows ──
        var selectedRows = NewPreviewGrid.SelectedItems
            .OfType<GoogleSheetReviewMigrationPreviewRow>()
            .ToList();

        if (selectedRows.Count == 0)
        {
            ImportReportsSelectedButton.IsEnabled = false;
            ImportStatusLabel.Text = allEligible.Count > 0
                ? $"בחר שורות לייבוא ({allEligible.Count} כשירות סה״כ)"
                : "אין שורות כשירות לייבוא";
            return;
        }

        var selectedEligible = selectedRows
            .Where(IsRowEligibleForPhase2Import)
            .ToList();

        if (selectedEligible.Count == 0)
        {
            ImportReportsSelectedButton.IsEnabled = false;
            ImportStatusLabel.Text = $"{selectedRows.Count} שורות נבחרו — אף אחת אינה מתאימה לייבוא";
            return;
        }

        ImportReportsSelectedButton.IsEnabled = true;
        ImportStatusLabel.Text = selectedEligible.Count == selectedRows.Count
            ? $"{selectedEligible.Count} שורות מוכנות לייבוא"
            : $"{selectedEligible.Count}/{selectedRows.Count} שורות מתאימות לייבוא";
    }

    private async void ImportReportsSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPreviewRows is null || _selectedTemplate is null ||
            _lastCompatibilityResults is null || _lastTemplateSyncRows is null)
            return;

        // Get eligible selected rows using the central helper.
        var selectedRows = NewPreviewGrid.SelectedItems
            .OfType<GoogleSheetReviewMigrationPreviewRow>()
            .Where(IsRowEligibleForPhase2Import)
            .ToList();

        if (selectedRows.Count == 0)
        {
            MessageBox.Show("אין שורות מתאימות לייבוא.", "Phase 2", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Confirmation.
        var confirmMsg = $"האם לייבא {selectedRows.Count} דוח/ות ל-DB?\n\n" +
                         $"תבנית: {_selectedTemplate.Name}\n" +
                         $"פעולה זו תיצור InspectionSeries, InspectionReport, ו-InspectionNote ב-DB.\n\n" +
                         $"Phase 2 — שורות נבחרות בלבד. ללא Workflow, Tasks, או Google Sheets.";
        var confirm = MessageBox.Show(confirmMsg, "אישור ייבוא Phase 2",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        ImportReportsSelectedButton.IsEnabled = false;
        ImportStatusLabel.Text = "מייבא...";

        try
        {
            // Resolve services (same pattern as preview — no DI constructor injection).
            var dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var reportService = App.ServiceProvider.GetRequiredService<IInspectionReportService>();
            var templateSyncService = App.ServiceProvider.GetRequiredService<TemplateSyncService>();

            var importService = new ReportImportService(dbFactory, reportService, templateSyncService);

            void LogToUi(string msg)
            {
                Dispatcher.Invoke(() => AppendToLog(msg));
            }

            var result = await Task.Run(async () =>
                await importService.ImportRowsAsync(
                    selectedRows,
                    _lastCompatibilityResults!,
                    _lastTemplateSyncRows!,
                    _selectedTemplate!,
                    LogToUi,
                    CancellationToken.None));

            ImportStatusLabel.Text = $"✅ ייבוא הושלם: {result.ReportsCreated} נוצרו, " +
                                    $"{result.ReportsSkippedAlreadyExists} דילוג, " +
                                    $"{result.NotesImported} הערות, " +
                                    $"{result.Errors} שגיאות";

            AppendToLog(result.BuildSummary());

            if (result.Errors > 0)
            {
                MessageBox.Show(
                    $"הייבוא הסתיים עם {result.Errors} שגיאות.\n" +
                    $"נוצרו {result.ReportsCreated} דוחות, {result.NotesImported} הערות.\n\n" +
                    $"ראה לוג לפרטים.",
                    "Phase 2 — הסתיים עם שגיאות", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"ייבוא הושלם בהצלחה!\n\n" +
                    $"דוחות נוצרו: {result.ReportsCreated}\n" +
                    $"הערות יובאו: {result.NotesImported}\n" +
                    $"תגובות מתכנן: {result.PlannerResponsesImported}\n" +
                    $"הערות פערים: {result.GapNotesCreated}\n" +
                    $"שדות כלליים יובאו: {result.GeneralFieldsImported}\n" +
                    $"שדות כלליים שדולגו: {result.GeneralFieldsSkipped}\n" +
                    $"Placeholder defaults: {result.PlaceholderDefaultsFilled}\n" +
                    $"דילוג (קיים כבר): {result.ReportsSkippedAlreadyExists}\n" +
                    $"דילוג (קונפליקט): {result.ReportsSkippedConflict}",
                    "Phase 2 — הצלחה", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ImportStatusLabel.Text = "⚠ שגיאה בייבוא";
            AppendToLog($"[Phase2] Import error: {ex}");
            MessageBox.Show($"שגיאה בייבוא:\n{ex.Message}", "Phase 2 Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateImportButtonState();
        }
    }

    /// <summary>
    /// Import ALL eligible preview rows (not just selected).
    /// Uses the same ReportImportService.ImportRowsAsync pipeline.
    /// Approved for DB testing only — no Workflow, Tasks, or MarkReportAsSentAsync.
    /// </summary>
    private async void ImportReportsAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPreviewRows is null || _selectedTemplate is null ||
            _lastCompatibilityResults is null || _lastTemplateSyncRows is null)
            return;

        // Filter all eligible rows using the central helper.
        var allEligible = _lastPreviewRows
            .Where(IsRowEligibleForPhase2Import)
            .ToList();

        if (allEligible.Count == 0)
        {
            MessageBox.Show("אין שורות כשירות לייבוא.", "Phase 2", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Summary before confirmation.
        var totalRows = _lastPreviewRows.Count;
        var noProject = _lastPreviewRows.Count(r => !r.ResolvedProjectId.HasValue);
        var noJson = _lastPreviewRows.Count(r => r.ResolvedProjectId.HasValue && !IsJsonCacheAvailableForImport(r));
        var templateMismatch = _lastPreviewRows.Count(r =>
            r.ResolvedProjectId.HasValue && IsJsonCacheAvailableForImport(r) &&
            r.TemplateValidationStatus is not ("FullMatch" or "PartialMatch"));
        var blocked = totalRows - allEligible.Count - noProject - noJson - templateMismatch;
        if (blocked < 0) blocked = 0;

        var confirmMsg = $"פעולה זו תייבא {allEligible.Count} שורות כשירות ל-DB הנוכחי.\n" +
                         $"מיועד לבדיקות בלבד.\n\n" +
                         $"סה״כ Preview: {totalRows}\n" +
                         $"כשירות: {allEligible.Count}\n" +
                         $"ללא ProjectId: {noProject}\n" +
                         $"ללא JSON: {noJson}\n" +
                         $"Template mismatch: {templateMismatch}\n" +
                         $"חסומות: {blocked}\n\n" +
                         $"תבנית: {_selectedTemplate.Name}\n\n" +
                         $"להמשיך?";
        var confirm = MessageBox.Show(confirmMsg, "אישור ייבוא כל הדוחות — Phase 2",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        AppendToLog($"[Phase2 ImportAll] Starting import of {allEligible.Count}/{totalRows} eligible rows. " +
                    $"Rejected: noProject={noProject}, noJson={noJson}, templateMismatch={templateMismatch}, blocked={blocked}");

        ImportReportsSelectedButton.IsEnabled = false;
        ImportReportsAllButton.IsEnabled = false;
        ImportStatusLabel.Text = $"מייבא {allEligible.Count} שורות...";

        try
        {
            var dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            var reportService = App.ServiceProvider.GetRequiredService<IInspectionReportService>();
            var templateSyncService = App.ServiceProvider.GetRequiredService<TemplateSyncService>();

            var importService = new ReportImportService(dbFactory, reportService, templateSyncService);

            void LogToUi(string msg)
            {
                Dispatcher.Invoke(() => AppendToLog(msg));
            }

            var result = await Task.Run(async () =>
                await importService.ImportRowsAsync(
                    allEligible,
                    _lastCompatibilityResults!,
                    _lastTemplateSyncRows!,
                    _selectedTemplate!,
                    LogToUi,
                    CancellationToken.None));

            ImportStatusLabel.Text = $"✅ ImportAll: {result.ReportsCreated} נוצרו, " +
                                    $"{result.ReportsSkippedAlreadyExists} דילוג, " +
                                    $"{result.NotesImported} הערות, " +
                                    $"{result.Errors} שגיאות";

            AppendToLog(result.BuildSummary());

            if (result.Errors > 0)
            {
                MessageBox.Show(
                    $"הייבוא הסתיים עם {result.Errors} שגיאות.\n" +
                    $"נוצרו {result.ReportsCreated} דוחות, {result.NotesImported} הערות.\n\n" +
                    $"ראה לוג לפרטים.",
                    "Phase 2 ImportAll — הסתיים עם שגיאות", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"ייבוא הושלם בהצלחה!\n\n" +
                    $"דוחות נוצרו: {result.ReportsCreated}\n" +
                    $"הערות יובאו: {result.NotesImported}\n" +
                    $"תגובות מתכנן: {result.PlannerResponsesImported}\n" +
                    $"הערות פערים: {result.GapNotesCreated}\n" +
                    $"שדות כלליים יובאו: {result.GeneralFieldsImported}\n" +
                    $"שדות כלליים שדולגו: {result.GeneralFieldsSkipped}\n" +
                    $"Placeholder defaults: {result.PlaceholderDefaultsFilled}\n" +
                    $"דילוג (קיים כבר): {result.ReportsSkippedAlreadyExists}\n" +
                    $"דילוג (קונפליקט): {result.ReportsSkippedConflict}",
                    "Phase 2 ImportAll — הצלחה", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ImportStatusLabel.Text = "⚠ שגיאה בייבוא";
            AppendToLog($"[Phase2 ImportAll] Import error: {ex}");
            MessageBox.Show($"שגיאה בייבוא:\n{ex.Message}", "Phase 2 ImportAll Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateImportButtonState();
        }
    }

    /// <summary>
    /// Central eligibility check for Phase 2 import.
    /// Used by UpdateImportButtonState, ImportReportsSelectedButton_Click,
    /// and ImportReportsAllButton_Click to ensure identical filtering.
    /// </summary>
    private static bool IsRowEligibleForPhase2Import(GoogleSheetReviewMigrationPreviewRow row)
    {
        return row.ResolvedProjectId.HasValue &&
               row.TemplateValidationStatus is "FullMatch" or "PartialMatch" &&
               IsJsonCacheAvailableForImport(row) &&
               row.Classification is not (
                   MigrationPreviewClassification.AlreadyDone or
                   MigrationPreviewClassification.NoMatch or
                   MigrationPreviewClassification.MissingData or
                   MigrationPreviewClassification.JsonMissing or
                   MigrationPreviewClassification.ExistingReportConflict);
    }

    /// <summary>
    /// Check whether a preview row has a JSON cache available for Phase 2 import.
    /// Supports values like "Available", "Found", "Found (V1)", "Found (V2)", "✅".
    /// </summary>
    private static bool IsJsonCacheAvailableForImport(GoogleSheetReviewMigrationPreviewRow row)
    {
        var status = row.JsonCacheStatus;
        if (string.IsNullOrWhiteSpace(status)) return false;

        return status.Equals("Available", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Found", StringComparison.OrdinalIgnoreCase)
            || status == "✅";
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
