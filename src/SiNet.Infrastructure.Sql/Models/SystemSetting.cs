namespace SiNetSQL.Models;

/// <summary>
/// Centralized key-value configuration stored in the database.
/// All users share the same settings — managed by administrators.
/// Supports current parameters (e.g., InspectionTemplatesFolderId)
/// and future ones (e.g., PDF_Export_Path, Default_Email_Recipient).
/// </summary>
public class SystemSetting
{
    /// <summary>
    /// Unique setting identifier (e.g., "InspectionTemplatesFolderId").
    /// </summary>
    public string SettingKey { get; set; } = string.Empty;

    /// <summary>
    /// The setting value. Stored as string — consumers parse as needed.
    /// </summary>
    public string SettingValue { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description shown in the Admin UI.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// UTC timestamp of the last modification.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
