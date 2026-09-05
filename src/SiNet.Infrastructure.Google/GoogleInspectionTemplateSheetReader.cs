using Google.Apis.Sheets.v4;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Reads inspection template spreadsheets via the shared Google session.
/// Tag parsing / sync remain in the SQL template pipeline.
/// </summary>
public sealed class GoogleInspectionTemplateSheetReader(
    GmailClientProvider gmailClientProvider,
    IAppLogger? logger = null) : IInspectionTemplateSheetReader
{
    private readonly GmailClientProvider _gmail =
        gmailClientProvider ?? throw new ArgumentNullException(nameof(gmailClientProvider));
    private readonly IAppLogger? _logger = logger;

    public async Task<InspectionTemplateSheetReadResult> ReadFirstSheetAsync(
        string spreadsheetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spreadsheetId))
            return InspectionTemplateSheetReadResult.Fail("חסר מזהה תבנית Google Sheets.");

        var sheets = await _gmail.TryGetSheetsServiceAsync(cancellationToken).ConfigureAwait(false);
        if (sheets is null)
        {
            return InspectionTemplateSheetReadResult.Fail(
                "אין חיבור Google Sheets — התחבר לחשבון Google ונסה שוב.");
        }

        try
        {
            var spreadsheet = await sheets.Spreadsheets.Get(spreadsheetId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            if (spreadsheet.Sheets is null || spreadsheet.Sheets.Count == 0)
                return InspectionTemplateSheetReadResult.Fail("התבנית ריקה — לא נמצא גיליון.");

            var sheet = spreadsheet.Sheets[0];
            var sheetTitle = sheet.Properties?.Title ?? "Sheet1";
            var totalRows = sheet.Properties?.GridProperties?.RowCount ?? 1200;

            var request = sheets.Spreadsheets.Values.Get(
                spreadsheetId, $"'{sheetTitle}'!A1:Z{totalRows}");
            request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest
                .ValueRenderOptionEnum.FORMATTEDVALUE;
            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            if (response.Values is null || response.Values.Count == 0)
                return InspectionTemplateSheetReadResult.Ok([]);

            IReadOnlyList<IReadOnlyList<object?>> rows = response.Values
                .Select(r => (IReadOnlyList<object?>)(r?.Cast<object?>().ToList() ?? []))
                .ToList();
            return InspectionTemplateSheetReadResult.Ok(rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.Error("[GoogleInspectionTemplateSheetReader] Failed to read template sheet", ex);
            return InspectionTemplateSheetReadResult.Fail($"שגיאה בקריאת תבנית Google: {ex.Message}");
        }
    }
}
