using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiNetProjectManagerV2.Services;
using SiNetProjectManagerV2.Services.Stamping;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiNetSQL.Services.AI;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// Interaction logic for FloatingInspectionView.xaml
/// Floating ToolWindow showing inspection reports for the currently active project.
/// Inherits shared behavior (collapse, drag, opacity, position persistence) from <see cref="FloatingWindowBase"/>.
/// Wires the Google-based template provider into the ViewModel.
/// </summary>
public partial class FloatingInspectionView : FloatingWindowBase
{
    /// <summary>Scale factor applied to the global <c>AppFontSize</c> for this compact floating window.</summary>
    private const double FontScaleFactor = 0.8;

    public FloatingInspectionView()
    {
        InitializeComponent();

        // Apply scaled font size (80% of global AppFontSize)
        ApplyScaledFontSize();

        var viewModel = App.ServiceProvider.GetRequiredService<FloatingInspectionViewModel>();
        DataContext = viewModel;

        // Wire the Google template provider and export service into the ViewModel
        WireGoogleServices(viewModel);

        // Wire the drawing stamp service
        viewModel.SetDrawingStampService(new DrawingStampService());

        // Wire the WPF reviewed-plan picker (Phase B)
        viewModel.SetReviewedPlanPicker(new SiNetProjectManagerV2.Services.Inspection.ReviewedPlanPicker());

        // Wire the WPF per-note linked-file picker
        viewModel.SetNoteLinkedFilePicker(new SiNetProjectManagerV2.Services.Inspection.NoteLinkedFilePicker());

        // Initialize common floating behavior (opacity, settings, collapse)
        InitializeFloatingBehavior();

        // Verify Ollama AI connectivity at startup
        _ = CheckOllamaConnectivityAsync();

#if DEBUG
        Loaded += FloatingInspectionView_DebugLogIdentity;
#endif
    }

#if DEBUG
    private void FloatingInspectionView_DebugLogIdentity(object sender, RoutedEventArgs e)
    {
        try
        {
            var vm = DataContext as FloatingInspectionViewModel;
            var reg = SiNetSQL.Services.ActiveFileQuery.ActiveFileQueryRegistry.Instance;
            System.Diagnostics.Debug.WriteLine(
                $"[FloatingInspectionView] Loaded " +
                $"Window#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this)} " +
                $"DataContextType={(DataContext?.GetType().FullName ?? "(null)")} " +
                $"VM#{(vm == null ? "(null)" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vm).ToString())} " +
                $"VM.IsActiveFileServiceAvailable={(vm?.IsActiveFileServiceAvailable.ToString() ?? "(null)")} " +
                $"VM.IsWorkWindowWarningVisible={(vm?.IsWorkWindowWarningVisible.ToString() ?? "(null)")} " +
                $"Registry.IsAvailable={reg.IsAvailable} " +
                $"Registry.CurrentProjectNumber={(reg.CurrentProjectNumber?.ToString() ?? "(null)")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FloatingInspectionView] Loaded log failed: {ex.Message}");
        }
    }
#endif

    /// <summary>
    /// One-time startup check: loads the saved model from DB and verifies the Ollama server is reachable.
    /// </summary>
    private async Task CheckOllamaConnectivityAsync()
    {
        var ollamaService = App.ServiceProvider.GetService<OllamaService>();
        if (ollamaService is null)
        {
            System.Diagnostics.Debug.WriteLine("[AI Startup] ? OllamaService not registered in DI.");
            return;
        }

        // Load model preference from DB (overrides appsettings.json default)
        try
        {
            var settingsService = App.ServiceProvider.GetRequiredService<SystemSettingsService>();
            var savedModel = await settingsService.GetOrDefaultAsync(
                SystemSettingKeys.OllamaModel, ollamaService.Model);

            if (!string.IsNullOrWhiteSpace(savedModel))
                ollamaService.Model = savedModel;

            System.Diagnostics.Debug.WriteLine($"[AI Startup] Active model: {ollamaService.Model}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AI Startup] ?? Could not load model from DB: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine("[AI Startup] Checking Ollama server connectivity...");
        try
        {
            var available = await ollamaService.IsAvailableAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine(available
                ? "[AI Startup] ? Ollama server is reachable and responding."
                : "[AI Startup] ?? Ollama server returned non-success status.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AI Startup] ? Ollama server unreachable: {ex.Message}");
        }
    }

    /// <summary>Gets the ViewModel for external access.</summary>
    public FloatingInspectionViewModel ViewModel => (FloatingInspectionViewModel)DataContext;

    #region FloatingWindowBase Overrides

    protected override IFloatingWindowViewModel FloatingViewModel => ViewModel;
    protected override FrameworkElement OpacityTarget => ContentBorder;
    protected override string LogPrefix => "[FloatingInspection]";

    protected override (double Top, double Left, double Width, double Height)
        ReadWindowPosition(AppSettings settings) =>
        (settings.FloatingInspectionTop, settings.FloatingInspectionLeft,
         settings.FloatingInspectionWidth, settings.FloatingInspectionHeight);

    protected override void WriteWindowPosition(
        AppSettings settings, double top, double left, double width, double height)
    {
        settings.FloatingInspectionTop = top;
        settings.FloatingInspectionLeft = left;
        settings.FloatingInspectionWidth = width;
        settings.FloatingInspectionHeight = height;
    }

    protected override void OnSettingsChanged(AppSettings settings, string? propertyName)
    {
        if (propertyName == nameof(AppSettings.FontSize))
            ApplyScaledFontSize();
    }

    #endregion

    /// <summary>
    /// Reads the global <c>AppFontSize</c> resource and applies 80% of it to this window.
    /// </summary>
    private void ApplyScaledFontSize()
    {
        if (Application.Current.TryFindResource("AppFontSize") is double baseFontSize)
        {
            FontSize = Math.Max(8, baseFontSize * FontScaleFactor);
        }
    }

    #region Domain-Specific Handlers

    /// <summary>
    /// Auto-saves dirty notes and removes empty sub-notes on lost focus.
    /// </summary>
    private void TreeNoteTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: NoteTreeItem note }) return;

        if (note.IsDirty)
            ViewModel.SaveNote(note);

        if (string.IsNullOrWhiteSpace(note.NoteText))
            ViewModel.DeleteEmptyNote(note);
    }

    /// <summary>
    /// Raised by <see cref="RichTextNoteEditor"/> when the user exits edit mode.
    /// Saves dirty notes, removes empty sub-notes, and triggers background AI review.
    /// </summary>
    private void NoteEditor_EditCompleted(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[AI Flow] NoteEditor_EditCompleted entered. sender={sender?.GetType().Name}");
        if (sender is not RichTextNoteEditor { DataContext: NoteTreeItem note }) return;

        System.Diagnostics.Debug.WriteLine($"[AI Flow] NoteEditor_EditCompleted — NoteText='{note.NoteText?.Substring(0, Math.Min(note.NoteText?.Length ?? 0, 50))}', IsDirty={note.IsDirty}");

        if (note.IsDirty)
            ViewModel.SaveNote(note);

        if (string.IsNullOrWhiteSpace(note.NoteText))
        {
            System.Diagnostics.Debug.WriteLine("[AI Flow] Note is empty — deleting, skipping AI.");
            ViewModel.DeleteEmptyNote(note);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[AI Flow] Calling RunAiReviewInBackground...");
            _ = RunAiReviewInBackground(note);
        }
    }

    /// <summary>
    /// Auto-saves the note immediately when the status ComboBox selection changes.
    /// </summary>
    private void TreeNoteStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: NoteTreeItem note }) return;
        if (note.IsDirty)
            ViewModel.SaveNote(note);
    }

    /// <summary>
    /// Saves a dirty general-data field when the TextBox loses focus.
    /// </summary>
    private void GeneralField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GeneralFieldTreeItem field }) return;
        if (field.IsDirty)
            ViewModel.SaveGeneralField(field);
    }

    private void AutoManualToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GeneralFieldTreeItem field }) return;
        if (field.IsDirty)
            ViewModel.SaveGeneralField(field);
    }

    /// <summary>
    /// Opens the comprehensive Help Center window for inspection templates.
    /// </summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new InspectionHelpWindow { Owner = this };
        helpWindow.Show();
    }

    /// <summary>
    /// Wraps the selected text in the note TextBox with a RichTextCodec markup tag.
    /// Strips existing markup from the selection before applying the new color.
    /// Tag codes: 1! = Red+Bold, 2 = Blue, 3 = Gray, 4 = Green, 0 = Black (strip).
    /// </summary>
    private void ColorText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string colorCode } item) return;

        var contextMenu = item.Parent as ContextMenu;
        if (contextMenu?.PlacementTarget is not TextBox textBox) return;

        var selectedText = textBox.SelectedText;
        if (string.IsNullOrEmpty(selectedText)) return;

        var start = textBox.SelectionStart;
        var length = textBox.SelectionLength;

        // Strip any existing markup from the selection first
        var cleanText = Regex.Replace(selectedText, @"\{(1!|2|3|4)\s(.*?)\}", "$2");

        var replacement = colorCode switch
        {
            "1!" => $"{{1! {cleanText}}}",
            "2" => $"{{2 {cleanText}}}",
            "3" => $"{{3 {cleanText}}}",
            "4" => $"{{4 {cleanText}}}",
            _ => cleanText
        };

        var text = textBox.Text ?? "";
        textBox.Text = text[..start] + replacement + text[(start + length)..];

        // Trigger binding update
        var binding = textBox.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();
    }

    #endregion

    /// <summary>
    /// Applies the selected AI suggestion from <see cref="RichTextNoteEditor"/>'s context menu.
    /// </summary>
    private void NoteEditor_AiReviewRequested(object sender, RichTextNoteEditor.AiReviewRequestedEventArgs e)
    {
        if (sender is not RichTextNoteEditor { DataContext: NoteTreeItem note }) return;

        note.NoteText = e.SuggestedText;
        ViewModel.SaveNote(note);

        var displayType = e.ReviewType == "grammar" ? "תיקון תחבירי" : "ניסוח מחדש";
        ViewModel.StatusMessage = $"?? {displayType} הוחל בהצלחה ?";

        // Re-run AI review on the newly applied text
        _ = RunAiReviewInBackground(note);
    }

    /// <summary>
    /// Runs the two AI checks for the inspection note in the background:
    /// <list type="bullet">
    ///   <item><see cref="AiModelLevel.Simple"/> for the quick mistake / spelling check.</item>
    ///   <item><see cref="AiModelLevel.QualityCheck"/> for the wording / phrasing check.</item>
    /// </list>
    /// The concrete model name is resolved per-level by <see cref="AiService"/> from
    /// <see cref="SystemSettingsService"/>; the inspection form does not know it.
    /// </summary>
    private async Task RunAiReviewInBackground(NoteTreeItem note)
    {
        System.Diagnostics.Debug.WriteLine("[AI Flow] RunAiReviewInBackground — ENTERED");

        var aiService = App.ServiceProvider.GetService<AiService>();
        if (aiService is null)
        {
            System.Diagnostics.Debug.WriteLine("[AI Flow] ? AiService is NULL in DI — aborting.");
            return;
        }
        System.Diagnostics.Debug.WriteLine("[AI Flow] ? AiService resolved from DI");

        var (plainText, _) = RichTextCodec.Parse(note.NoteText ?? "");
        if (string.IsNullOrWhiteSpace(plainText))
        {
            System.Diagnostics.Debug.WriteLine("[AI Flow] ? Plain text is empty after parse — aborting.");
            return;
        }
        // Length only — never log the note text itself (may contain sensitive project info).
        System.Diagnostics.Debug.WriteLine($"[AI Flow] ? Plain text extracted ({plainText.Length} chars)");

        // Skip if already reviewed for this exact text
        if (note.AiOriginalText == plainText && !note.AiReviewInProgress)
        {
            System.Diagnostics.Debug.WriteLine("[AI Flow] ? Already reviewed for this exact text — skipping.");
            return;
        }

        note.ClearAiResults();
        note.AiOriginalText = plainText;
        note.AiReviewInProgress = true;
        ViewModel.StatusMessage = "?? AI בודק ברקע...";

        System.Diagnostics.Debug.WriteLine($"[AI Flow] ?? Sending request to AI at {DateTime.Now:HH:mm:ss}...");
        try
        {
            // 1) Mistake check ? Simple level
            var grammar = (await CheckMistakesWithAiAsync(aiService, plainText).ConfigureAwait(false))?.Trim();
            // 2) Wording check ? QualityCheck level (NOT Writing — Writing is reserved for future rewrite action)
            var rephrased = (await CheckWordingWithAiAsync(aiService, plainText).ConfigureAwait(false))?.Trim();

            System.Diagnostics.Debug.WriteLine(
                $"[AI Flow] ?? AI responses received at {DateTime.Now:HH:mm:ss}.");

            await Dispatcher.InvokeAsync(() =>
            {
                note.AiGrammarResult = grammar;
                note.AiRephraseResult = rephrased;
                ViewModel.StatusMessage = "?? AI סיים — לחץ ימין לתפריט ?";
            });
        }
        catch (AiModelNotConfiguredException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AI Review] No model configured for level {ex.Level}: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                ViewModel.StatusMessage = $"?? {ex.Message}";
            });
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[AI Review] ? Request timed out or was cancelled.");
            await Dispatcher.InvokeAsync(() =>
            {
                ViewModel.StatusMessage = "?? AI לא הגיב תוך הזמן המוקצב (timeout).";
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AI Review] Background review exception: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                ViewModel.StatusMessage = $"?? שגיאה: {ex.Message}";
            });
        }
        finally
        {
            Dispatcher.Invoke(() => note.AiReviewInProgress = false);
        }
    }

    /// <summary>
    /// Simple mistake / spelling / punctuation check.
    /// Uses <see cref="AiModelLevel.Simple"/> — fast, cheap model configured in AI Settings.
    /// </summary>
    private static Task<string> CheckMistakesWithAiAsync(AiService aiService, string plainText)
    {
        System.Diagnostics.Debug.WriteLine("[AI Flow] ? CheckMistakesWithAiAsync (level=Simple)");
        return aiService.AskAsync($"{AiPrompts.Grammar}\n{plainText}", AiModelLevel.Simple);
    }

    /// <summary>
    /// Wording / phrasing check on the inspection note.
    /// Uses <see cref="AiModelLevel.QualityCheck"/>; <see cref="AiModelLevel.Writing"/>
    /// is intentionally reserved for a future "rewrite / draft" action.
    /// </summary>
    private static Task<string> CheckWordingWithAiAsync(AiService aiService, string plainText)
    {
        System.Diagnostics.Debug.WriteLine("[AI Flow] ? CheckWordingWithAiAsync (level=QualityCheck)");
        return aiService.AskAsync($"{AiPrompts.Rephrase}\n{plainText}", AiModelLevel.QualityCheck);
    }

    /// <summary>
    /// Creates the Google template provider and export service using vault-based configuration
    /// and injects them into the ViewModel with the admin-configured folder ID
    /// retrieved from the centralized SystemSettings DB table.
    /// </summary>
    private static async void WireGoogleServices(FloatingInspectionViewModel viewModel)
    {
        try
        {
            // ?? Check Google client secrets availability ??
            if (string.IsNullOrWhiteSpace(AppConfiguration.GetGoogleClientSecretsPath()))
            {
                System.Diagnostics.Debug.WriteLine("[InspectionView] Google credentials not configured — Google services not wired.");
                return;
            }

            // ?? Resolve shared GoogleAuthService (singleton — single auth per session) ??
            var authService = App.ServiceProvider.GetRequiredService<GoogleAuthService>();

            // ?? Template Provider ??
            var provider = new GoogleInspectionTemplateProvider(authService);

            // ?? Export Service (with logger for diagnostic output) ??
            var dbContextFactory = App.ServiceProvider
                .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
            var loggerFactory = App.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
            var exportLogger = loggerFactory?.CreateLogger<GoogleReportExportService>();
            var exportService = new GoogleReportExportService(authService, dbContextFactory, exportLogger);

            // ?? Read admin-configured folder IDs from centralized DB settings ??
            var settingsService = App.ServiceProvider.GetRequiredService<SystemSettingsService>();
            var folderId = await settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionTemplatesFolderId, string.Empty);

            var reportsFolderId = await settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionReportsFolderId, string.Empty);
            exportService.ReportsFolderId = reportsFolderId;

            // ?? Inject into ViewModel ??
            viewModel.SetTemplateProvider(provider, folderId);
            viewModel.SetExportService(exportService);

            // ?? Planner Response Import Service ??
            var importLogger = loggerFactory?.CreateLogger<GooglePlannerResponseImportService>();
            var importService = new GooglePlannerResponseImportService(authService, dbContextFactory, importLogger);
            viewModel.SetPlannerResponseImportService(importService);

            // -- Note Screenshot Upload Service --
            var screenshotLogger = loggerFactory?.CreateLogger<GoogleNoteScreenshotUploadService>();
            var screenshotService = new GoogleNoteScreenshotUploadService(authService, dbContextFactory, screenshotLogger)
            {
                ReportsFolderId = reportsFolderId
            };
            viewModel.SetScreenshotUploadService(screenshotService);

            System.Diagnostics.Debug.WriteLine(
                $"[InspectionView] Google services wired. FolderId={folderId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[InspectionView] Failed to wire Google services: {ex.Message}");
        }
    }
}
