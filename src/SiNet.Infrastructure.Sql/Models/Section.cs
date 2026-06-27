using System.ComponentModel.DataAnnotations.Schema;

namespace SiNetSQL.Models;

/// <summary>
/// Versioned template section within a chapter (e.g., section 1 inside Chapter 3 = "3.1").
/// Each (<see cref="ChapterId"/>, <see cref="SectionCode"/>) pair may have multiple versions;
/// only one should be <see cref="IsActive"/>.
/// Display name is resolved from the <see cref="SectionName"/> dictionary.
/// </summary>
public class Section
{
    public int SectionId { get; set; }

    /// <summary>FK → <see cref="Models.Chapter"/> — the chapter this section belongs to.</summary>
    public int ChapterId { get; set; }

    /// <summary>FK → <see cref="Models.SectionName"/> — the dictionary entry for this section's display text.</summary>
    public int SectionNameId { get; set; }

    /// <summary>Sub-number only (e.g., <c>1</c> for section "3.1"). Full code derived from <see cref="FullCode"/>.</summary>
    public int SectionCode { get; set; }

    /// <summary>Monotonically increasing version counter (1, 2, 3…).</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Soft-delete / versioning flag. Only the latest version for a given
    /// (<see cref="ChapterId"/>, <see cref="SectionCode"/>) should have <c>true</c>.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Derived full section code: "<c>ChapterNumber.SectionCode</c>" (e.g., "3.1").
    /// Requires <see cref="Chapter"/> to be loaded.
    /// </summary>
    [NotMapped]
    public string FullCode => Chapter != null
        ? $"{Chapter.ChapterNumber}.{SectionCode}"
        : SectionCode.ToString();

    // Navigation
    public virtual Chapter Chapter { get; set; } = null!;

    public virtual SectionName SectionName { get; set; } = null!;

    public virtual ICollection<InspectionNote> InspectionNotes { get; set; } = new List<InspectionNote>();

    public virtual ICollection<CommentsBank> CommentsBank { get; set; } = new List<CommentsBank>();
}
