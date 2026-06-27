namespace SiNetSQL.Models;

/// <summary>
/// Bridge entity grouping inspection reports of the same type for a project.
/// For example, a project may have a "Monthly Safety" series and a "Quarterly Compliance" series,
/// each with its own Google Sheets template.
/// </summary>
public class InspectionSeries
{
    public int SeriesId { get; set; }

    public int ProjectId { get; set; }

    /// <summary>
    /// Human-readable name describing the inspection type (e.g. "בדיקת בטיחות חודשית").
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Full URL of the Google Sheets template used for this series.
    /// </summary>
    public string? TemplateUrl { get; set; }

    /// <summary>
    /// Extracted Google spreadsheet ID for API calls.
    /// </summary>
    public string? TemplateSpreadsheetId { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }

    // Navigation
    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<InspectionReport> InspectionReports { get; set; } = new List<InspectionReport>();

    public virtual ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();

    public virtual ICollection<InspectionSeriesFileConfig> FileConfigs { get; set; } = new List<InspectionSeriesFileConfig>();
}
