namespace SiNetSQL.Models;

/// <summary>
/// Dictionary table storing unique chapter display names (e.g., "Parking", "General").
/// Multiple <see cref="Chapter"/> rows can reference the same name.
/// </summary>
public class ChapterName
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Navigation
    public virtual ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
}
