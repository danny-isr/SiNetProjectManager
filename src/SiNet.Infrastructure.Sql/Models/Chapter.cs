namespace SiNetSQL.Models;

/// <summary>
/// Template chapter scoped to an <see cref="InspectionSeries"/> (e.g., Chapter 3 "Parking").
/// Display name is resolved from the <see cref="ChapterName"/> dictionary.
/// </summary>
public class Chapter
{
    public int ChapterId { get; set; }

    /// <summary>FK → <see cref="InspectionSeries"/> — which template series this chapter belongs to. Nullable for shared chapters (e.g., Chapter 0).</summary>
    public int? SeriesId { get; set; }

    public int ChapterNumber { get; set; }

    /// <summary>FK → <see cref="Models.ChapterName"/> — the dictionary entry for this chapter's display text.</summary>
    public int ChapterNameId { get; set; }

    // Navigation
    public virtual InspectionSeries? Series { get; set; }

    public virtual ChapterName ChapterName { get; set; } = null!;

    public virtual ICollection<Section> Sections { get; set; } = new List<Section>();
}
