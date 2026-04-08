using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiNetProjectManager.Services;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManager.WPFUserControl;

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

        // Initialize common floating behavior (opacity, settings, collapse)
        InitializeFloatingBehavior();
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
    /// Saves dirty notes and removes empty sub-notes.
    /// </summary>
    private void NoteEditor_EditCompleted(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextNoteEditor { DataContext: NoteTreeItem note }) return;

        if (note.IsDirty)
            ViewModel.SaveNote(note);

        if (string.IsNullOrWhiteSpace(note.NoteText))
            ViewModel.DeleteEmptyNote(note);
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
    /// Creates the Google template provider and export service using vault-based configuration
    /// and injects them into the ViewModel with the admin-configured folder ID
    /// retrieved from the centralized SystemSettings DB table.
    /// </summary>
    private static async void WireGoogleServices(FloatingInspectionViewModel viewModel)
    {
        try
        {
            // ── Get Google client secrets from vault ──
            var clientSecretsPath = AppConfiguration.GetGoogleClientSecretsPath();
            if (string.IsNullOrWhiteSpace(clientSecretsPath))
            {
                System.Diagnostics.Debug.WriteLine("[InspectionView] Google credentials not configured — Google services not wired.");
                return;
            }

            // ── Create GoogleAuthService ──
            var authService = new GoogleAuthService(
                clientSecretsPath,
                AppConfiguration.GoogleTokenStorePath,
                AppConfiguration.GoogleApplicationName);

            // ── Template Provider ──
            var provider = new GoogleInspectionTemplateProvider(authService);

            // ── Export Service (with logger for diagnostic output) ──
            var dbContextFactory = App.ServiceProvider
                .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
            var loggerFactory = App.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
            var exportLogger = loggerFactory?.CreateLogger<GoogleReportExportService>();
            var exportService = new GoogleReportExportService(authService, dbContextFactory, exportLogger);

            // ── Read admin-configured folder IDs from centralized DB settings ──
            var settingsService = App.ServiceProvider.GetRequiredService<SystemSettingsService>();
            var folderId = await settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionTemplatesFolderId, string.Empty);

            var reportsFolderId = await settingsService.GetOrDefaultAsync(
                SystemSettingKeys.InspectionReportsFolderId, string.Empty);
            exportService.ReportsFolderId = reportsFolderId;

            // ── Inject into ViewModel ──
            viewModel.SetTemplateProvider(provider, folderId);
            viewModel.SetExportService(exportService);

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
