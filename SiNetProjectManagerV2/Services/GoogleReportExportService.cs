using System.Text.RegularExpressions;
using Google.Apis.Drive.v3;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;
using static SiNetSQL.Services.InspectionSync.RichTextCodec;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using SheetsColor = Google.Apis.Sheets.v4.Data.Color;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Google Sheets implementation of <see cref="IReportExportService"/>.
/// Copies a template spreadsheet and injects inspection data via BatchUpdate:
/// <list type="number">
///   <item>Global tag replacement (<c>&lt;&lt;FieldName&gt;&gt;</c>).</item>
///   <item>Tag-based cell scanning: <c>&lt;&lt;X.Y Title [...]&gt;&gt;</c> → aggregated status, <c>&lt;&lt;X.Y Title&gt;&gt;</c> → note text.</item>
///   <item>"Strongest status" aggregation: Failed &gt; RecurringFailed &gt; Passed &gt; NotApplicable — injected as configurable Hebrew labels with bold + background color.</item>
///   <item>Row cloning for sections with multiple notes (bottom-up to avoid index shifting).</item>
///   <item>Rich text injection with <see cref="TextFormatRun"/> conversion.</item>
/// </list>
/// </summary>
public sealed class GoogleReportExportService : IReportExportService
{
    private readonly GoogleAuthService _authService;
    private readonly IDbContextFactory<SiNetSQLDbContext> _contextFactory;
    private readonly ILogger<GoogleReportExportService>? _logger;

    private const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>
    /// Google Drive folder ID for storing exported reports.
    /// When set, exported files are placed in a project-specific sub-folder hierarchy.
    /// </summary>
    public string? ReportsFolderId { get; set; }

    // RGB values matching the app's UI color scheme
    private static readonly SheetsColor RedColor = new() { Red = 0.827f, Green = 0.184f, Blue = 0.184f };
    private static readonly SheetsColor BlueColor = new() { Red = 0.098f, Green = 0.463f, Blue = 0.824f };
    private static readonly SheetsColor GreenColor = new() { Red = 0.180f, Green = 0.490f, Blue = 0.196f };
    private static readonly SheetsColor GrayColor = new() { Red = 0.5f, Green = 0.5f, Blue = 0.5f };
    private static readonly SheetsColor BlackColor = new() { Red = 0f, Green = 0f, Blue = 0f };

    public GoogleReportExportService(
        GoogleAuthService authService,
        IDbContextFactory<SiNetSQLDbContext> contextFactory,
        ILogger<GoogleReportExportService>? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReportExportResult> ExportReportAsync(
        int reportId,
        string templateSpreadsheetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSpreadsheetId);

        var warnings = new List<string>();
        int tagsReplaced = 0;
        int rowsInjected = 0;

        try
        {
            // ── 1. Load data from DB ──
            _logger?.LogInformation("[Export] Loading report {ReportId} data from database.", reportId);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var report = await context.InspectionReports
                .AsNoTracking()
                .Include(r => r.Project)
                    .ThenInclude(p => p!.Place)
                .Include(r => r.Project)
                    .ThenInclude(p => p!.OnerProject)
                .Include(r => r.Project)
                    .ThenInclude(p => p!.Contacts)
                .Include(r => r.Project)
                    .ThenInclude(p => p!.ProjectPlanners)
                        .ThenInclude(pp => pp.Contacts)
                .Include(r => r.Inspector)
                .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken)
                ?? throw new InvalidOperationException($"Report {reportId} not found.");

            var notes = await context.InspectionNotes
                .AsNoTracking()
                .Include(n => n.Section)
                    .ThenInclude(s => s.Chapter)
                        .ThenInclude(c => c.ChapterName)
                .Include(n => n.Section)
                    .ThenInclude(s => s.SectionName)
                .Include(n => n.Attachments)
                .Where(n => n.ReportId == reportId)
                .OrderBy(n => n.Section.Chapter.ChapterNumber)
                .ThenBy(n => n.Section.SectionCode)
                .ThenBy(n => n.NoteSubIndex)
                .ToListAsync(cancellationToken);

            _logger?.LogInformation("[Export] Raw notes loaded: {Count}. Report SeriesId={SeriesId}, ProjectId={ProjectId}.",
                notes.Count, report.SeriesId, report.ProjectId);
            System.Diagnostics.Debug.WriteLine($"[Export] === RAW NOTES LOADED: {notes.Count}, SeriesId={report.SeriesId}, ProjectId={report.ProjectId} ===");

            // Debug: log each note's key fields to diagnose empty export
            foreach (var n in notes)
            {
                var extractedKey = ExtractSectionCode(n.NoteSubIndex, n.Section.FullCode);
                _logger?.LogDebug(
                    "[Export] Note {NoteId}: FullCode='{FullCode}', SubIndex='{SubIndex}', ExtractedKey='{Key}', Status='{Status}', TextLen={Len}, ChapterNull={ChNull}",
                    n.NoteId, n.Section.FullCode, n.NoteSubIndex, extractedKey, n.NoteStatus, n.NoteText?.Length ?? 0, n.Section.Chapter == null);
                System.Diagnostics.Debug.WriteLine(
                    $"[Export] Note {n.NoteId}: FullCode='{n.Section.FullCode}', SubIndex='{n.NoteSubIndex}', " +
                    $"ExtractedKey='{extractedKey}', Status='{n.NoteStatus}', TextLen={n.NoteText?.Length ?? 0}, ChapterNull={n.Section.Chapter == null}");
            }

            var withSubIndex = notes
                .Where(n => !string.IsNullOrEmpty(n.NoteSubIndex))
                .ToList();

            // Stability audit: explicitly log every note that the export drops
            // because it has no NoteSubIndex. These notes are visible in the
            // VM tree but silently disappear from the exported report, which
            // matched the user's symptom of "different sections missing in
            // each export".
            var skippedNoSubIndex = notes
                .Where(n => string.IsNullOrEmpty(n.NoteSubIndex))
                .ToList();
            if (skippedNoSubIndex.Count > 0)
            {
                _logger?.LogWarning(
                    "[Export] SKIPPED {Count} notes with empty NoteSubIndex: [{Ids}]",
                    skippedNoSubIndex.Count,
                    string.Join(", ", skippedNoSubIndex.Select(n => $"NoteId={n.NoteId}/Section={n.Section?.FullCode}")));
                System.Diagnostics.Debug.WriteLine(
                    $"[Export] SKIPPED {skippedNoSubIndex.Count} notes with empty NoteSubIndex: " +
                    string.Join(", ", skippedNoSubIndex.Select(n => $"NoteId={n.NoteId}/Section={n.Section?.FullCode}")));
            }

            _logger?.LogInformation(
                "[Export] Filter: {Total} total → {WithSub} with NoteSubIndex.",
                notes.Count, withSubIndex.Count);
            System.Diagnostics.Debug.WriteLine($"[Export] FILTER: {notes.Count} total → {withSubIndex.Count} with NoteSubIndex");

            // Group by numeric code prefix (e.g. "1.1") — template tags use <<1.1 Title [...]>> / <<1.1 Title>>
            // Stability audit: detect duplicate Section.FullCode keys before
            // collapsing into a Dictionary, otherwise GroupBy+ToDictionary
            // would silently drop one of the colliding sections.
            var groupedBySection = withSubIndex.GroupBy(n => n.Section.FullCode).ToList();
            var duplicateKeys = groupedBySection
                .GroupBy(g => g.Key)
                .Where(gg => gg.Count() > 1)
                .Select(gg => gg.Key)
                .ToList();
            if (duplicateKeys.Count > 0)
            {
                _logger?.LogWarning(
                    "[Export] Duplicate Section.FullCode keys detected (would overwrite): [{Keys}]",
                    string.Join(", ", duplicateKeys));
                System.Diagnostics.Debug.WriteLine(
                    $"[Export] DUPLICATE SECTION KEYS: [{string.Join(", ", duplicateKeys)}]");
            }
            var notesBySection = groupedBySection
                .ToDictionary(g => g.Key, g => g.ToList());

            _logger?.LogInformation(
                "[Export] Loaded {NoteCount} notes across {SectionCount} sections. Keys: [{Keys}]",
                withSubIndex.Count, notesBySection.Count, string.Join(", ", notesBySection.Keys));
            System.Diagnostics.Debug.WriteLine($"[Export] SECTIONS: {notesBySection.Count} keys=[{string.Join(", ", notesBySection.Keys)}]");

            // ── 1b. Load status display labels from settings ──
            var statusLabels = await LoadStatusLabelsAsync(cancellationToken);

            // ── 2. Authenticate with Google ──
            await _authService.EnsureAuthenticatedAsync(cancellationToken);

            var driveService = _authService.DriveService
                ?? throw new InvalidOperationException("Drive service not available after authentication.");
            var sheetsService = _authService.SheetsService
                ?? throw new InvalidOperationException("Sheets service not available after authentication.");

            // ── 3. Copy template to new spreadsheet (into Google Drive Reports folder) ──
            var project = report.Project;
            var projectNumber = project?.Number?.ToString("F0") ?? "0";
            var location = project?.Place?.Title ?? "Unknown";
            var dateStr = report.InspectionDate.ToString("yyyyMMdd");
            var reportTitle = $"{projectNumber}_{location}_{dateStr}";

            // Build Google Drive folder hierarchy: Reports / [Location] / [ParentProject (optional)] / [ProjectName]
            string? targetFolderId = null;
            if (!string.IsNullOrWhiteSpace(ReportsFolderId))
            {
                try
                {
                    targetFolderId = ReportsFolderId;

                    // Level 1: Location folder
                    if (!string.IsNullOrWhiteSpace(location) && location != "Unknown")
                    {
                        targetFolderId = await FindOrCreateDriveFolderAsync(
                            driveService, targetFolderId, location, cancellationToken);
                    }

                    // Level 2 (optional): Parent project folder
                    if (project?.OnerProject != null)
                    {
                        var parentName = project.OnerProject.Title ?? $"Project_{project.OnerProjectId}";
                        targetFolderId = await FindOrCreateDriveFolderAsync(
                            driveService, targetFolderId, parentName, cancellationToken);
                    }

                    // Level 3: Project folder
                    if (project != null)
                    {
                        var projectName = project.Title ?? $"Project_{project.Id}";
                        targetFolderId = await FindOrCreateDriveFolderAsync(
                            driveService, targetFolderId, projectName, cancellationToken);
                    }
                }
                catch (Exception folderEx)
                {
                    _logger?.LogWarning(folderEx, "[Export] Failed to create Drive folder hierarchy — file will be placed in root.");
                    warnings.Add($"Drive folder creation failed: {folderEx.Message}");
                    targetFolderId = null;
                }
            }

            _logger?.LogInformation("[Export] Copying template {TemplateId} → '{Title}'.", templateSpreadsheetId, reportTitle);

            var copyMeta = new DriveFile { Name = reportTitle };
            if (!string.IsNullOrWhiteSpace(targetFolderId))
                copyMeta.Parents = [targetFolderId];

            var copyRequest = driveService.Files.Copy(copyMeta, templateSpreadsheetId);
            copyRequest.SupportsAllDrives = true;

            var copiedFile = await copyRequest.ExecuteAsync(cancellationToken);
            var destinationId = copiedFile.Id;
            var destinationUrl = $"https://docs.google.com/spreadsheets/d/{destinationId}";

            _logger?.LogInformation("[Export] Template copied → {DestinationId}.", destinationId);

            // ── 4. Read destination sheet ──
            // 4a. Metadata: sheetId, title, column count (no grid data — keeps response small)
            var spreadsheet = await sheetsService.Spreadsheets.Get(destinationId)
                .ExecuteAsync(cancellationToken);
            var sheet = spreadsheet.Sheets[0];
            var sheetId = sheet.Properties.SheetId ?? 0;
            var sheetTitle = sheet.Properties.Title ?? "Sheet1";
            var totalSheetColumns = sheet.Properties.GridProperties?.ColumnCount ?? 26;
            var totalSheetRows = sheet.Properties.GridProperties?.RowCount ?? 1200;

            // 4b. Values.Get with explicit range — returns ALL cell text reliably
            var valuesResponse = await sheetsService.Spreadsheets.Values
                .Get(destinationId, $"'{sheetTitle}'!A1:Z{totalSheetRows}")
                .ExecuteAsync(cancellationToken);
            var allRows = valuesResponse.Values;

            if (allRows == null || allRows.Count == 0)
            {
                warnings.Add("Template sheet contains no data rows.");
                return BuildResult(destinationId, destinationUrl, tagsReplaced, rowsInjected, report, warnings, true);
            }

            _logger?.LogDebug("[Export] Values.Get: {RowCount} rows from sheet '{Title}'.", allRows.Count, sheetTitle);
            System.Diagnostics.Debug.WriteLine($"[Export] === VALUES.GET: {allRows.Count} rows, {totalSheetColumns} cols, sheet='{sheetTitle}' ===");

            // ── 5. Build tag map ──
            var tagMap = BuildTagMap(report.Project, report);

            // ── 5b. Overlay Chapter 0 (general data) note values onto tag map ──
            // Manual overrides replace automatic DB values; regular fields add user-entered text.
            var chapter0Notes = notes
                .Where(n => n.Section?.Chapter?.ChapterNumber == 0
                         && n.NoteSubIndex != null && !n.NoteSubIndex.Contains('.'))
                .ToList();

            foreach (var note in chapter0Notes)
            {
                var label = note.Section.SectionName?.Name;
                if (string.IsNullOrWhiteSpace(label)) continue;

                var isAutoField = GeneralFieldTreeItem.AutoFieldLabels.Contains(label);

                if (isAutoField && string.Equals(note.NoteStatus, "Manual", StringComparison.Ordinal))
                {
                    // Auto field with manual override — user's text replaces DB value
                    tagMap[label] = note.NoteText ?? "";
                }
                else if (!isAutoField)
                {
                    // Regular field — always use user-entered text from tree
                    tagMap[label] = note.NoteText ?? "";
                }
                // else: auto field, not manual → keep BuildTagMap value (DB property)
            }

            _logger?.LogInformation("[Export] Overlaid {Count} Chapter 0 notes onto tag map ({Auto} auto, {Regular} regular).",
                chapter0Notes.Count,
                chapter0Notes.Count(n => GeneralFieldTreeItem.AutoFieldLabels.Contains(n.Section.SectionName?.Name ?? "")),
                chapter0Notes.Count(n => !GeneralFieldTreeItem.AutoFieldLabels.Contains(n.Section.SectionName?.Name ?? "")));

            // ── 6. Build batch requests ──
            var requests = new List<Request>();

            // 6.0. Locate the mandatory <<תגובת המתכנן>> column tag and replace it with
            //      a user-friendly header. The column it sits in becomes the authoritative
            //      planner-response column for this exported report — no fixed offset.
            var allScannedTags = ScanAllTemplateTags(allRows);
            var plannerResponseTag = allScannedTags
                .FirstOrDefault(t => t.IsPlannerResponseColumnTag);
            int plannerResponseColumnIndex = plannerResponseTag?.Col ?? -1;

            if (plannerResponseTag is null)
            {
                _logger?.LogWarning(
                    "[Export] Planner-response column tag <<{Tag}>> not found in template. " +
                    "ReportId={ReportId}, ReportNumber={ReportNumber}, Operation=DesignerCommentsColumn, " +
                    "Reason=Template tag missing — falling back to noteCol+2 for backward compatibility.",
                    TemplateTagValidator.PlannerResponseTagLabel,
                    reportId, report.ReportNumber);
                warnings.Add(TemplateTagValidator.PlannerResponseTagMissingMessage);
            }
            else
            {
                _logger?.LogInformation(
                    "[Export] Planner-response column resolved from tag. ReportId={ReportId}, " +
                    "Tag=<<{Tag}>>, Row={Row}, Col={Col}, Operation=DesignerCommentsColumn",
                    reportId, TemplateTagValidator.PlannerResponseTagLabel,
                    plannerResponseTag.Row, plannerResponseTag.Col);

                // Replace the tag literal in the destination sheet with a plain header
                // so the user never sees the raw <<...>> placeholder.
                requests.Add(new Request
                {
                    FindReplace = new FindReplaceRequest
                    {
                        Find = $"<<{TemplateTagValidator.PlannerResponseTagLabel}>>",
                        Replacement = TemplateTagValidator.PlannerResponseTagLabel,
                        AllSheets = true,
                        MatchCase = false,
                        MatchEntireCell = false
                    }
                });
            }

            // 6a. Global <<tag>> replacement via FindReplace
            foreach (var (tag, value) in tagMap)
            {
                requests.Add(new Request
                {
                    FindReplace = new FindReplaceRequest
                    {
                        Find = $"<<{tag}>>",
                        Replacement = value,
                        AllSheets = true,
                        MatchCase = false,
                        MatchEntireCell = false
                    }
                });
                tagsReplaced++;
            }

            // 6b. Single-pass scan of ALL cells for <<X.Y Title [...]>> status and <<X.Y Title>> note tags
            var sectionTags = ScanTemplateTags(allRows, notesBySection);

            _logger?.LogInformation("[Export] Found {Count} sections with tags.", sectionTags.Count);
            System.Diagnostics.Debug.WriteLine($"[Export] === TAG SCAN RESULT: {sectionTags.Count} matched sections ===");

            // Log each matched section's tag details for debugging
            foreach (var (code, info) in sectionTags)
            {
                var statusLoc = info.StatusCell.HasValue ? $"R{info.StatusCell.Value.Row}C{info.StatusCell.Value.Col}" : "NONE";
                var noteLoc = info.NoteCell.HasValue ? $"R{info.NoteCell.Value.Row}C{info.NoteCell.Value.Col}" : "NONE";
                _logger?.LogInformation(
                    "[Export] Section '{Code}': StatusCell={Status}, NoteCell={Note}, Notes={NoteCount}",
                    code, statusLoc, noteLoc, info.Notes.Count);
                System.Diagnostics.Debug.WriteLine($"[Export]   Section '{code}': StatusCell={statusLoc}, NoteCell={noteLoc}, Notes={info.Notes.Count}");
            }

            if (sectionTags.Count == 0)
            {
                warnings.Add("No matching section tags found in the template. " +
                    $"NotesBySection keys: [{string.Join(", ", notesBySection.Keys)}]. " +
                    "Verify that the template contains <<X.Y Title [...]>> and <<X.Y Title>> tags matching these codes.");
            }

            // ── Export completeness validation: compare DB sections vs template tags ──
            // Operation=GoogleReportExportValidation. Non-blocking — log warnings only.
            var templateSectionCodes = allScannedTags
                .Where(t => !t.IsGeneralTag && !string.IsNullOrEmpty(t.SectionCode))
                .Select(t => t.SectionCode)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var code in notesBySection.Keys)
            {
                if (!sectionTags.ContainsKey(code))
                {
                    var sectionTitle = notesBySection[code]
                        .FirstOrDefault()?.Section?.SectionName?.Name;
                    _logger?.LogWarning(
                        "[Export] Section in report not exported. ReportId={ReportId}, " +
                        "ReportNumber={ReportNumber}, SectionNumber={SectionNumber}, " +
                        "SectionTitle={SectionTitle}, Operation=GoogleReportExportValidation, " +
                        "Reason={Reason}",
                        reportId, report.ReportNumber, code, sectionTitle ?? "<unknown>",
                        templateSectionCodes.Contains(code)
                            ? "Template tag present but no NoteCell/StatusCell mapping was built"
                            : "Unknown export mapping failure");
                    warnings.Add(
                        $"סעיף {code} ({sectionTitle ?? string.Empty}) לא יוצא לדוח Google.");
                }
            }

            // 6c. Inject aggregated status LABEL into <<X.Y Title [...]>> cells (bold + background color)
            foreach (var (code, info) in sectionTags)
            {
                if (info.StatusCell is not { } statusCell)
                    continue;

                var statusKey = ComputeAggregatedStatusKey(info.Notes);
                if (statusKey == null)
                {
                    // Clear the status tag text even when no meaningful status can be determined
                    requests.Add(new Request
                    {
                        UpdateCells = new UpdateCellsRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = statusCell.Row,
                                EndRowIndex = statusCell.Row + 1,
                                StartColumnIndex = statusCell.Col,
                                EndColumnIndex = statusCell.Col + 1
                            },
                            Rows = [new RowData { Values = [new CellData
                            {
                                UserEnteredValue = new ExtendedValue { StringValue = "" }
                            }] }],
                            Fields = "userEnteredValue"
                        }
                    });
                    continue;
                }

                var statusLabel = statusLabels.TryGetValue(statusKey, out var lbl) ? lbl : statusKey;
                var bgColor = GetStatusBackgroundColor(statusKey);

                _logger?.LogDebug(
                    "[Export] Section '{Code}': StatusKey={Key}, Label='{Label}' → R{Row}C{Col}.",
                    code, statusKey, statusLabel, statusCell.Row, statusCell.Col);

                requests.Add(new Request
                {
                    UpdateCells = new UpdateCellsRequest
                    {
                        Range = new GridRange
                        {
                            SheetId = sheetId,
                            StartRowIndex = statusCell.Row,
                            EndRowIndex = statusCell.Row + 1,
                            StartColumnIndex = statusCell.Col,
                            EndColumnIndex = statusCell.Col + 1
                        },
                        Rows =
                        [
                            new RowData
                            {
                                Values =
                                [
                                    new CellData
                                    {
                                        UserEnteredValue = new ExtendedValue { StringValue = statusLabel },
                                        UserEnteredFormat = new CellFormat
                                        {
                                            BackgroundColor = bgColor,
                                            TextFormat = new TextFormat { Bold = true }
                                        }
                                    }
                                ]
                            }
                        ],
                        Fields = "userEnteredValue,userEnteredFormat.backgroundColor,userEnteredFormat.textFormat.bold"
                    }
                });
                tagsReplaced++;
            }

            // 6d. Inject note text into <<X.Y Title>> cells (bottom-up to avoid index shifting)
            var noteCellMap = new List<ExportedNoteCellMap>();
            foreach (var (code, info) in sectionTags.OrderByDescending(kv => kv.Value.NoteCell?.Row ?? -1))
            {
                if (info.NoteCell is not { } noteCell || info.Notes.Count == 0)
                    continue;

                // Only inject notes that have actual text content
                var sectionNotes = info.Notes
                    .Where(n => !string.IsNullOrWhiteSpace(n.NoteText))
                    .ToList();
                if (sectionNotes.Count == 0)
                {
                    // Clear the tag cell even when no notes have text
                    requests.Add(new Request
                    {
                        UpdateCells = new UpdateCellsRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = noteCell.Row,
                                EndRowIndex = noteCell.Row + 1,
                                StartColumnIndex = noteCell.Col,
                                EndColumnIndex = noteCell.Col + 1
                            },
                            Rows = [new RowData { Values = [new CellData
                            {
                                UserEnteredValue = new ExtendedValue { StringValue = "" }
                            }] }],
                            Fields = "userEnteredValue"
                        }
                    });
                    continue;
                }

                var totalColumns = totalSheetColumns;

                // Insert extra rows below the note tag row if multiple notes
                if (sectionNotes.Count > 1)
                {
                    requests.Add(new Request
                    {
                        InsertDimension = new InsertDimensionRequest
                        {
                            Range = new DimensionRange
                            {
                                SheetId = sheetId,
                                Dimension = "ROWS",
                                StartIndex = noteCell.Row + 1,
                                EndIndex = noteCell.Row + sectionNotes.Count
                            },
                            InheritFromBefore = true
                        }
                    });

                    // Copy base row formatting to new rows
                    requests.Add(new Request
                    {
                        CopyPaste = new CopyPasteRequest
                        {
                            Source = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = noteCell.Row,
                                EndRowIndex = noteCell.Row + 1,
                                StartColumnIndex = 0,
                                EndColumnIndex = totalColumns
                            },
                            Destination = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = noteCell.Row + 1,
                                EndRowIndex = noteCell.Row + sectionNotes.Count,
                                StartColumnIndex = 0,
                                EndColumnIndex = totalColumns
                            },
                            PasteType = "PASTE_FORMAT"
                        }
                    });
                }

                // Inject each note's text into the note column (RTL aligned)
                for (int i = 0; i < sectionNotes.Count; i++)
                {
                    int targetRow = noteCell.Row + i;
                    var note = sectionNotes[i];

                    // Rich text into the note tag cell column
                    var (plainText, runs) = RichTextCodec.Parse(note.NoteText);

                    // RecurringFailed: override all formatting → entire text Bold+Red
                    if (string.Equals(note.NoteStatus, InspectionStatusKeys.RecurringFailed, StringComparison.Ordinal)
                        && !string.IsNullOrEmpty(plainText))
                    {
                        runs =
                        [
                            new RichTextRun
                            {
                                StartIndex = 0,
                                Length = plainText.Length,
                                Bold = true,
                                Color = RichTextColor.Red
                            }
                        ];
                    }

                    var googleRuns = ToGoogleTextFormatRuns(plainText, runs);

                    var noteCellData = new CellData
                    {
                        UserEnteredValue = new ExtendedValue { StringValue = plainText },
                        UserEnteredFormat = new CellFormat
                        {
                            HorizontalAlignment = "RIGHT",
                            TextDirection = "RIGHT_TO_LEFT"
                        }
                    };
                    if (googleRuns != null)
                        noteCellData.TextFormatRuns = googleRuns;

                    var noteFields = googleRuns != null
                        ? "userEnteredValue,textFormatRuns,userEnteredFormat.horizontalAlignment,userEnteredFormat.textDirection"
                        : "userEnteredValue,userEnteredFormat.horizontalAlignment,userEnteredFormat.textDirection";

                    requests.Add(new Request
                    {
                        UpdateCells = new UpdateCellsRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = targetRow,
                                EndRowIndex = targetRow + 1,
                                StartColumnIndex = noteCell.Col,
                                EndColumnIndex = noteCell.Col + 1
                            },
                            Rows = [new RowData { Values = [noteCellData] }],
                            Fields = noteFields
                        }
                    });

                    // Inject NoteSubIndex (e.g. "5.4.1") one column to the left of the note tag
                    // (visual right in RTL) with LEFT alignment so it sits flush against the note text.
                    int numCol = noteCell.Col - 1;
                    if (numCol >= 0 && !string.IsNullOrWhiteSpace(note.NoteSubIndex))
                    {
                        requests.Add(new Request
                        {
                            UpdateCells = new UpdateCellsRequest
                            {
                                Range = new GridRange
                                {
                                    SheetId = sheetId,
                                    StartRowIndex = targetRow,
                                    EndRowIndex = targetRow + 1,
                                    StartColumnIndex = numCol,
                                    EndColumnIndex = numCol + 1
                                },
                                Rows = [new RowData { Values = [new CellData
                                {
                                    UserEnteredValue = new ExtendedValue { StringValue = note.NoteSubIndex },
                                    UserEnteredFormat = new CellFormat
                                    {
                                        HorizontalAlignment = "LEFT"
                                    }
                                }] }],
                                Fields = "userEnteredValue,userEnteredFormat.horizontalAlignment"
                            }
                        });
                    }

                    rowsInjected++;

                    noteCellMap.Add(new ExportedNoteCellMap
                    {
                        NoteId = note.NoteId,
                        SectionCode = code,
                        NoteSubIndex = note.NoteSubIndex,
                        SheetName = sheetTitle,
                        ExportedRowIndex = targetRow,
                        ExportedNoteColumnIndex = noteCell.Col,
                        // Planner-response column is determined exclusively by the mandatory
                        // <<תגובת המתכנן>> template tag. We deliberately do NOT guess a
                        // fallback column (e.g. noteCol+2): the import side will log a
                        // warning and skip the note rather than read the wrong column.
                        PlannerResponseColumnIndex = plannerResponseColumnIndex
                    });
                }

                // Per-note ancillary tag substitution (PlannerResponse, OurResponse,
                // ACC links, screenshot). Only applied for single-note sections to keep
                // multi-note row insertion logic unchanged. Tags use the section code,
                // e.g. <<3.1 PlannerResponse>>.
                if (sectionNotes.Count == 1)
                {
                    var n = sectionNotes[0];
                    AddTagFindReplace(requests, sheetId, $"<<{code} PlannerResponse>>", n.PlannerResponseText);
                    AddTagFindReplace(requests, sheetId, $"<<{code} OurResponse>>", n.OurResponseToPlanner);
                    AddTagFindReplace(requests, sheetId, $"<<{code} AccIssueUrl>>", n.AccIssueUrl);
                    AddTagFindReplace(requests, sheetId, $"<<{code} AccMarkupUrl>>", n.AccMarkupUrl ?? n.AccMarkupLink);

                    var screenshot = n.Attachments?
                        .FirstOrDefault(a => a.AttachmentType == InspectionNoteAttachmentType.Screenshot
                            && !string.IsNullOrWhiteSpace(a.GoogleDriveUrl));

                    AddTagFindReplace(requests, sheetId, $"<<{code} ScreenshotUrl>>", screenshot?.GoogleDriveUrl);
                    // =IMAGE() requires a directly-served image URL; not all Drive URLs work, so we emit
                    // the formula and leave Sheets to display the image when supported.
                    var imageFormula = string.IsNullOrWhiteSpace(screenshot?.GoogleDriveUrl)
                        ? null
                        : $"=IMAGE(\"{screenshot!.GoogleDriveUrl}\")";
                    AddTagFindReplace(requests, sheetId, $"<<{code} ScreenshotImage>>", imageFormula);
                }
            }

            // ── 7. Execute batch update ──
            if (requests.Count > 0)
            {
                _logger?.LogInformation("[Export] Executing batch update with {RequestCount} requests.", requests.Count);
                System.Diagnostics.Debug.WriteLine($"[Export] === EXECUTING BATCH UPDATE: {requests.Count} requests (FindReplace={tagsReplaced}, RowsInjected={rowsInjected}) ===");

                var batchRequest = new BatchUpdateSpreadsheetRequest { Requests = requests };
                await sheetsService.Spreadsheets.BatchUpdate(batchRequest, destinationId)
                    .ExecuteAsync(cancellationToken);
            }

            _logger?.LogInformation(
                "[Export] Export complete. Tags={Tags}, Rows={Rows}, Warnings={Warnings}, NoteCellMapCount={MapCount}.",
                tagsReplaced, rowsInjected, warnings.Count, noteCellMap.Count);

            // ── 7b. Apply planner-only edit permissions ──
            // Share the sheet with the planner email (if any) and protect everything
            // except the planner-response cells listed in noteCellMap.
            try
            {
                var designerEmail = ResolvePlannerEmail(report);
                await ApplyPlannerResponsePermissionsAsync(
                    sheetsService,
                    driveService,
                    templateSpreadsheetId,
                    destinationId,
                    spreadsheet,
                    noteCellMap,
                    report,
                    designerEmail,
                    warnings,
                    cancellationToken);
            }
            catch (Exception permEx)
            {
                _logger?.LogWarning(permEx,
                    "[Export] Operation=PlannerResponsePermissions ReportId={ReportId} ReportNumber={ReportNumber} " +
                    "SpreadsheetId={SpreadsheetId} Failed to apply planner-response permissions — export not aborted.",
                    reportId, report.ReportNumber, destinationId);
                warnings.Add($"Planner-response permissions failed: {permEx.Message}");
            }

            if (noteCellMap.Count == 0)
            {
                _logger?.LogError(
                    "[Export] ERROR: NoteCellMap is EMPTY for report {ReportId}. Planner-response import will not work.",
                    reportId);
            }
            else
            {
                _logger?.LogInformation(
                    "[Export] NoteCellMap sample: NoteId={NoteId}, Sheet={Sheet}, Row={Row}, NoteCol={NoteCol}, RespCol={RespCol}.",
                    noteCellMap[0].NoteId, noteCellMap[0].SheetName,
                    noteCellMap[0].ExportedRowIndex, noteCellMap[0].ExportedNoteColumnIndex,
                    noteCellMap[0].PlannerResponseColumnIndex);
            }

            // ── 8. No-PDF rule ──
            bool pdfGenerated = !report.ReportNumber.ToString().Contains('.');

            return BuildResult(destinationId, destinationUrl, tagsReplaced, rowsInjected, report, warnings, true, pdfGenerated, noteCellMap);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Export] Export failed for report {ReportId}.", reportId);
            return new ReportExportResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Warnings = warnings
            };
        }
    }

    #region Tag Map

    private static Dictionary<string, string> BuildTagMap(Project? project, InspectionReport report)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // English keys
            ["ProjectName"] = project?.Title ?? "",
            ["ProjectTitle"] = project?.Title ?? "",
            ["ProjectNumber"] = project?.Number?.ToString("F0") ?? "",
            ["ProjectAdmin"] = project?.Admin ?? "",
            ["ProjectWorker"] = project?.Worker ?? "",
            ["InspectorName"] = report.InspectorName ?? "",
            ["ReportNumber"] = report.ReportNumber.ToString(),
            ["InspectionDate"] = report.InspectionDate.ToString("dd/MM/yyyy"),
            ["SourceFileVersion"] = report.SourceFileVersion ?? "",
            // Common English tags used in templates
            ["Today"] = DateTime.Now.ToString("dd/MM/yyyy"),
            ["User"] = report.InspectorName ?? "",
            ["Email"] = report.Inspector?.Email ?? "",
            // Hebrew aliases matching template general tags
            ["מספר דוח"] = report.ReportNumber.ToString(),
            ["מספר פרויקט"] = project?.Number?.ToString("F0") ?? "",
            ["ישוב"] = project?.Place?.Title ?? "",
            ["שם פרויקט"] = project?.Title ?? "",
            ["תאריך"] = report.InspectionDate.ToString("dd/MM/yyyy"),
            ["ממלא דוח"] = report.InspectorName ?? "",
            ["כתובת מייל"] = report.Inspector?.Email ?? "",
            // Header tags from traffic-report template
            ["מספר תכנית"] = project?.Number?.ToString("F0") ?? "",
            ["רשות מקומית"] = project?.Place?.Title ?? "",
            ["כתובת"] = project?.Contacts?.WorkAddress ?? "",
            ["גוש (חלקה)"] = "",
            ["יזם התכנית"] = project?.Admin ?? "",
            ["סוג נספח התנועה"] = "",
            ["מספרו"] = project?.Number?.ToString("F0") ?? "",
            ["מספר מגרש"] = project?.MazcirotTik?.ToString("F0") ?? "",
            ["מתכנן התכנית"] = project?.Worker ?? "",
            ["תאריך קבלת התוכנית"] = project?.Start?.ToString("dd/MM/yyyy") ?? ""
        };
    }

    #endregion

    #region Tag Scanning & Status Aggregation

    /// <summary>
    /// Tracks the location of template tags for a single section.
    /// </summary>
    private sealed class SectionTagInfo
    {
        public required string SectionCode { get; init; }
        /// <summary>Cell containing a <c>&lt;&lt;X.Y Title [...]&gt;&gt;</c> status tag.</summary>
        public (int Row, int Col)? StatusCell { get; set; }
        /// <summary>Cell containing a <c>&lt;&lt;X.Y Title&gt;&gt;</c> note tag.</summary>
        public (int Row, int Col)? NoteCell { get; set; }
        public List<InspectionNote> Notes { get; set; } = [];
    }

    // Compiled regex patterns for template tag detection
    // <<X.Y Title [Subtitle]>> — header/definition tag (numbered, with brackets)
    private static readonly Regex StatusTagRegex = new(@"<<\s*(\d+(?:\.\d+)+)\s+([^\[]*?)\[(.*?)\]\s*>>", RegexOptions.Compiled);
    // <<X.Y $>> or <<$ X.Y>> — note-input tag (numbered, dollar sign marker, either order for RTL/LTR)
    private static readonly Regex NoteInputTagRegex = new(@"<<\s*(?:(?<code>\d+(?:\.\d+)+)\s+\$|\$\s+(?<code>\d+(?:\.\d+)+))\s*>>", RegexOptions.Compiled);
    // <<X.Y Title>> — legacy note tag (numbered, no brackets, no $)
    private static readonly Regex NoteTagRegex = new(@"<<\s*(\d+(?:\.\d+)+)\s+([^>\[\$]+?)>>", RegexOptions.Compiled);
    // <<text>> — general data tag (non-numbered, e.g. <<שם פרויקט>>)
    private static readonly Regex GeneralTagRegex = new(@"<<\s*([^\d>\s][^>]*?)\s*>>", RegexOptions.Compiled);

    /// <summary>
    /// Single-pass scan of ALL cell values (from <c>Values.Get</c>) for
    /// <c>&lt;&lt;X.Y Title [...]&gt;&gt;</c> status tags and <c>&lt;&lt;X.Y Title&gt;&gt;</c> note tags.
    /// </summary>
    private Dictionary<string, SectionTagInfo> ScanTemplateTags(
        IList<IList<object>> rows,
        Dictionary<string, List<InspectionNote>> notesBySection)
    {
        var sections = new Dictionary<string, SectionTagInfo>(StringComparer.Ordinal);

        SectionTagInfo GetOrAdd(string code)
        {
            if (!sections.TryGetValue(code, out var info))
            {
                info = new SectionTagInfo { SectionCode = code };
                sections[code] = info;
            }
            return info;
        }

        int totalCellsScanned = 0;
        int statusTagsFound = 0;
        int noteTagsFound = 0;
        var sampleCells = new List<string>(20);

        for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var row = rows[rowIdx];
            if (row == null) continue;

            for (int colIdx = 0; colIdx < row.Count; colIdx++)
            {
                var rawText = row[colIdx]?.ToString();
                if (string.IsNullOrEmpty(rawText)) continue;

                totalCellsScanned++;
                var text = StripBidiMarks(rawText);

                if (sampleCells.Count < 20)
                    sampleCells.Add($"R{rowIdx}C{colIdx}='{text}'");

                // <<X.Y Title [...]>> status tags
                foreach (Match m in StatusTagRegex.Matches(text))
                {
                    var code = m.Groups[1].Value;
                    if (notesBySection.ContainsKey(code))
                    {
                        var info = GetOrAdd(code);
                        info.StatusCell ??= (rowIdx, colIdx);
                        statusTagsFound++;
                        _logger?.LogDebug("[ScanTags] Status <<{Code} [...]>> at R{Row}C{Col}.", code, rowIdx, colIdx);
                    }
                    else
                    {
                        _logger?.LogWarning(
                            "[ScanTags] Status tag code '{Code}' at R{Row}C{Col} has NO matching notes. DB keys: [{Keys}]",
                            code, rowIdx, colIdx, string.Join(", ", notesBySection.Keys));
                        System.Diagnostics.Debug.WriteLine(
                            $"[ScanTags] UNMATCHED Status '{code}' at R{rowIdx}C{colIdx}. DB keys=[{string.Join(", ", notesBySection.Keys)}]");
                    }
                }

                // <<X.Y $>> or <<$ X.Y>> note-input tags
                foreach (Match m in NoteInputTagRegex.Matches(text))
                {
                    var code = m.Groups["code"].Value;
                    if (notesBySection.ContainsKey(code))
                    {
                        var info = GetOrAdd(code);
                        info.NoteCell ??= (rowIdx, colIdx);
                        noteTagsFound++;
                        _logger?.LogDebug("[ScanTags] NoteInput <<{Code} $>> at R{Row}C{Col}.", code, rowIdx, colIdx);
                    }
                    else
                    {
                        _logger?.LogWarning(
                            "[ScanTags] NoteInput tag code '{Code}' at R{Row}C{Col} has NO matching notes. DB keys: [{Keys}]",
                            code, rowIdx, colIdx, string.Join(", ", notesBySection.Keys));
                        System.Diagnostics.Debug.WriteLine(
                            $"[ScanTags] UNMATCHED NoteInput '{code}' at R{rowIdx}C{colIdx}. DB keys=[{string.Join(", ", notesBySection.Keys)}]");
                    }
                }

                // <<X.Y Title>> note tags (no brackets = note tag)
                foreach (Match m in NoteTagRegex.Matches(text))
                {
                    var code = m.Groups[1].Value;
                    if (notesBySection.ContainsKey(code))
                    {
                        var info = GetOrAdd(code);
                        info.NoteCell ??= (rowIdx, colIdx);
                        noteTagsFound++;
                        _logger?.LogDebug("[ScanTags] Note <<{Code}>> at R{Row}C{Col}.", code, rowIdx, colIdx);
                    }
                    else
                    {
                        _logger?.LogWarning(
                            "[ScanTags] Note tag code '{Code}' at R{Row}C{Col} has NO matching notes. DB keys: [{Keys}]",
                            code, rowIdx, colIdx, string.Join(", ", notesBySection.Keys));
                        System.Diagnostics.Debug.WriteLine(
                            $"[ScanTags] UNMATCHED Note '{code}' at R{rowIdx}C{colIdx}. DB keys=[{string.Join(", ", notesBySection.Keys)}]");
                    }
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[ScanTags] Sample cells: {string.Join(" | ", sampleCells)}");
        _logger?.LogInformation(
            "[ScanTags] Scanned {Cells} cells across {Rows} rows. Status={Status}, Notes={Notes}, Matched={Matched}. Keys=[{Keys}]",
            totalCellsScanned, rows.Count, statusTagsFound, noteTagsFound, sections.Count,
            string.Join(", ", notesBySection.Keys));
        System.Diagnostics.Debug.WriteLine(
            $"[ScanTags] Scanned {totalCellsScanned} cells across {rows.Count} rows. Status={statusTagsFound}, Notes={noteTagsFound}, Matched={sections.Count}. DB keys=[{string.Join(", ", notesBySection.Keys)}]");

        foreach (var (code, noteList) in notesBySection)
        {
            if (sections.TryGetValue(code, out var info))
                info.Notes = noteList;
        }

        return sections;
    }

    /// <summary>
    /// Loads status label mappings from the SystemSettings DB table.
    /// Falls back to <see cref="InspectionStatusKeys.DefaultLabels"/> when no DB value exists.
    /// </summary>
    private async Task<Dictionary<string, string>> LoadStatusLabelsAsync(CancellationToken cancellationToken)
    {
        using var settingsService = new SystemSettingsService(_contextFactory);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, defaultLabel) in InspectionStatusKeys.DefaultLabels)
        {
            var settingKey = $"StatusLabel_{key}";
            labels[key] = await settingsService.GetOrDefaultAsync(settingKey, defaultLabel, cancellationToken);
        }
        return labels;
    }

    /// <summary>
    /// Computes the "strongest" aggregated status DB key for a section.
    /// Priority: Failed &gt; RecurringFailed &gt; Passed &gt; NotApplicable.
    /// Returns <c>null</c> when no meaningful status can be determined.
    /// Also accepts legacy status strings for backward compatibility.
    /// </summary>
    private static string? ComputeAggregatedStatusKey(List<InspectionNote> notes)
    {
        if (notes.Count == 0) return null;

        bool hasFailed = false;
        bool hasRecurring = false;
        bool hasPassed = false;
        bool allNotRelevant = true;

        foreach (var note in notes)
        {
            var s = note.NoteStatus?.Trim();
            if (string.IsNullOrEmpty(s)) { allNotRelevant = false; continue; }

            switch (s)
            {
                case "Failed":
                case "Issue":       // legacy
                case "ManagerReview":  // should not reach export, but treat as Failed if it does
                    hasFailed = true;
                    allNotRelevant = false;
                    break;
                case "RecurringFailed":
                case "Recurring":   // legacy
                    hasRecurring = true;
                    allNotRelevant = false;
                    break;
                case "Passed":
                case "OK":          // legacy
                    hasPassed = true;
                    allNotRelevant = false;
                    break;
                case "NotApplicable":
                case "לא רלוונטי":  // legacy
                    break;
                default:
                    allNotRelevant = false;
                    break;
            }
        }

        if (hasFailed) return InspectionStatusKeys.Failed;
        if (hasRecurring) return InspectionStatusKeys.RecurringFailed;
        if (hasPassed) return InspectionStatusKeys.Passed;
        if (allNotRelevant) return InspectionStatusKeys.NotApplicable;
        return null;
    }

    /// <summary>
    /// Returns a light background color for the given status DB key
    /// to visually distinguish status cells in the exported sheet.
    /// </summary>
    private static SheetsColor GetStatusBackgroundColor(string statusKey) => statusKey switch
    {
        "Passed" or "OK" => new SheetsColor { Red = 0.85f, Green = 0.95f, Blue = 0.85f },
        "Failed" or "Issue" => new SheetsColor { Red = 0.95f, Green = 0.85f, Blue = 0.85f },
        "RecurringFailed" or "Recurring" => new SheetsColor { Red = 1f, Green = 0.93f, Blue = 0.8f },
        "NotApplicable" or "לא רלוונטי" => new SheetsColor { Red = 0.93f, Green = 0.93f, Blue = 0.93f },
        "ManagerReview" => new SheetsColor { Red = 0.93f, Green = 0.85f, Blue = 0.95f },
        _ => new SheetsColor { Red = 1f, Green = 1f, Blue = 1f }
    };

    /// <summary>
    /// Strips Unicode BiDi control characters and zero-width characters
    /// that may interfere with regex matching in RTL sheets.
    /// </summary>
    private static string StripBidiMarks(string text) =>
        Regex.Replace(text, @"[\u200B-\u200F\u00AD\u2060\uFEFF\u202A-\u202E\u2066-\u2069]", "");

    /// <summary>
    /// Scans all cells for <c>&lt;&lt;X.Y Title [...]&gt;&gt;</c> (status) and
    /// <c>&lt;&lt;X.Y Title&gt;&gt;</c> (note) tags without any filtering.
    /// Returns every tag found — used for template sync comparison with the local database.
    /// Scans column-by-column (right to left), top to bottom within each column,
    /// so non-numbered "General Data" tags are discovered in correct RTL reading order.
    /// </summary>
    public static List<TemplateScanTag> ScanAllTemplateTags(IList<IList<object>> rows) =>
        InspectionTemplateTagGrammar.ScanAllTemplateTags(rows);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateScanTag>> ScanTemplateAsync(
        string templateSpreadsheetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSpreadsheetId);

        await _authService.EnsureAuthenticatedAsync(cancellationToken);
        var sheetsService = _authService.SheetsService
            ?? throw new InvalidOperationException("Sheets service not available after authentication.");

        var spreadsheet = await sheetsService.Spreadsheets.Get(templateSpreadsheetId)
            .ExecuteAsync(cancellationToken);
        var sheet = spreadsheet.Sheets[0];
        var sheetTitle = sheet.Properties.Title ?? "Sheet1";
        var totalRows = sheet.Properties.GridProperties?.RowCount ?? 1200;

        var valuesResponse = await sheetsService.Spreadsheets.Values
            .Get(templateSpreadsheetId, $"'{sheetTitle}'!A1:Z{totalRows}")
            .ExecuteAsync(cancellationToken);

        if (valuesResponse.Values == null || valuesResponse.Values.Count == 0)
            return Array.Empty<TemplateScanTag>();

        return ScanAllTemplateTags(valuesResponse.Values);
    }

    #endregion

    /// <inheritdoc />
    public async Task<AnyoneWithLinkShareResult> ShareReportAnyoneWithLinkAsync(
        string spreadsheetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);

        try
        {
            await _authService.EnsureAuthenticatedAsync(cancellationToken);
            var driveService = _authService.DriveService
                ?? throw new InvalidOperationException("Drive service not available after authentication.");

            var listRequest = driveService.Permissions.List(spreadsheetId);
            listRequest.Fields = "permissions(id,type,role,allowFileDiscovery)";
            listRequest.SupportsAllDrives = true;

            var existing = await listRequest.ExecuteAsync(cancellationToken);
            var anyone = existing?.Permissions?
                .FirstOrDefault(p => string.Equals(p.Type, "anyone", StringComparison.OrdinalIgnoreCase));

            bool existingFound = anyone != null;
            var url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";

            if (anyone != null)
            {
                if (!string.Equals(anyone.Role, "writer", StringComparison.OrdinalIgnoreCase))
                {
                    var updateBody = new Google.Apis.Drive.v3.Data.Permission { Role = "writer" };
                    var updateRequest = driveService.Permissions.Update(updateBody, spreadsheetId, anyone.Id);
                    updateRequest.SupportsAllDrives = true;
                    await updateRequest.ExecuteAsync(cancellationToken);
                }
            }
            else
            {
                var permission = new Google.Apis.Drive.v3.Data.Permission
                {
                    Type = "anyone",
                    Role = "writer",
                    AllowFileDiscovery = false
                };
                var createRequest = driveService.Permissions.Create(permission, spreadsheetId);
                createRequest.SupportsAllDrives = true;
                createRequest.SendNotificationEmail = false;
                await createRequest.ExecuteAsync(cancellationToken);
            }

            _logger?.LogInformation(
                "[Export] Operation=ShareReportAnyoneWithLink SpreadsheetId={SpreadsheetId} " +
                "ExistingAnyonePermissionFound={Existing} Result=Success",
                spreadsheetId, existingFound);

            return new AnyoneWithLinkShareResult
            {
                IsSuccess = true,
                ExistingAnyonePermissionFound = existingFound,
                SpreadsheetUrl = url
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[Export] Operation=ShareReportAnyoneWithLink SpreadsheetId={SpreadsheetId} " +
                "Result=Failed Reason={Reason}", spreadsheetId, ex.Message);
            return new AnyoneWithLinkShareResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
    #region TextFormatRun Conversion

    /// <summary>
    /// Converts internal <see cref="RichTextRun"/> list to Google Sheets <see cref="TextFormatRun"/> list.
    /// Returns <c>null</c> if there are no styled runs (plain text only).
    /// </summary>
    private static List<TextFormatRun>? ToGoogleTextFormatRuns(
        string plainText, List<RichTextRun> runs)
    {
        if (runs.Count == 0 || string.IsNullOrEmpty(plainText))
            return null;

        var sorted = runs.OrderBy(r => r.StartIndex).ToList();
        var result = new List<TextFormatRun>();
        int cursor = 0;

        foreach (var run in sorted)
        {
            // Emit default-format run for gap before this styled run
            if (run.StartIndex > cursor)
            {
                result.Add(new TextFormatRun
                {
                    StartIndex = cursor,
                    Format = CreateDefaultFormat()
                });
            }

            // Emit the styled run
            result.Add(new TextFormatRun
            {
                StartIndex = run.StartIndex,
                Format = CreateStyledFormat(run)
            });

            cursor = run.StartIndex + run.Length;
        }

        // Emit default run for trailing plain text
        if (cursor < plainText.Length)
        {
            result.Add(new TextFormatRun
            {
                StartIndex = cursor,
                Format = CreateDefaultFormat()
            });
        }

        return result.Count > 0 ? result : null;
    }

    private static TextFormat CreateDefaultFormat()
    {
        return new TextFormat
        {
            Bold = false,
            ForegroundColor = BlackColor
        };
    }

    private static TextFormat CreateStyledFormat(RichTextRun run)
    {
        return new TextFormat
        {
            Bold = run.Bold,
            ForegroundColor = run.Color switch
            {
                RichTextColor.Red => RedColor,
                RichTextColor.Blue => BlueColor,
                RichTextColor.Green => GreenColor,
                RichTextColor.Gray => GrayColor,
                _ => BlackColor
            }
        };
    }

    #endregion

    #region Google Drive Folder Management

    /// <summary>
    /// Finds an existing sub-folder by name inside <paramref name="parentFolderId"/>,
    /// or creates a new one. Returns the folder ID.
    /// </summary>
    private static async Task<string> FindOrCreateDriveFolderAsync(
        DriveService driveService,
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken)
    {
        // Search for existing folder
        var listRequest = driveService.Files.List();
        listRequest.Q = $"'{parentFolderId}' in parents " +
                        $"and mimeType = '{FolderMimeType}' " +
                        $"and name = '{folderName.Replace("'", "\\'")}' " +
                        $"and trashed = false";
        listRequest.Fields = "files(id, name)";
        listRequest.PageSize = 1;
        listRequest.SupportsAllDrives = true;
        listRequest.IncludeItemsFromAllDrives = true;

        var listResult = await listRequest.ExecuteAsync(cancellationToken);
        if (listResult.Files is { Count: > 0 })
            return listResult.Files[0].Id;

        // Create new folder
        var folderMeta = new DriveFile
        {
            Name = folderName,
            MimeType = FolderMimeType,
            Parents = [parentFolderId]
        };

        var createRequest = driveService.Files.Create(folderMeta);
        createRequest.SupportsAllDrives = true;
        createRequest.Fields = "id";

        var created = await createRequest.ExecuteAsync(cancellationToken);
        return created.Id;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Adds a FindReplace request that substitutes <paramref name="tag"/> with
    /// <paramref name="value"/> on a single sheet. Empty values clear the tag.
    /// </summary>
    private static void AddTagFindReplace(List<Request> requests, int sheetId, string tag, string? value)
    {
        if (string.IsNullOrEmpty(tag)) return;
        requests.Add(new Request
        {
            FindReplace = new FindReplaceRequest
            {
                Find = tag,
                Replacement = value ?? string.Empty,
                MatchCase = false,
                MatchEntireCell = false,
                SheetId = sheetId
            }
        });
    }

    /// <summary>
    /// Extracts the section code from a NoteSubIndex by stripping the last segment.
    /// E.g. "1.1.1" → "1.1", "2.3.2" → "2.3". Falls back to <paramref name="fullCode"/> when NoteSubIndex
    /// has no multi-level dot notation.
    /// </summary>
    private static string ExtractSectionCode(string? noteSubIndex, string fullCode)
    {
        if (!string.IsNullOrEmpty(noteSubIndex))
        {
            var lastDot = noteSubIndex.LastIndexOf('.');
            if (lastDot > 0)
                return noteSubIndex[..lastDot];
        }
        return fullCode;
    }

    private static ReportExportResult BuildResult(
        string destinationId,
        string destinationUrl,
        int tagsReplaced,
        int rowsInjected,
        InspectionReport report,
        List<string> warnings,
        bool success,
        bool pdfGenerated = false,
        List<ExportedNoteCellMap>? noteCellMap = null)
    {
        return new ReportExportResult
        {
            DestinationSpreadsheetId = destinationId,
            DestinationUrl = destinationUrl,
            TagsReplaced = tagsReplaced,
            RowsInjected = rowsInjected,
            PdfGenerated = pdfGenerated,
            Warnings = warnings,
            IsSuccess = success,
            NoteCellMap = noteCellMap ?? []
        };
    }

    #endregion

    #region Planner-Response Permissions

    private static readonly System.Text.RegularExpressions.Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Resolves the planner/designer email from the report's project planners.
    /// Returns null if no planner with a valid email is associated.
    /// </summary>
    private static string? ResolvePlannerEmail(InspectionReport report)
    {
        var email = report.Project?.ProjectPlanners?
            .Select(pp => pp.Contacts?.Email)
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e) && EmailRegex.IsMatch(e!));
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    private const string PlannerProtectionDescriptionPrefix = "SI Inspection Planner Response Protection";

    private static string BuildPlannerProtectionDescription(InspectionReport report)
        => $"{PlannerProtectionDescriptionPrefix} - ReportId={report.ReportId}";

    /// <summary>
    /// Shares the exported spreadsheet with the planner (if email is known) and
    /// protects every sheet so that only the planner-response cells in
    /// <paramref name="noteCellMap"/> remain editable. Cells with a missing
    /// PlannerResponseColumnIndex are skipped with a warning. After applying
    /// the AddProtectedRange batch, re-reads the spreadsheet and verifies that
    /// our protections were actually persisted.
    /// </summary>
    private async Task ApplyPlannerResponsePermissionsAsync(
        SheetsService sheetsService,
        DriveService driveService,
        string templateSpreadsheetId,
        string spreadsheetId,
        Spreadsheet spreadsheet,
        List<ExportedNoteCellMap> noteCellMap,
        InspectionReport report,
        string? designerEmail,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        // 0. Sanity: ensure we are operating on the destination, not the template.
        _logger?.LogInformation(
            "[Export] Operation=PlannerResponsePermissions Step=Begin ReportId={ReportId} ReportNumber={ReportNumber} " +
            "TemplateSpreadsheetId={TemplateId} ExportedSpreadsheetId={DestId} SpreadsheetIdUsedForPermissions={UsedId}",
            report.ReportId, report.ReportNumber, templateSpreadsheetId, spreadsheetId, spreadsheetId);

        if (string.Equals(spreadsheetId, templateSpreadsheetId, StringComparison.Ordinal))
        {
            _logger?.LogError(
                "[Export] Operation=PlannerResponsePermissions Reason=AppliedToTemplate " +
                "ReportId={ReportId} TemplateSpreadsheetId={TemplateId} — refusing to mutate template.",
                report.ReportId, templateSpreadsheetId);
            warnings.Add("Planner-response permissions aborted: target equals template ID.");
            return;
        }

        // 1. Build (sheet,row,col) list and log noteCellMap diagnostics.
        var sheetTitlesInSpreadsheet = spreadsheet.Sheets
            .Select(s => s.Properties?.Title ?? string.Empty)
            .ToList();
        var sheetTitlesInMap = noteCellMap
            .Select(e => e.SheetName ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _logger?.LogInformation(
            "[Export] Operation=PlannerResponsePermissions Step=NoteCellMapAudit " +
            "ReportId={ReportId} SpreadsheetSheetNames=[{SpreadsheetSheets}] NoteCellMapSheetNames=[{MapSheets}] EntriesTotal={Total}",
            report.ReportId,
            string.Join(", ", sheetTitlesInSpreadsheet),
            string.Join(", ", sheetTitlesInMap),
            noteCellMap.Count);

        var unlocked = new List<(string SheetName, int Row, int Col)>();
        int skippedMissingSheet = 0;
        int skippedMissingCol = 0;
        int skippedBadRow = 0;
        int skippedSheetNotFound = 0;
        var validSheetTitles = new HashSet<string>(sheetTitlesInSpreadsheet, StringComparer.Ordinal);

        foreach (var entry in noteCellMap)
        {
            if (string.IsNullOrWhiteSpace(entry.SheetName))
            {
                skippedMissingSheet++;
                continue;
            }
            if (!validSheetTitles.Contains(entry.SheetName))
            {
                skippedSheetNotFound++;
                _logger?.LogWarning(
                    "[Export] Operation=PlannerResponsePermissions Reason=SheetNameNotFoundInSpreadsheet " +
                    "ReportId={ReportId} NoteId={NoteId} Sheet={Sheet}",
                    report.ReportId, entry.NoteId, entry.SheetName);
                continue;
            }
            if (entry.PlannerResponseColumnIndex < 0)
            {
                skippedMissingCol++;
                _logger?.LogWarning(
                    "[Export] Operation=PlannerResponsePermissions Reason=Missing PlannerResponseColumnIndex " +
                    "ReportId={ReportId} NoteId={NoteId} Sheet={Sheet} Row={Row}",
                    report.ReportId, entry.NoteId, entry.SheetName, entry.ExportedRowIndex);
                continue;
            }
            if (entry.ExportedRowIndex < 0)
            {
                skippedBadRow++;
                continue;
            }
            unlocked.Add((entry.SheetName!, entry.ExportedRowIndex, entry.PlannerResponseColumnIndex));
        }

        // 2. Share the file with the planner as Editor.
        if (!string.IsNullOrWhiteSpace(designerEmail))
        {
            try
            {
                var permission = new Google.Apis.Drive.v3.Data.Permission
                {
                    Type = "user",
                    Role = "writer",
                    EmailAddress = designerEmail
                };
                var permRequest = driveService.Permissions.Create(permission, spreadsheetId);
                permRequest.SendNotificationEmail = false;
                permRequest.SupportsAllDrives = true;
                await permRequest.ExecuteAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "[Export] Operation=PlannerResponsePermissions Reason=DriveShareFailed " +
                    "ReportId={ReportId} DesignerEmail={Email}", report.ReportId, designerEmail);
                warnings.Add($"Failed to share report with planner '{designerEmail}': {ex.Message}");
            }
        }
        else
        {
            _logger?.LogWarning(
                "[Export] Operation=PlannerResponsePermissions Reason=MissingDesignerEmail " +
                "ReportId={ReportId} ReportNumber={ReportNumber} — protection still applied, but no planner share.",
                report.ReportId, report.ReportNumber);
            warnings.Add("Planner email is unknown — exported sheet protected but not shared.");
        }

        // 3. Resolve the Google account that runs this export — it must be the
        //    sole editor of the protected range. If we can't resolve it, abort
        //    rather than create a wide-open protection.
        string? currentUserEmail = null;
        try
        {
            var aboutRequest = driveService.About.Get();
            aboutRequest.Fields = "user(emailAddress,displayName,permissionId)";
            var about = await aboutRequest.ExecuteAsync(cancellationToken);
            currentUserEmail = about?.User?.EmailAddress;
            _logger?.LogInformation(
                "[Export] Operation=PlannerResponsePermissions Step=ResolveProtectionOwner " +
                "ReportId={ReportId} CurrentGoogleUserEmail={Email} CurrentGoogleUserDisplayName={Name} " +
                "CurrentGoogleUserPermissionId={PermId}",
                report.ReportId, currentUserEmail ?? "(null)",
                about?.User?.DisplayName ?? "(null)", about?.User?.PermissionId ?? "(null)");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[Export] Operation=PlannerResponsePermissions Reason=CouldNotResolveProtectionOwner " +
                "ReportId={ReportId} SpreadsheetId={SpreadsheetId}",
                report.ReportId, spreadsheetId);
        }

        if (string.IsNullOrWhiteSpace(currentUserEmail))
        {
            _logger?.LogWarning(
                "[Export] Operation=PlannerResponsePermissions Reason=CouldNotResolveProtectionOwner " +
                "ReportId={ReportId} SpreadsheetId={SpreadsheetId} — aborting protection to avoid wide-open ranges.",
                report.ReportId, spreadsheetId);
            warnings.Add("Could not resolve protection owner email — protection NOT applied.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(designerEmail) &&
            string.Equals(designerEmail, currentUserEmail, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "[Export] Operation=PlannerResponsePermissions Reason=DesignerEqualsCurrentUser " +
                "ReportId={ReportId} CurrentUser={CurrentUser} DesignerEmail={Designer}",
                report.ReportId, currentUserEmail, designerEmail);
        }

        // 4. Build AddProtectedRange requests per sheet.
        var ourDescription = BuildPlannerProtectionDescription(report);
        var requests = new List<Request>();
        int protectedSheets = 0;
        var perSheetUnlockCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var sheet in spreadsheet.Sheets)
        {
            var props = sheet.Properties;
            if (props is null) continue;

            var sheetTitle = props.Title ?? string.Empty;
            int sheetId = props.SheetId ?? 0;
            int rowCount = props.GridProperties?.RowCount ?? 1000;
            int colCount = props.GridProperties?.ColumnCount ?? 26;

            var unprotected = unlocked
                .Where(u => string.Equals(u.SheetName, sheetTitle, StringComparison.Ordinal)
                            && u.Row < rowCount && u.Col < colCount)
                .Select(u => new GridRange
                {
                    SheetId = sheetId,
                    StartRowIndex = u.Row,
                    EndRowIndex = u.Row + 1,
                    StartColumnIndex = u.Col,
                    EndColumnIndex = u.Col + 1
                })
                .ToList();

            perSheetUnlockCounts[sheetTitle] = unprotected.Count;

            var protectedRange = new ProtectedRange
            {
                // Range with only SheetId → protects the entire sheet (per Sheets API docs).
                Range = new GridRange { SheetId = sheetId },
                Description = ourDescription,
                WarningOnly = false,
                // Editors MUST be set explicitly to restrict edit rights to the
                // current Google user only. Omitting Editors lets every file editor
                // (including the planner) edit the protected range, which defeats
                // the whole purpose. We must NOT include designerEmail here.
                Editors = new Editors
                {
                    Users = new List<string> { currentUserEmail },
                    DomainUsersCanEdit = false
                }
            };
            if (unprotected.Count > 0)
                protectedRange.UnprotectedRanges = unprotected;

            var sample = unprotected.Take(3)
                .Select(r => $"R{r.StartRowIndex + 1}C{r.StartColumnIndex + 1}")
                .ToList();
            bool designerInEditors = !string.IsNullOrWhiteSpace(designerEmail) &&
                protectedRange.Editors.Users.Any(u =>
                    string.Equals(u, designerEmail, StringComparison.OrdinalIgnoreCase));
            _logger?.LogInformation(
                "[Export] Operation=PlannerResponsePermissions Step=BuildRequest " +
                "ReportId={ReportId} ReportNumber={ReportNumber} SpreadsheetId={SpreadsheetId} " +
                "SheetName={SheetName} SheetId={SheetId} ProtectedRangeDescription={Desc} " +
                "EditorsMode=CurrentUserOnly EditorsUsersCount={EditorsCount} EditorsUsers=[{EditorsUsers}] " +
                "DesignerEmail={Designer} DesignerIsProtectedRangeEditor={DesignerInEditors} " +
                "UnprotectedRangesCount={UnprotCount} Sample=[{Sample}]",
                report.ReportId, report.ReportNumber, spreadsheetId,
                sheetTitle, sheetId, ourDescription,
                protectedRange.Editors.Users.Count, string.Join(", ", protectedRange.Editors.Users),
                designerEmail ?? "(none)", designerInEditors,
                unprotected.Count, string.Join(", ", sample));

            requests.Add(new Request
            {
                AddProtectedRange = new AddProtectedRangeRequest { ProtectedRange = protectedRange }
            });
            protectedSheets++;
        }

        _logger?.LogInformation(
            "[Export] Operation=PlannerResponsePermissions Step=PreBatch " +
            "ReportId={ReportId} SpreadsheetId={SpreadsheetId} SheetsCount={SheetsCount} RequestsCount={ReqCount} " +
            "UnlockedTotal={Unlocked} SkippedMissingCol={SkipCol} SkippedSheetNotFound={SkipSheet} " +
            "SkippedMissingSheetName={SkipName} SkippedBadRow={SkipRow}",
            report.ReportId, spreadsheetId, spreadsheet.Sheets.Count, requests.Count,
            unlocked.Count, skippedMissingCol, skippedSheetNotFound, skippedMissingSheet, skippedBadRow);

        if (requests.Count == 0)
        {
            _logger?.LogWarning(
                "[Export] Operation=PlannerResponsePermissions Reason=NoProtectedRangeRequestsCreated " +
                "ReportId={ReportId} SpreadsheetId={SpreadsheetId}",
                report.ReportId, spreadsheetId);
            warnings.Add("No planner-response protections were requested for this report.");
            return;
        }

        // 4. Execute the batch and capture replies.
        BatchUpdateSpreadsheetResponse? batchResponse = null;
        try
        {
            var batch = new BatchUpdateSpreadsheetRequest { Requests = requests };
            batchResponse = await sheetsService.Spreadsheets.BatchUpdate(batch, spreadsheetId)
                .ExecuteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[Export] Operation=PlannerResponsePermissions Reason=BatchUpdateFailed " +
                "ReportId={ReportId} SpreadsheetId={SpreadsheetId}",
                report.ReportId, spreadsheetId);
            warnings.Add($"Planner-response BatchUpdate failed: {ex.Message}");
            return;
        }

        var createdProtectedIds = batchResponse?.Replies?
            .Where(r => r.AddProtectedRange?.ProtectedRange?.ProtectedRangeId != null)
            .Select(r => r.AddProtectedRange.ProtectedRange.ProtectedRangeId!.Value.ToString())
            .ToList() ?? new List<string>();

        _logger?.LogInformation(
            "[Export] Operation=PlannerResponsePermissions Step=PostBatch " +
            "ReportId={ReportId} SpreadsheetId={SpreadsheetId} RepliesCount={Replies} CreatedProtectedRangeIds=[{Ids}]",
            report.ReportId, spreadsheetId,
            batchResponse?.Replies?.Count ?? 0, string.Join(", ", createdProtectedIds));

        // 5. Verification — re-read the spreadsheet and check that our protections persisted.
        try
        {
            var verifyGet = sheetsService.Spreadsheets.Get(spreadsheetId);
            verifyGet.Fields = "sheets(properties(sheetId,title),protectedRanges(protectedRangeId,description,range,unprotectedRanges,warningOnly,editors))";
            var verifySpreadsheet = await verifyGet.ExecuteAsync(cancellationToken);

            int totalOurProtected = 0;
            int totalOurUnprotected = 0;
            foreach (var sheet in verifySpreadsheet.Sheets)
            {
                var props = sheet.Properties;
                if (props is null) continue;
                var sheetTitle = props.Title ?? string.Empty;
                int sheetId = props.SheetId ?? 0;

                var allProt = sheet.ProtectedRanges ?? new List<ProtectedRange>();
                var ours = allProt
                    .Where(p => string.Equals(p.Description, ourDescription, StringComparison.Ordinal))
                    .ToList();
                int ourUnprot = ours.Sum(p => p.UnprotectedRanges?.Count ?? 0);
                totalOurProtected += ours.Count;
                totalOurUnprotected += ourUnprot;

                _logger?.LogInformation(
                    "[Export] Operation=PlannerResponsePermissionsVerify ReportId={ReportId} ReportNumber={ReportNumber} " +
                    "SpreadsheetId={SpreadsheetId} SheetName={SheetName} SheetId={SheetId} " +
                    "ProtectedRangesCount={All} OurProtectedRangesCount={Ours} OurUnprotectedRangesCount={OurUnprot}",
                    report.ReportId, report.ReportNumber, spreadsheetId, sheetTitle, sheetId,
                    allProt.Count, ours.Count, ourUnprot);

                foreach (var p in ours)
                {
                    bool wholeSheet = p.Range != null
                        && p.Range.StartRowIndex is null && p.Range.EndRowIndex is null
                        && p.Range.StartColumnIndex is null && p.Range.EndColumnIndex is null;
                    var editorUsers = p.Editors?.Users ?? new List<string>();
                    bool domainCanEdit = p.Editors?.DomainUsersCanEdit ?? false;
                    var unprotSample = (p.UnprotectedRanges ?? new List<GridRange>())
                        .Take(3)
                        .Select(r => $"R{(r.StartRowIndex ?? 0) + 1}C{(r.StartColumnIndex ?? 0) + 1}")
                        .ToList();

                    _logger?.LogInformation(
                        "[Export] Operation=PlannerResponsePermissionsVerifyDetails ReportId={ReportId} " +
                        "SpreadsheetId={SpreadsheetId} SheetName={SheetName} ProtectedRangeId={Pid} " +
                        "Description={Desc} WarningOnly={Warn} ProtectionScope={Scope} " +
                        "EditorsUsers=[{Editors}] DomainUsersCanEdit={DomainEdit} " +
                        "UnprotectedRangesCount={UnprotCount} SampleUnprotectedRanges=[{Sample}]",
                        report.ReportId, spreadsheetId, sheetTitle,
                        p.ProtectedRangeId, p.Description, p.WarningOnly,
                        wholeSheet ? "WholeSheet" : "PartialRange",
                        string.Join(", ", editorUsers), domainCanEdit,
                        p.UnprotectedRanges?.Count ?? 0, string.Join(", ", unprotSample));

                    if (editorUsers.Count == 0)
                    {
                        _logger?.LogWarning(
                            "[Export] Operation=PlannerResponsePermissionsVerifyDetails Reason=ProtectedRangeEditorsNotRestricted " +
                            "ReportId={ReportId} ProtectedRangeId={Pid}",
                            report.ReportId, p.ProtectedRangeId);
                        warnings.Add("Protected range editors are not restricted — anyone with file access can edit.");
                    }
                    if (!string.IsNullOrWhiteSpace(designerEmail) &&
                        editorUsers.Any(u => string.Equals(u, designerEmail, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger?.LogWarning(
                            "[Export] Operation=PlannerResponsePermissionsVerifyDetails Reason=DesignerCanEditProtectedRange " +
                            "ReportId={ReportId} ProtectedRangeId={Pid} DesignerEmail={Designer}",
                            report.ReportId, p.ProtectedRangeId, designerEmail);
                        warnings.Add("Planner is listed as protected-range editor — protection ineffective.");
                    }
                }
            }

            if (totalOurProtected == 0)
            {
                _logger?.LogWarning(
                    "[Export] Operation=PlannerResponsePermissionsVerify Reason=OurProtectionNotFound " +
                    "ReportId={ReportId} SpreadsheetId={SpreadsheetId}",
                    report.ReportId, spreadsheetId);
                warnings.Add("לא נמצאה הגנת תאים חדשה בדוח לאחר ניסיון ההגדרה");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[Export] Operation=PlannerResponsePermissionsVerify Reason=VerifyFailed " +
                "ReportId={ReportId} SpreadsheetId={SpreadsheetId}",
                report.ReportId, spreadsheetId);
            warnings.Add($"Planner-response verification failed: {ex.Message}");
        }

        _logger?.LogInformation(
            "[Export] Operation=PlannerResponsePermissions Step=Done ReportId={ReportId} ReportNumber={ReportNumber} " +
            "SpreadsheetId={SpreadsheetId} DesignerEmail={DesignerEmail} ProtectedSheetsCount={ProtectedSheets} " +
            "UnlockedPlannerResponseCellsCount={Unlocked} SkippedCellsCount={Skipped}",
            report.ReportId, report.ReportNumber, spreadsheetId,
            designerEmail ?? "(none)", protectedSheets, unlocked.Count,
            skippedMissingCol + skippedMissingSheet + skippedBadRow + skippedSheetNotFound);
    }

    #endregion
}
