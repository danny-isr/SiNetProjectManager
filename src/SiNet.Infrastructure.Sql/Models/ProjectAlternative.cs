namespace SiNetSQL.Models;

/// <summary>
/// Represents a design alternative scoped to a single <see cref="Project"/>.
/// <para>
/// Hierarchy: Project → ProjectAlternative.
/// Alternatives are NOT scoped per JobType / TypeOfProjectInProject.
/// The same alternative "1" (or "1~15.05.2026", "בינוי~עדכון יועץ" etc.) is shared
/// across every JobType in the project. JobType remains attached to the file itself
/// (<see cref="ProjectFile"/> / attachment) and is consumed by the file name builder,
/// not by the alternative.
/// </para>
/// <para>
/// <see cref="Name"/> stores the full value as entered by the user, including the
/// '~' UI grouping separator. <see cref="NormalizedName"/> is the lookup key used
/// to prevent duplicates within the same project.
/// </para>
/// </summary>
public class ProjectAlternative
{
    public int Id { get; set; }

    /// <summary>FK to the owning <see cref="Project"/> (required).</summary>
    public int ProjectId { get; set; }

    /// <summary>
    /// Human-readable name including the optional '~' grouping separator
    /// (e.g., "1", "1~15.05.2026", "בינוי~עדכון יועץ"). Defaults to "1".
    /// </summary>
    public string Name { get; set; } = "1";

    /// <summary>
    /// Lookup key used to detect duplicates among ACTIVE alternatives within the
    /// same project. Computed from <see cref="Name"/> by trimming, collapsing
    /// whitespace, trimming around '~', and lower-casing (invariant culture).
    /// Bootstrap of "1" is handled in code (not at DB level).
    /// </summary>
    public string NormalizedName { get; set; } = "1";

    /// <summary>
    /// Short machine-friendly code (e.g., "ALT-A", "V1").
    /// Optional but recommended for programmatic lookups and integrations.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Optional free-text description of this alternative.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is the primary (active/selected) alternative for the project.
    /// At most one alternative per project should be marked as primary; enforced
    /// at the service layer.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>Display/execution order (1-based).</summary>
    public int SortOrder { get; set; }

    /// <summary>Soft-delete / disable flag.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional filesystem path associated with this alternative.
    /// Populated when the alternative was discovered or linked via folder scanning.
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// Indicates this record was auto-created by a folder-scan import process
    /// rather than manually by a user.
    /// </summary>
    public bool CreatedFromFolderScan { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>FK to the user who created this record (nullable for system-created).</summary>
    public int? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>FK to the user who last updated this record.</summary>
    public int? UpdatedBy { get; set; }

    // ═══ Navigation ═══

    public virtual Project Project { get; set; } = null!;

    public virtual Siuser? CreatedByUser { get; set; }

    public virtual Siuser? UpdatedByUser { get; set; }
}
