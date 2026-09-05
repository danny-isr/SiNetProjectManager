namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// Builds a pre-send Inspection → planner email draft without sending (certification-safe).
/// </summary>
public interface IInspectionReportComposeDraftService
{
    Task<InspectionReportComposeDraft?> BuildDraftAsync(
        int reportId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Operator-visible compose fields for Report → planner. Never implies outbound send succeeded.
/// </summary>
public sealed record InspectionReportComposeDraft(
    int ReportId,
    int ReportNumber,
    int ProjectId,
    string Subject,
    string Body,
    IReadOnlyList<string> ToRecipients,
    string? SentSpreadsheetId,
    string? SentSpreadsheetUrl,
    string? WarningMessage)
{
    public bool HasArtifact =>
        !string.IsNullOrWhiteSpace(SentSpreadsheetId)
        || !string.IsNullOrWhiteSpace(SentSpreadsheetUrl);

    public bool HasRecipients => ToRecipients.Count > 0;
}
