namespace SiNetProjectManagerV2.Services.Migration.Models;

/// <summary>
/// Aggregated result of a Phase 2 report-import batch.
/// Each selected preview row is processed independently; individual failures
/// do not block other rows.
/// </summary>
public sealed class ReportImportResult
{
    public int RowsProcessed { get; set; }
    public int ReportsCreated { get; set; }
    public int ReportsSkippedAlreadyExists { get; set; }
    public int ReportsSkippedConflict { get; set; }
    public int JsonMissing { get; set; }
    public int NotesImported { get; set; }
    public int NotesSkippedTemplateMismatch { get; set; }
    public int GapNotesCreated { get; set; }
    public int PlannerResponsesImported { get; set; }
    public int GeneralFieldsSkipped { get; set; }
    public int GeneralFieldsImported { get; set; }
    public int PlaceholderDefaultsFilled { get; set; }
    public int Errors { get; set; }

    /// <summary>
    /// Human-readable log messages generated during the import.
    /// </summary>
    public List<string> Messages { get; } = [];

    /// <summary>Append a log line and optionally forward it to a UI callback.</summary>
    public void Log(string message, Action<string>? uiLog = null)
    {
        Messages.Add(message);
        uiLog?.Invoke(message);
    }

    /// <summary>Build a summary string suitable for the UI log output.</summary>
    public string BuildSummary()
    {
        return $"""
            ═══ Phase 2 Import Summary ═══
            Rows processed:           {RowsProcessed}
            Reports created:          {ReportsCreated}
            Reports skipped (exists): {ReportsSkippedAlreadyExists}
            Reports skipped (conflict): {ReportsSkippedConflict}
            JSON missing:             {JsonMissing}
            Notes imported:           {NotesImported}
            Notes skipped (template): {NotesSkippedTemplateMismatch}
            Gap notes created:        {GapNotesCreated}
            Planner responses:        {PlannerResponsesImported}
            General fields imported:  {GeneralFieldsImported}
            General fields skipped:   {GeneralFieldsSkipped}
            Placeholder defaults:     {PlaceholderDefaultsFilled}
            Errors:                   {Errors}
            ═══════════════════════════════
            """;
    }
}
