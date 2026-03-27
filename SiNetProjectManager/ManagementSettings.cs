using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiNetProjectManager;

/// <summary>
/// Management-level settings that can only be modified by administrators.
/// These settings affect business logic and report calculations.
/// </summary>
public class ManagementSettings : INotifyPropertyChanged
{
    public ManagementSettings()
    {
        // Default values
        hourPriceDefault = 280m;
        defaultProjectTitle = "ניהול  משרד - כללי";
    }

    // === Email Ingestion Settings ===

    private string defaultProjectTitle;

    /// <summary>
    /// Title of the default project used for email ingestion.
    /// New emails that have not been assigned to a project will use this.
    /// Must exactly match an existing project title in the database.
    /// </summary>
    public string DefaultProjectTitle
    {
        get => defaultProjectTitle;
        set
        {
            if (value == defaultProjectTitle) return;
            defaultProjectTitle = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    // === Inspection Templates Settings ===

    private string inspectionTemplatesFolderId = string.Empty;

    /// <summary>
    /// Google Drive Folder ID containing the inspection template Google Sheets.
    /// The system scans this folder for available templates (Sheets only).
    /// Configurable by administrators.
    /// </summary>
    public string InspectionTemplatesFolderId
    {
        get => inspectionTemplatesFolderId;
        set
        {
            if (value == inspectionTemplatesFolderId) return;
            inspectionTemplatesFolderId = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    // === Reports Output Settings ===

    private string reportsOutputRoot = string.Empty;

    /// <summary>
    /// Root directory for exported inspection reports (local backup).
    /// When empty, no local copy is saved.
    /// </summary>
    public string ReportsOutputRoot
    {
        get => reportsOutputRoot;
        set
        {
            if (value == reportsOutputRoot) return;
            reportsOutputRoot = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    private string inspectionReportsFolderId = string.Empty;

    /// <summary>
    /// Google Drive Folder ID for storing exported inspection reports.
    /// Reports are saved in sub-folders: [ReportsFolder]/[ParentProject]/[Project]/[Report].
    /// </summary>
    public string InspectionReportsFolderId
    {
        get => inspectionReportsFolderId;
        set
        {
            if (value == inspectionReportsFolderId) return;
            inspectionReportsFolderId = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    // === Report Calculation Settings ===

    private decimal hourPriceDefault;

    /// <summary>
    /// Default hourly rate used for calculating hours from submitted values.
    /// Formula: CalculatedHours = SubmittedValueToDate / HourPrice
    /// </summary>
    public decimal HourPriceDefault
    {
        get => hourPriceDefault;
        set
        {
            if (value == hourPriceDefault) return;
            hourPriceDefault = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
