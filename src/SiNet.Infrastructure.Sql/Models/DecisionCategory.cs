namespace SiNetSQL.Models;

/// <summary>
/// Lookup table for decision categories (e.g., "תכנון", "תקציב", "לוגיסטיקה").
/// </summary>
public class DecisionCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    // Navigation
    public virtual ICollection<ProjectDecision> Decisions { get; set; } = new List<ProjectDecision>();
}
