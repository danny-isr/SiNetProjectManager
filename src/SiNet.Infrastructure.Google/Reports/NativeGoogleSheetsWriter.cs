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

    /// <summary>
    /// Creates (or recreates) a pivot sheet sourced from an existing data sheet.
    /// Ported from SiOffice.GoogleConnector <c>GoogleSheetsService.CreatePivotTableAsync</c>.
    /// </summary>
    /// <param name="spreadsheetId">Target spreadsheet.</param>
    /// <param name="pivotSheetName">Title of the pivot sheet to create.</param>
    /// <param name="sourceSheetName">Sheet that holds the source table (usually Data).</param>
    /// <param name="headerRow">1-based header row in the source sheet.</param>
    /// <param name="lastDataRow">1-based inclusive last data row (GridRange EndRowIndex exclusive).</param>
    /// <param name="lastColumnIndex">0-based last column index in the source range.</param>
    /// <param name="config">Rows / columns / values / filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PivotTableResult> CreatePivotTableAsync(
        string spreadsheetId,
        string pivotSheetName,
        string sourceSheetName,
        int headerRow,
        int lastDataRow,
        int lastColumnIndex,
        PivotTableConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pivotSheetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSheetName);
        ArgumentNullException.ThrowIfNull(config);

        var result = new PivotTableResult();

        try
        {
            var sourceSheetId = await GetSheetIdAsync(spreadsheetId, sourceSheetName, cancellationToken)
                .ConfigureAwait(false);
            if (sourceSheetId is null)
            {
                result.Errors.Add($"Source sheet '{sourceSheetName}' not found");
                return result;
            }

            result.LogEntries.Add($"Source SheetId: {sourceSheetId}");
            result.LogEntries.Add($"Source Sheet: '{sourceSheetName}'");

            var requests = new List<Request>();

            var existingPivotSheetId = await GetSheetIdAsync(spreadsheetId, pivotSheetName, cancellationToken)
                .ConfigureAwait(false);
            if (existingPivotSheetId is not null)
            {
                result.LogEntries.Add($"Deleting existing pivot sheet (SheetId: {existingPivotSheetId})");
                requests.Add(new Request
                {
                    DeleteSheet = new DeleteSheetRequest { SheetId = existingPivotSheetId },
                });
            }

            // Predictable sheet id (offset + hash) to anchor UpdateCells in the same batch.
            var newPivotSheetId = 1000 + Math.Abs(pivotSheetName.GetHashCode(StringComparison.Ordinal) % 1000);
            result.LogEntries.Add($"Creating pivot sheet: '{pivotSheetName}' (SheetId: {newPivotSheetId})");

            requests.Add(new Request
            {
                AddSheet = new AddSheetRequest
                {
                    Properties = new SheetProperties
                    {
                        SheetId = newPivotSheetId,
                        Title = pivotSheetName,
                        GridProperties = new GridProperties
                        {
                            RowCount = 1000,
                            ColumnCount = 50,
                        },
                    },
                },
            });

            var sourceRange = new GridRange
            {
                SheetId = sourceSheetId,
                StartRowIndex = headerRow - 1,
                EndRowIndex = lastDataRow,
                StartColumnIndex = 0,
                EndColumnIndex = lastColumnIndex + 1,
            };

            result.LogEntries.Add(
                $"Source Range: StartRow={sourceRange.StartRowIndex} (1-based: {headerRow}), " +
                $"EndRow={sourceRange.EndRowIndex} (1-based: {lastDataRow}), " +
                $"StartCol={sourceRange.StartColumnIndex}, EndCol={sourceRange.EndColumnIndex}");
            result.LogEntries.Add(
                $"Pivot Rows: [{string.Join(", ", config.Rows.Select(r => r.SourceColumnIndex))}]");
            result.LogEntries.Add(
                $"Pivot Values: [{string.Join(", ", config.Values.Select(v => $"{v.SourceColumnIndex}:{v.SummarizeFunction}"))}]");
            result.LogEntries.Add(
                $"Pivot Filters: [{string.Join(", ", config.Filters.Select(f => f.SourceColumnIndex))}]");

            var pivotTable = new PivotTable
            {
                Source = sourceRange,
                Rows = config.Rows.Select(r => new PivotGroup
                {
                    SourceColumnOffset = r.SourceColumnIndex,
                    ShowTotals = r.ShowTotals,
                    SortOrder = "ASCENDING",
                }).ToList(),
                Values = config.Values.Select(v => new PivotValue
                {
                    SourceColumnOffset = v.SourceColumnIndex,
                    SummarizeFunction = v.SummarizeFunction,
                    Name = v.DisplayName,
                }).ToList(),
                FilterSpecs = config.Filters.Count == 0
                    ? null
                    : config.Filters.Select(f => new PivotFilterSpec
                    {
                        FilterCriteria = new PivotFilterCriteria { VisibleByDefault = true },
                        ColumnOffsetIndex = f.SourceColumnIndex,
                    }).ToList(),
            };

            if (config.Columns.Count > 0)
            {
                pivotTable.Columns = config.Columns.Select(c => new PivotGroup
                {
                    SourceColumnOffset = c.SourceColumnIndex,
                    ShowTotals = c.ShowTotals,
                    SortOrder = "ASCENDING",
                }).ToList();
            }

            requests.Add(new Request
            {
                UpdateCells = new UpdateCellsRequest
                {
                    Rows =
                    [
                        new RowData
                        {
                            Values =
                            [
                                new CellData { PivotTable = pivotTable },
                            ],
                        },
                    ],
                    Start = new GridCoordinate
                    {
                        SheetId = newPivotSheetId,
                        RowIndex = 0,
                        ColumnIndex = 0,
                    },
                    Fields = "pivotTable",
                },
            });

            result.LogEntries.Add($"Executing BatchUpdate with {requests.Count} requests");
            await _sheets.Spreadsheets.BatchUpdate(
                    new BatchUpdateSpreadsheetRequest { Requests = requests },
                    spreadsheetId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            result.Success = true;
            result.PivotSheetId = newPivotSheetId;
            result.PivotSheetName = pivotSheetName;
            result.SourceSheetId = sourceSheetId;
        }
        catch (global::Google.GoogleApiException ex)
        {
            result.Errors.Add($"Google API error: {ex.Message}");
            result.LogEntries.Add($"EXCEPTION: {ex.Message}");
            if (ex.Error?.Errors is { } apiErrors)
            {
                foreach (var error in apiErrors)
                {
                    result.Errors.Add($"  - {error.Message}");
                    result.LogEntries.Add($"  API Error: {error.Message} (Reason: {error.Reason})");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add($"Error creating pivot table: {ex.Message}");
            result.LogEntries.Add($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }

        return result;
    }

    public async Task<int?> GetSheetIdAsync(
        string spreadsheetId,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        var info = await _sheets.Spreadsheets.Get(spreadsheetId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var sheet = info.Sheets?.FirstOrDefault(s =>
            string.Equals(s.Properties?.Title, sheetName, StringComparison.Ordinal));
        return sheet?.Properties?.SheetId;
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
