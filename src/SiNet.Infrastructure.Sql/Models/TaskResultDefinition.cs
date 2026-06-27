namespace SiNetSQL.Models;

/// <summary>
/// Lookup table for professional/business task outcomes (separated from generic
/// <see cref="ProjectAssignmentStatus"/>).
/// <para>
/// Examples: <c>QuoteSent</c>, <c>AuthorityApproved</c>, <c>MaterialMissing</c>.
/// </para>
/// </summary>
public partial class TaskResultDefinition
{
    public int Id { get; set; }

    /// <summary>Stable machine identifier (never localized). Unique.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Hebrew display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional grouping (e.g. "Quote", "Material", "Approval", "Billing").</summary>
    public string? Category { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual ICollection<ProjectAssignmentEvent> Events { get; set; } = new List<ProjectAssignmentEvent>();

    public virtual ICollection<ProjectAssignment> AssignmentsLastResult { get; set; } = new List<ProjectAssignment>();
}
