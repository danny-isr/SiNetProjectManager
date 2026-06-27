namespace SiNetSQL.Models;

/// <summary>
/// Represents an Autodesk Construction Cloud Hub (Account).
/// A hub is the top-level container in ACC, typically representing an organization.
/// </summary>
public class AccHub
{
    /// <summary>
    /// Primary key (auto-increment).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Autodesk hub/account ID (e.g., "b.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx").
    /// This is the unique identifier from Autodesk's system.
    /// </summary>
    public string HubId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name for the hub (optional).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Indicates if this is the default hub for the organization.
    /// Only one hub should be marked as default.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// UTC timestamp when this record was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when this record was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    // Navigation properties
    public virtual ICollection<AccSystemResource> SystemResources { get; set; } = new List<AccSystemResource>();
    public virtual ICollection<ProjectAccMapping> ProjectMappings { get; set; } = new List<ProjectAccMapping>();
}
