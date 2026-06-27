namespace SiNetSQL.Models;

/// <summary>
/// Pre-defined comment snippets linked to a section for reuse during inspections.
/// </summary>
public class CommentsBank
{
    public int CommentId { get; set; }

    public int SectionId { get; set; }

    public string? CommonText { get; set; }

    // Navigation
    public virtual Section Section { get; set; } = null!;
}
