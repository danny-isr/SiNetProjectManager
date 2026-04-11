using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SiNetProjectManager.Services.Stamping;
using SiNetSQL.Services;

namespace SiNetProjectManager.WPF_Window;

/// <summary>
/// A standalone quick-stamp utility window.
/// Lets the user pick any DWF/PDF file, fill in inspector name + date,
/// and stamp it immediately — independent of the inspection workflow.
/// </summary>
public partial class QuickStampWindow : Window
{
    private string? _selectedFilePath;
    private bool _isDwf;
    private IReadOnlyList<DwfLayoutInfo>? _layouts;

    public QuickStampWindow()
    {
        InitializeComponent();
        StampDatePicker.SelectedDate = DateTime.Now;
        InspectorNameTextBox.Text = Environment.UserName;
    }

    // ── Browse ─────────────────────────────────────────────────────────

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "בחר קובץ סרטוט לחתימה",
            Filter = "Drawing Files (*.dwf;*.pdf)|*.dwf;*.pdf|DWF Files (*.dwf)|*.dwf|PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        _selectedFilePath = dialog.FileName;
        _isDwf = Path.GetExtension(_selectedFilePath)
            .Equals(".dwf", StringComparison.OrdinalIgnoreCase);

        FilePathTextBox.Text = _selectedFilePath;
        StampButton.IsEnabled = true;

        var typeLabel = _isDwf ? "DWF (תבנית)" : "PDF (פרוגרמטי)";
        StatusTextBlock.Text = $"קובץ נבחר: {Path.GetFileName(_selectedFilePath)}\nסוג: {typeLabel}";

        var outputName = $"{Path.GetFileNameWithoutExtension(_selectedFilePath)}_stamped{Path.GetExtension(_selectedFilePath)}";
        OutputInfoLabel.Text = $"פלט: {Path.Combine(Path.GetDirectoryName(_selectedFilePath)!, outputName)}";

        // Layout discovery for DWF files
        if (_isDwf)
        {
            try
            {
                var path = _selectedFilePath;
                _layouts = await Task.Run(() => DwfStampManager.GetLayouts(path));

                LayoutComboBox.ItemsSource = _layouts;
                LayoutComboBox.DisplayMemberPath = "LayoutName";
                LayoutComboBox.SelectedIndex = 0;
                LayoutSelectorPanel.Visibility = Visibility.Visible;
                StampTitlePanel.Visibility = Visibility.Collapsed;

                StatusTextBlock.Text += $"\nנמצאו {_layouts.Count} layouts.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text += $"\n⚠️ שגיאה בזיהוי layouts: {ex.Message}";
                _layouts = null;
                LayoutSelectorPanel.Visibility = Visibility.Collapsed;
                StampTitlePanel.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            _layouts = null;
            LayoutSelectorPanel.Visibility = Visibility.Collapsed;
            StampTitlePanel.Visibility = Visibility.Visible;
        }
    }

    // ── Stamp ──────────────────────────────────────────────────────────

    private async void StampButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedFilePath) || !File.Exists(_selectedFilePath))
        {
            StatusTextBlock.Text = "❌ קובץ לא נמצא. בחר קובץ אחר.";
            return;
        }

        if (string.IsNullOrWhiteSpace(InspectorNameTextBox.Text))
        {
            StatusTextBlock.Text = "❌ נא למלא שם בודק.";
            return;
        }

        var stampDate = StampDatePicker.SelectedDate ?? DateTime.Now;
        var inspectorName = InspectorNameTextBox.Text.Trim();

        var outputPath = Path.Combine(
            Path.GetDirectoryName(_selectedFilePath)!,
            $"{Path.GetFileNameWithoutExtension(_selectedFilePath)}_stamped{Path.GetExtension(_selectedFilePath)}");

        StampButton.IsEnabled = false;
        StatusTextBlock.Text = "⏳ חותם...";

        try
        {
            if (_isDwf)
            {
                await StampDwfAsync(_selectedFilePath, outputPath, inspectorName, stampDate);
            }
            else
            {
                var stampTitle = StampTitleTextBox.Text.Trim();
                await StampPdfAsync(_selectedFilePath, outputPath, inspectorName, stampDate, stampTitle);
            }

            StatusTextBlock.Text = $"✅ הקובץ נחתם בהצלחה!\n📁 {outputPath}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ שגיאה: {ex.Message}";
        }
        finally
        {
            StampButton.IsEnabled = true;
        }
    }

    // ── PDF stamp (programmatic — no template needed) ──────────────────

    private static Task StampPdfAsync(string sourcePath, string outputPath,
        string inspectorName, DateTime stampDate, string title)
    {
        return Task.Run(() =>
        {
            PdfStampManager.GenerateAndApplyStamp(sourcePath, outputPath,
                new PdfGeneratedStampOptions
                {
                    Title = title,
                    InspectorName = inspectorName,
                    StampDate = stampDate,
                    Placement = StampPlacement.BottomRight,
                    StampAllPages = true,
                    OverwriteOutput = true
                });
        });
    }

    // ── DWF stamp (template-based + date placeholder replacement) ──────

    private async Task StampDwfAsync(string sourcePath, string outputPath,
        string inspectorName, DateTime stampDate)
    {
        // Resolve DWF template path from system settings
        var templatePath = await ResolveDwfTemplatePathAsync();

        if (string.IsNullOrWhiteSpace(templatePath))
            throw new InvalidOperationException(
                "לא הוגדר נתיב תבנית חותמת DWF בהגדרות הניהול.\n" +
                "הגדר אותו בתפריט: מנהלה → הגדרות ניהול → נתיב תבנית חותמת DWF.");

        // Use the layout selected by the user, or discover + use first
        var selectedLayout = LayoutComboBox.SelectedItem as DwfLayoutInfo;

        if (selectedLayout == null)
        {
            var layouts = _layouts
                ?? await Task.Run(() => DwfStampManager.GetLayouts(sourcePath));

            if (layouts.Count == 0)
                throw new InvalidOperationException("לא נמצאו layouts בקובץ DWF.");

            selectedLayout = layouts[0];
        }

        StatusTextBlock.Text = $"⏳ חותם layout: \"{selectedLayout.LayoutName}\" (index {selectedLayout.Index})...";

        // Insert stamp from template into the selected layout
        await Task.Run(() =>
        {
            var result = DwfStampManager.AddStampFromTemplate(
                sourcePath, templatePath, outputPath,
                new DwfStampInsertOptions
                {
                    LayoutIndex = selectedLayout.Index,
                    OverwriteOutput = true
                });

            if (!result.Success)
                throw new InvalidOperationException($"החתמה נכשלה: {result.Message}");

            // Replace date placeholder in the stamped output
            var replacements = new Dictionary<string, string>
            {
                [DwfStampManager.StampPlaceholders.Date] = stampDate.ToString("dd/MM/yyyy")
            };

            DwfStampManager.ReplaceStampPlaceholders(outputPath, replacements);
        });
    }

    private static async Task<string?> ResolveDwfTemplatePathAsync()
    {
        var settingsService = App.ServiceProvider.GetRequiredService<SystemSettingsService>();
        var path = await settingsService.GetOrDefaultAsync(
            SystemSettingKeys.StampTemplatePath, string.Empty);
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
    }

    // ── Close ──────────────────────────────────────────────────────────

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
