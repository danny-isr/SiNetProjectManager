namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// Host seam that lists inspection templates (typically Google Sheets in a Drive folder).
/// Bound by V2 to <c>GoogleInspectionTemplateProvider</c> until native Drive exists.
/// </summary>
public interface IInspectionTemplateCatalog
{
    Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListTemplatesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>One template available for creating an inspection report.</summary>
public sealed record InspectionTemplateCatalogItem(
    string Name,
    string SpreadsheetId,
    string Url);
