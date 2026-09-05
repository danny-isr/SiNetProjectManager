namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// Reads the first worksheet of a Google Spreadsheet as formatted cell values.
/// Does not parse inspection tag grammar — that stays with template sync.
/// </summary>
public interface IInspectionTemplateSheetReader
{
    /// <summary>
    /// Returns non-null cell rows for the first sheet, or a failed result with a Hebrew message.
    /// </summary>
    Task<InspectionTemplateSheetReadResult> ReadFirstSheetAsync(
        string spreadsheetId,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw sheet cell grid used by the shared template tag scanner.</summary>
public sealed record InspectionTemplateSheetReadResult(
    bool Succeeded,
    string? ErrorMessage,
    IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public static InspectionTemplateSheetReadResult Fail(string message) =>
        new(false, message, []);

    public static InspectionTemplateSheetReadResult Ok(IReadOnlyList<IReadOnlyList<object?>> rows) =>
        new(true, null, rows);
}
