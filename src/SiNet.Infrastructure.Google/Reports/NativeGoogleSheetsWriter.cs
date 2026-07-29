using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace SiNet.Infrastructure.Google.Reports;

/// <summary>Minimal Sheets write helpers for MasterPlan reports (ported from GoogleConnector).</summary>
public sealed class NativeGoogleSheetsWriter
{
    private readonly SheetsService _sheets;
    private readonly int _batchSize;
    private readonly int _batchDelayMs;

    public NativeGoogleSheetsWriter(SheetsService sheets, int batchSize = 1000, int batchDelayMs = 100)
    {
        _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        _batchSize = batchSize > 0 ? batchSize : 1000;
        _batchDelayMs = batchDelayMs >= 0 ? batchDelayMs : 100;
    }

    public static string QuoteSheetName(string sheetName)
        => "'" + sheetName.Replace("'", "''", StringComparison.Ordinal) + "'";

    public static string BuildRange(string sheetName, string cellRange)
        => $"{QuoteSheetName(sheetName)}!{cellRange}";

    public async Task ClearRangeAsync(string spreadsheetId, string range, CancellationToken cancellationToken = default)
    {
        var request = _sheets.Spreadsheets.Values.Clear(new ClearValuesRequest(), spreadsheetId, range);
        await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteHeadersAsync(
        string spreadsheetId,
        string range,
        IList<object> headers,
        CancellationToken cancellationToken = default)
    {
        var valueRange = new ValueRange { Values = new List<IList<object>> { headers } };
        var request = _sheets.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteValuesAsync(
        string spreadsheetId,
        string range,
        IList<IList<object>> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return;

        var valueRange = new ValueRange { Values = rows };
        var request = _sheets.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> WriteDataBatchedAsync(
        string spreadsheetId,
        string startRange,
        IList<IList<object?>> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return 0;

        var (sheetName, startRow) = ParseRange(startRange);
        var totalBatches = (int)Math.Ceiling(rows.Count / (double)_batchSize);
        var written = 0;

        for (var batchNum = 0; batchNum < totalBatches; batchNum++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var skip = batchNum * _batchSize;
            var batch = rows.Skip(skip).Take(_batchSize)
                .Select(r => (IList<object>)r.Select(c => (object?)c ?? string.Empty).Cast<object>().ToList())
                .ToList();
            if (batch.Count == 0)
                break;

            var batchStartRow = startRow + skip;
            await WriteValuesAsync(
                    spreadsheetId,
                    BuildRange(sheetName, $"A{batchStartRow}"),
                    batch,
                    cancellationToken)
                .ConfigureAwait(false);
            written += batch.Count;

            if (batchNum < totalBatches - 1 && _batchDelayMs > 0)
                await Task.Delay(_batchDelayMs, cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    public async Task<bool> EnsureSheetExistsAsync(
        string spreadsheetId,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        var info = await _sheets.Spreadsheets.Get(spreadsheetId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (info.Sheets?.Any(s => string.Equals(s.Properties?.Title, sheetName, StringComparison.Ordinal)) == true)
            return false;

        var request = new BatchUpdateSpreadsheetRequest
        {
            Requests =
            [
                new Request
                {
                    AddSheet = new AddSheetRequest
                    {
                        Properties = new SheetProperties
                        {
                            Title = sheetName,
                            // Keep the grid small: 50_000×30 ≈ 1.5M cells per tab and Google's
                            // spreadsheet cap is 10M cells — a handful of R03 employee tabs then fails
                            // addSheet with "exceeds 10000000 cells". Sheets grow automatically.
                            GridProperties = new GridProperties { RowCount = 200, ColumnCount = 10 },
                        },
                    },
                },
            ],
        };

        await _sheets.Spreadsheets.BatchUpdate(request, spreadsheetId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>Creates/overwrites the Parameters sheet used by R01 formula columns (HourPrice in B1).</summary>
    public async Task WriteParametersSheetAsync(
        string spreadsheetId,
        decimal hourPrice,
        CancellationToken cancellationToken = default)
    {
        await EnsureSheetExistsAsync(spreadsheetId, "Parameters", cancellationToken).ConfigureAwait(false);
        await ClearRangeAsync(
                spreadsheetId,
                BuildRange("Parameters", "A1:B10"),
                cancellationToken)
            .ConfigureAwait(false);
        await WriteValuesAsync(
                spreadsheetId,
                BuildRange("Parameters", "A1"),
                new List<IList<object>>
                {
                    new List<object> { "עלות שעה", hourPrice },
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task EnsureSheetNamedDataAsync(string spreadsheetId, CancellationToken cancellationToken = default)
    {
        var info = await _sheets.Spreadsheets.Get(spreadsheetId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var first = info.Sheets?.FirstOrDefault();
        if (first?.Properties?.Title is { } title
            && !string.Equals(title, "Data", StringComparison.Ordinal))
        {
            var sheetId = first.Properties.SheetId;
            var rename = new BatchUpdateSpreadsheetRequest
            {
                Requests =
                [
                    new Request
                    {
                        UpdateSheetProperties = new UpdateSheetPropertiesRequest
                        {
                            Properties = new SheetProperties { SheetId = sheetId, Title = "Data" },
                            Fields = "title",
                        },
                    },
                ],
            };
            await _sheets.Spreadsheets.BatchUpdate(rename, spreadsheetId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await EnsureSheetExistsAsync(spreadsheetId, "Data", cancellationToken).ConfigureAwait(false);
        }
    }

    private static (string SheetName, int StartRow) ParseRange(string range)
    {
        var bang = range.IndexOf('!', StringComparison.Ordinal);
        if (bang < 0)
            return (range.Trim('\''), 1);

        var sheet = range[..bang].Trim('\'');
        var cell = range[(bang + 1)..];
        var digits = new string(cell.Where(char.IsDigit).ToArray());
        return (sheet, int.TryParse(digits, out var row) ? row : 1);
    }
}
