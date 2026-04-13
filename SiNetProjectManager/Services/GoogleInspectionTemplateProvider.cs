using Google.Apis.Drive.v3;
using Google.Apis.Sheets.v4;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManager.Services;

/// <summary>
/// Google-based implementation of <see cref="IInspectionTemplateProvider"/>.
/// Uses Drive API to list sheets in a folder and Sheets API to parse template rows.
/// Reuses the existing <see cref="GoogleAuthService"/> and appsettings.json config.
/// </summary>
public sealed class GoogleInspectionTemplateProvider : IInspectionTemplateProvider
{
    private readonly GoogleAuthService _authService;

    private const string SpreadsheetMimeType = "application/vnd.google-apps.spreadsheet";

    /// <summary>
    /// Default range to read from each template.
    /// 2-column layout: Col A = empty (chapter row) or SectionCode (section row), Col B = text.
    /// First row is header (skipped).
    /// </summary>
    private const string DefaultRange = "A:B";

    public GoogleInspectionTemplateProvider(GoogleAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <inheritdoc />
    public async Task<List<InspectionTemplateItem>> GetAvailableTemplatesAsync(
        string folderId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        await _authService.EnsureAuthenticatedAsync(cancellationToken);

        var driveService = _authService.DriveService
            ?? throw new InvalidOperationException("Drive service not available after authentication.");

        var templates = new List<InspectionTemplateItem>();
        string? pageToken = null;

        do
        {
            var request = driveService.Files.List();

            // Strict filter: only Google Sheets, in the specified folder, not trashed
            request.Q = $"'{folderId}' in parents " +
                        $"and mimeType = '{SpreadsheetMimeType}' " +
                        $"and trashed = false";

            request.Fields = "nextPageToken, files(id, name, modifiedTime, webViewLink)";
            request.OrderBy = "name";
            request.PageSize = 100;
            request.PageToken = pageToken;

            // Support Shared Drives
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;

            var result = await request.ExecuteAsync(cancellationToken);

            if (result.Files != null)
            {
                foreach (var file in result.Files)
                {
                    templates.Add(new InspectionTemplateItem
                    {
                        SpreadsheetId = file.Id,
                        Name = file.Name,
                        Url = file.WebViewLink
                              ?? $"https://docs.google.com/spreadsheets/d/{file.Id}",
                        ModifiedTime = file.ModifiedTimeDateTimeOffset?.UtcDateTime
                    });
                }
            }

            pageToken = result.NextPageToken;
        }
        while (pageToken != null);

        return templates;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSyncRow>> ParseTemplateAsync(
        string spreadsheetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);

        await _authService.EnsureAuthenticatedAsync(cancellationToken);

        var sheetsService = _authService.SheetsService
            ?? throw new InvalidOperationException("Sheets service not available after authentication.");

        // Read the first sheet using the default range
        var request = sheetsService.Spreadsheets.Values.Get(spreadsheetId, DefaultRange);
        request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest
            .ValueRenderOptionEnum.FORMATTEDVALUE;

        var response = await request.ExecuteAsync(cancellationToken);

        if (response.Values == null || response.Values.Count < 2)
            return Array.Empty<TemplateSyncRow>();

        var rows = new List<TemplateSyncRow>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        string? currentChapterTitle = null;

        // Skip header row (index 0); parse data rows using 2-column rules:
        //   Chapter row: Column A empty AND Column B has text.
        //   Section row: Column A has a section code (e.g. "1.1") AND Column B has text.
        for (int i = 1; i < response.Values.Count; i++)
        {
            var row = response.Values[i];
            if (row.Count == 0)
                continue;

            var colA = GetCellValue(row, 0);
            var colB = GetCellValue(row, 1);

            // Skip rows with no text in column B
            if (string.IsNullOrWhiteSpace(colB))
                continue;

            if (string.IsNullOrWhiteSpace(colA))
            {
                // Chapter row: Col A empty, Col B = chapter title
                currentChapterTitle = colB;
                continue;
            }

            // Section row: Col A = section code, Col B = section title
            var sectionCode = colA;

            // Derive chapter number from section code prefix (e.g. "1.1" → 1)
            var dotIndex = sectionCode.IndexOf('.');
            var chapterPart = dotIndex > 0 ? sectionCode[..dotIndex] : sectionCode;
            if (!int.TryParse(chapterPart, out var chapterNumber))
                continue; // invalid section code format, skip

            // Duplicate section code detection: keep first occurrence
            if (!seenCodes.Add(sectionCode))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TemplateParser] Warning: duplicate SectionCode '{sectionCode}' at row {i + 1} — keeping first.");
                continue;
            }

            rows.Add(new TemplateSyncRow
            {
                RowNumber = i + 1, // 1-based sheet row number
                ChapterNumber = chapterNumber,
                ChapterTitle = currentChapterTitle,
                SectionCode = sectionCode,
                SectionTitle = colB
            });
        }

        return rows;
    }

    private static string? GetCellValue(IList<object> row, int index)
    {
        if (index >= row.Count) return null;
        var value = row[index]?.ToString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <inheritdoc />
    public async Task<TemplateScanResult> ScanAndParseTemplateAsync(
        string spreadsheetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);

        await _authService.EnsureAuthenticatedAsync(cancellationToken);

        var sheetsService = _authService.SheetsService
            ?? throw new InvalidOperationException("Sheets service not available after authentication.");

        // Read the full sheet (all columns, all rows)
        var spreadsheet = await sheetsService.Spreadsheets.Get(spreadsheetId)
            .ExecuteAsync(cancellationToken);
        var sheet = spreadsheet.Sheets[0];
        var sheetTitle = sheet.Properties.Title ?? "Sheet1";
        var totalRows = sheet.Properties.GridProperties?.RowCount ?? 1200;

        var request = sheetsService.Spreadsheets.Values.Get(
            spreadsheetId, $"'{sheetTitle}'!A1:Z{totalRows}");
        request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest
            .ValueRenderOptionEnum.FORMATTEDVALUE;
        var response = await request.ExecuteAsync(cancellationToken);

        if (response.Values == null || response.Values.Count == 0)
        {
            return new TemplateScanResult
            {
                SyncRows = [],
                AllTags = [],
                ValidationErrors = []
            };
        }

        // 1. Scan all tags
        var allTags = GoogleReportExportService.ScanAllTemplateTags(response.Values);

        // 2. Validate
        var validationErrors = TemplateTagValidator.Validate(allTags);

        // 3. Convert header tags to TemplateSyncRow (even if errors — caller decides)
        var syncRows = BuildSyncRowsFromTags(allTags);

        return new TemplateScanResult
        {
            SyncRows = syncRows,
            AllTags = allTags,
            ValidationErrors = validationErrors
        };
    }

    /// <summary>
    /// Converts header (definition) tags and general tags into <see cref="TemplateSyncRow"/> objects
    /// for the sync service.
    /// </summary>
    private static List<TemplateSyncRow> BuildSyncRowsFromTags(IReadOnlyList<TemplateScanTag> tags)
    {
        var rows = new List<TemplateSyncRow>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        int rowNumber = 0;

        // ── Numbered header tags → regular sections ──
        var headerTags = tags
            .Where(t => t.IsStatusTag && !t.IsGeneralTag)
            .OrderBy(t => t.Col)
            .ThenBy(t => t.Row);

        foreach (var tag in headerTags)
        {
            if (!seenCodes.Add(tag.SectionCode))
                continue; // already processed this code

            var dotIdx = tag.SectionCode.IndexOf('.');
            var chapterPart = dotIdx > 0 ? tag.SectionCode[..dotIdx] : tag.SectionCode;
            if (!int.TryParse(chapterPart, out var chapterNumber))
                continue;

            rowNumber++;
            rows.Add(new TemplateSyncRow
            {
                RowNumber = rowNumber,
                ChapterNumber = chapterNumber,
                ChapterTitle = tag.Title?.Trim(),
                SectionCode = tag.SectionCode,
                SectionTitle = $"<<{tag.SectionCode} {tag.Title} [{tag.DefaultText}]>>"
            });
        }

        // ── Fallback: numbered tags (note-input / legacy note) without a status tag ──
        // Sections whose status tag wasn't matched (e.g. invisible Unicode chars broke regex)
        // are rescued here using any other tag that carries the same section code.
        var fallbackTags = tags
            .Where(t => !t.IsGeneralTag && !t.IsStatusTag
                        && !string.IsNullOrEmpty(t.SectionCode)
                        && !seenCodes.Contains(t.SectionCode))
            .OrderBy(t => t.Col)
            .ThenBy(t => t.Row);

        foreach (var tag in fallbackTags)
        {
            if (!seenCodes.Add(tag.SectionCode))
                continue;

            var dotIdx = tag.SectionCode.IndexOf('.');
            var chapterPart = dotIdx > 0 ? tag.SectionCode[..dotIdx] : tag.SectionCode;
            if (!int.TryParse(chapterPart, out var chapterNumber))
                continue;

            var title = tag.Title?.Trim() ?? tag.SectionCode;
            rowNumber++;
            rows.Add(new TemplateSyncRow
            {
                RowNumber = rowNumber,
                ChapterNumber = chapterNumber,
                ChapterTitle = title,
                SectionCode = tag.SectionCode,
                SectionTitle = $"<<{tag.SectionCode} {title}>>"
            });
        }

        // ── General tags → Chapter 0 sections ──
        // Ordered by column ascending (A first), then row ascending (top first)
        var generalTags = tags
            .Where(t => t.IsGeneralTag && !string.IsNullOrWhiteSpace(t.GeneralTagLabel))
            .OrderBy(t => t.Col)
            .ThenBy(t => t.Row);

        var seenLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in generalTags)
        {
            var label = tag.GeneralTagLabel!;
            if (!seenLabels.Add(label))
                continue; // duplicate label

            rowNumber++;
            rows.Add(new TemplateSyncRow
            {
                RowNumber = rowNumber,
                ChapterNumber = 0,
                ChapterTitle = "נתונים כלליים",
                SectionCode = label,
                SectionTitle = label
            });
        }

        return rows;
    }

    private const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <inheritdoc />
    public async Task<string> GetFolderNameAsync(
        string folderId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        await _authService.EnsureAuthenticatedAsync(cancellationToken);

        var driveService = _authService.DriveService
            ?? throw new InvalidOperationException("Drive service not available after authentication.");

        var request = driveService.Files.Get(folderId);
        request.Fields = "id, name, mimeType";
        request.SupportsAllDrives = true;

        var file = await request.ExecuteAsync(cancellationToken);

        if (file.MimeType != FolderMimeType)
            throw new InvalidOperationException(
                $"The ID '{folderId}' does not point to a folder (mimeType: {file.MimeType}).");

        return file.Name;
    }
}
