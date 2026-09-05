namespace SiNetSQL.Services.InspectionSync;

/// <summary>
/// Represents a single row from the Google Sheet template.
/// The caller is responsible for parsing Google Sheets API responses into this DTO.
/// </summary>
public sealed class TemplateSyncRow
{
    /// <summary>Row number in the source sheet (1-based, excluding header).</summary>
    public int RowNumber { get; init; }

    /// <summary>Chapter number (e.g., 1, 2, 3).</summary>
    public int ChapterNumber { get; init; }

    /// <summary>Chapter title (e.g., "כללי", "חנייה").</summary>
    public string? ChapterTitle { get; init; }

    /// <summary>Section code (e.g., "1.6.1", "3.8").</summary>
    public required string SectionCode { get; init; }

    /// <summary>Section title / description text.</summary>
    public string? SectionTitle { get; init; }
}
