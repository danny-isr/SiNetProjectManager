using System.Linq;
using System.Windows;
using SiNetProjectManagerV2.Services.Migration;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Read-only preview window that shows the full deterministic extraction result
/// for a single report/version — what the system would fill during a later import.
///
/// Preview only — no DB write, no extraction, no AI.
/// </summary>
public partial class FullReportFillPreviewWindow : Window
{
    /// <summary>
    /// Opens the full report fill preview for the given cached envelope.
    /// </summary>
    /// <param name="envelope">Extraction result (from _lastDeterministicResult or cache).</param>
    /// <param name="cachePath">Optional path of the JSON cache file, shown in technical details if available.</param>
    public FullReportFillPreviewWindow(ExtractionCacheEnvelope envelope, string? cachePath = null)
    {
        InitializeComponent();
        PopulateReportFillFields(envelope);
        PopulateGeneralFields(envelope);
        PopulateSections(envelope);
        PopulateTechDetails(envelope, cachePath);
        UpdateTitle(envelope);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Section 1 — Report-fill fields (business values only)
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateReportFillFields(ExtractionCacheEnvelope envelope)
    {
        // Section 1 contains only envelope identity fields (ProjectNumber, ReportNumber, VersionIndex).
        // Report-fill fields extracted from the template (GeneralFields) are shown in Section 2.
        var rows = new List<ReportFillFieldRow>
        {
            new("מספר פרויקט", "ProjectNumber",  OrNull(envelope.ProjectNumber),                                       "שדה זהות"),
            new("מספר דוח",    "ReportNumber",   OrNull(envelope.ReportNumber),                                        "שדה זהות"),
            new("אינדקס גרסה", "VersionIndex",   envelope.VersionIndex > 0 ? envelope.VersionIndex.ToString() : null, "שדה זהות"),
        };

        ReportFieldsGrid.ItemsSource = rows;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Section 2 — All GeneralFields from extraction (raw, sorted)
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateGeneralFields(ExtractionCacheEnvelope envelope)
    {
        if (envelope.GeneralFields.Count == 0)
        {
            GeneralFieldsGrid.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new("—", "לא זוהו שדות כלליים בתוצאת החילוץ הנוכחית.")
            };
            return;
        }

        GeneralFieldsGrid.ItemsSource = envelope.GeneralFields
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value))
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Section 3 — Full extracted sections
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateSections(ExtractionCacheEnvelope envelope)
    {
        SectionsGrid.ItemsSource = envelope.Sections;

        var splitCount    = envelope.Sections.Count(s => s.WasSplit);
        var resolvedCount = envelope.Sections.Count(s => s.IsResolved);
        var datedCount    = envelope.Sections.Count(s => s.ClosedDate != null);

        SectionCountLabel.Text =
            $"סה\"כ: {envelope.Sections.Count} שורות  |  פוצלו: {splitCount}  |  נסגרו: {resolvedCount}  |  עם תאריך: {datedCount}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Technical details — collapsed Expander at the bottom
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateTechDetails(ExtractionCacheEnvelope envelope, string? cachePath)
    {
        var rows = new List<KeyValuePair<string, string>>();

        AddTech(rows, "Template Spreadsheet ID", envelope.TemplateSpreadsheetId);
        AddTech(rows, "Report Spreadsheet ID",   envelope.ReportSpreadsheetId);
        AddTech(rows, "Extraction method",       "Deterministic (no AI)");
        AddTech(rows, "Cache status",            string.IsNullOrEmpty(cachePath) ? "In-memory only" : "JSON cache");

        if (envelope.ExtractedAtUtc != default)
            AddTech(rows, "Extracted at (UTC)", envelope.ExtractedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"));

        var count = envelope.SectionCount > 0 ? envelope.SectionCount : envelope.Sections.Count;
        AddTech(rows, "Section count", count.ToString());

        if (!string.IsNullOrEmpty(cachePath))
            AddTech(rows, "JSON cache path", cachePath);

        if (envelope.Warnings.Count > 0)
            AddTech(rows, "Warnings", $"{envelope.Warnings.Count} warnings: " +
                          string.Join(" | ", envelope.Warnings.Take(5)));

        TechDetailsItems.ItemsSource = rows;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Title
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateTitle(ExtractionCacheEnvelope envelope)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(envelope.ProjectNumber))
            parts.Add($"פרויקט {envelope.ProjectNumber}");
        if (!string.IsNullOrWhiteSpace(envelope.ReportNumber))
            parts.Add($"דוח {envelope.ReportNumber}");
        if (envelope.VersionIndex > 0)
            parts.Add($"גרסה {envelope.VersionIndex}");

        Title = parts.Count > 0
            ? $"Full Report Fill Preview — {string.Join(" / ", parts)} — Preview Only"
            : "Full Report Fill Preview — Preview Only";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string? OrNull(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    private static void AddTech(List<KeyValuePair<string, string>> list, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            list.Add(new KeyValuePair<string, string>(key, value));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTO for Section 1 — Report-fill field rows
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single row in the "report-fill fields" section of the preview window.
/// Represents one field that would be populated during a future report import.
/// </summary>
public sealed class ReportFillFieldRow
{
    public string DisplayName { get; }
    public string FieldKey    { get; }
    public string Value       { get; }
    public string SourceNote  { get; }
    public bool   IsAvailable { get; }

    public ReportFillFieldRow(string displayName, string fieldKey, string? value, string? sourceNote)
    {
        DisplayName = displayName;
        FieldKey    = fieldKey;
        IsAvailable = !string.IsNullOrWhiteSpace(value);
        Value       = IsAvailable ? value! : "לא זוהה";
        SourceNote  = sourceNote ?? string.Empty;
    }
}

