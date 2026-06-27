namespace SiNetSQL.Models;

/// <summary>
/// Dictionary table storing unique section display names (e.g., "Signage and Striping", "ProjectName").
/// These correspond to bracket content in template tags or tag labels for general fields.
/// Multiple <see cref="Section"/> rows can reference the same name.
/// </summary>
public class SectionName
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Navigation
    public virtual ICollection<Section> Sections { get; set; } = new List<Section>();
}
