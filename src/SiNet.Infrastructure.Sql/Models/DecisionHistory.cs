namespace SiNetSQL.Models;

/// <summary>
/// Stores previous versions of a <see cref="ProjectDecision"/>.
/// Created automatically before each edit to preserve full history.
/// </summary>
public class DecisionHistory
{
    public int Id { get; set; }

    /// <summary>FK to the decision that was edited.</summary>
    public int DecisionId { get; set; }

    /// <summary>The content BEFORE the edit.</summary>
    public string OldContent { get; set; } = string.Empty;

    /// <summary>User who made the change.</summary>
    public int ChangedByUserId { get; set; }

    public DateTime ChangedAt { get; set; }

    // Navigation properties
    public virtual ProjectDecision Decision { get; set; } = null!;
    public virtual Siuser ChangedByUser { get; set; } = null!;
}
