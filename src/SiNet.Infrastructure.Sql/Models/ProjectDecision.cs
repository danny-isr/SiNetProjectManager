namespace SiNetSQL.Models;

/// <summary>
/// A project-level decision record.
/// Tracks the current content and audit fields; previous versions are stored in <see cref="DecisionHistory"/>.
/// </summary>
public class ProjectDecision
{
    public int Id { get; set; }

    /// <summary>FK to the project this decision belongs to.</summary>
    public int ProjectId { get; set; }

    /// <summary>FK to the category (optional grouping).</summary>
    public int CategoryId { get; set; }

    /// <summary>The current decision text.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>User who created this decision.</summary>
    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>User who last edited this decision (null if never edited).</summary>
    public int? LastUpdatedByUserId { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    // Navigation properties
    public virtual Project Project { get; set; } = null!;
    public virtual DecisionCategory Category { get; set; } = null!;
    public virtual Siuser CreatedByUser { get; set; } = null!;
    public virtual Siuser? LastUpdatedByUser { get; set; }
    public virtual ICollection<DecisionHistory> History { get; set; } = new List<DecisionHistory>();
}
