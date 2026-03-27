using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
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
/// Persists window position via AppSettings/SettingsManager (JSON file).
/// Wires the Google-based template provider into the ViewModel.
/// </summary>
public partial class FloatingInspectionView : Window
{
    private bool _isMouseOver;

    // Collapse/expand: store previous dimensions
    private double _expandedWidth;
    private double _expandedHeight;
    private double _expandedMinWidth;
    private double _expandedMinHeight;

    /// <summary>Scale factor applied to the global <c>AppFontSize</c> for this compact floating window.</summary>
    private const double FontScaleFactor = 0.8;

    public FloatingInspectionView()
    {
        InitializeComponent();

        // Apply scaled font size (80% of global AppFontSize)
        ApplyScaledFontSize();

        var viewModel = App.ServiceProvider.GetRequiredService<FloatingInspectionViewModel>();
        DataContext = viewModel;

        // Subscribe to ViewModel property changes for collapse handling
        viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Apply opacity settings from AppSettings
        var settings = App.AppSettings;
        if (settings != null)
        {
            viewModel.ActiveOpacity = settings.FloatingWindowActiveOpacity;
            viewModel.IdleOpacity = settings.FloatingWindowIdleOpacity;
            settings.PropertyChanged += Settings_PropertyChanged;
        }

        ContentBorder.Opacity = viewModel.IdleOpacity;

        // Wire the Google template provider and export service into the ViewModel
        WireGoogleServices(viewModel);
    }

    /// <summary>
    /// Gets the ViewModel for external access.
    /// </summary>
    public FloatingInspectionViewModel ViewModel => (FloatingInspectionViewModel)DataContext;

    /// <summary>
    /// Restores saved window position on load.
    /// Falls back to CenterScreen if no saved position or if saved bounds are off-screen.
    /// </summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = App.AppSettings;
        if (settings == null)
            return;

        var top = settings.FloatingInspectionTop;
        var left = settings.FloatingInspectionLeft;
        var width = settings.FloatingInspectionWidth;
        var height = settings.FloatingInspectionHeight;

        // Validate that we have a saved position (not NaN) and dimensions are reasonable
        if (!double.IsNaN(top) && !double.IsNaN(left) && width > 0 && height > 0)
        {
            // Ensure the window is at least partially visible on any monitor
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualWidth = SystemParameters.VirtualScreenWidth;
            var virtualHeight = SystemParameters.VirtualScreenHeight;

            if (left >= virtualLeft - width + 50 &&
                left <= virtualLeft + virtualWidth - 50 &&
                top >= virtualTop - height + 50 &&
                top <= virtualTop + virtualHeight - 50)
            {
                Top = top;
                Left = left;
                Width = width;
                Height = height;
                return;
            }
        }

        // Default: full WorkArea height, positioned at right edge
        var workArea = SystemParameters.WorkArea;
        Width = DefaultWindowWidth;
        Height = workArea.Height;
        Top = workArea.Top;
        Left = workArea.Left + workArea.Width - Width;
    }

    /// <summary>
    /// Saves window position and size on closing.
    /// </summary>
    private void Window_Closing(object sender, CancelEventArgs e)
    {
        SaveWindowPosition();
    }

    /// <summary>
    /// Disposes the ViewModel to unsubscribe from ActiveProjectContext.
    /// </summary>
    private void Window_Closed(object sender, EventArgs e)
    {
        // Unsubscribe from settings changes
        var settings = App.AppSettings;
        if (settings != null)
        {
            settings.PropertyChanged -= Settings_PropertyChanged;
        }

        if (DataContext is FloatingInspectionViewModel vm)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
            vm.Dispose();
        }
    }

    /// <summary>
    /// Reacts to ViewModel property changes — handles collapse/expand transitions.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FloatingInspectionViewModel.IsCollapsed))
            return;

        if (ViewModel.IsCollapsed)
            ApplyCollapsedState();
        else
            ApplyExpandedState();
    }

    /// <summary>
    /// Collapses the window to header-only: stores current dimensions, shrinks.
    /// Body elements are hidden via XAML binding to <see cref="FloatingInspectionViewModel.IsExpanded"/>.
    /// </summary>
    private void ApplyCollapsedState()
    {
        // Store current dimensions for restore
        _expandedWidth = Width;
        _expandedHeight = Height;
        _expandedMinWidth = MinWidth;
        _expandedMinHeight = MinHeight;

        // Shrink to header-only compact size
        MinWidth = 200;
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        Width = Math.Min(Width, 260);
        ResizeMode = ResizeMode.NoResize;
    }

    /// <summary>
    /// Expands the window back to its previous full size.
    /// Body elements are shown via XAML binding to <see cref="FloatingInspectionViewModel.IsExpanded"/>.
    /// </summary>
    private void ApplyExpandedState()
    {
        // Restore dimensions
        SizeToContent = SizeToContent.Manual;
        MinWidth = _expandedMinWidth;
        MinHeight = _expandedMinHeight;
        Width = _expandedWidth;
        Height = _expandedHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
    }

    /// <summary>
    /// Persists current window bounds to AppSettings via SettingsManager.
    /// </summary>
    private void SaveWindowPosition()
    {
        var settings = App.AppSettings;
        if (settings == null)
            return;

        if (WindowState == WindowState.Normal && !ViewModel.IsCollapsed)
        {
            settings.FloatingInspectionTop = Top;
            settings.FloatingInspectionLeft = Left;
            settings.FloatingInspectionWidth = Width;
            settings.FloatingInspectionHeight = Height;
        }
        else if (ViewModel.IsCollapsed)
        {
            // Save position only; use stored expanded dimensions for size
            settings.FloatingInspectionTop = Top;
            settings.FloatingInspectionLeft = Left;
            if (_expandedWidth > 0) settings.FloatingInspectionWidth = _expandedWidth;
            if (_expandedHeight > 0) settings.FloatingInspectionHeight = _expandedHeight;
        }

        try
        {
            SettingsManager.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FloatingInspection] Failed to save window position: {ex.Message}");
        }
    }

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

    /// <summary>
    /// Reacts to AppSettings changes (from SettingsWindow sliders) in real time.
    /// Updates ViewModel opacity, font size, and animates the ContentBorder to the correct value.
    /// </summary>
    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Settings_PropertyChanged(sender, e));
            return;
        }

        var settings = App.AppSettings;
        if (settings == null) return;

        switch (e.PropertyName)
        {
            case nameof(AppSettings.FloatingWindowActiveOpacity)
              or nameof(AppSettings.FloatingWindowIdleOpacity):
                ViewModel.ActiveOpacity = settings.FloatingWindowActiveOpacity;
                ViewModel.IdleOpacity = settings.FloatingWindowIdleOpacity;
                AnimateOpacity(_isMouseOver ? ViewModel.ActiveOpacity : ViewModel.IdleOpacity);
                break;

            case nameof(AppSettings.FontSize):
                ApplyScaledFontSize();
                break;
        }
    }

    /// <summary>
    /// Fades to active (fully visible) opacity when the mouse enters the window.
    /// </summary>
    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOver = true;
        AnimateOpacity(ViewModel.ActiveOpacity);
    }

    /// <summary>
    /// Fades to idle (semi-transparent) opacity when the mouse leaves the window.
    /// </summary>
    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOver = false;
        AnimateOpacity(ViewModel.IdleOpacity);
    }

    /// <summary>
    /// Smoothly animates the window opacity to the target value over 0.3 seconds.
    /// </summary>
    private void AnimateOpacity(double targetOpacity)
    {
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        ContentBorder.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>
    /// Enables dragging the window from the custom header.
    /// </summary>
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    /// <summary>Default width for the floating inspection window.</summary>
    private const double DefaultWindowWidth = 420;

    /// <summary>
    /// Closes the floating window via the custom close button.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Resets the window to its default dimensions: narrow width, full screen height.
    /// </summary>
    private void ResetSizeButton_Click(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Width = DefaultWindowWidth;
        Height = workArea.Height;
        Top = workArea.Top;
        Left = workArea.Left + workArea.Width - Width;
    }

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
