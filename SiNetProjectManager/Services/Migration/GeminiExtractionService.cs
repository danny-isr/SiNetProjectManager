using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Google.Apis.Sheets.v4;
using SiNetSQL.Services;
using SiOffice.GoogleConnector.Reports;
using SheetsColor = Google.Apis.Sheets.v4.Data.Color;

namespace SiNetProjectManager.Services.Migration;

/// <summary>
/// Uses Google Gemini AI to extract inspection data by reading both sheets
/// via the Sheets API (cell values + background colors as [BG:#hex] annotations)
/// and sending the structured text to Gemini for analysis.
/// <para>
/// Diagnostic files are saved to %APPDATA%\SiNet\Logs\GeminiDiag\ for debugging.
/// </para>
/// </summary>
public sealed class GeminiExtractionService(GoogleAuthService authService, string apiKey, string model = "gemini-2.5-flash", int timeoutSeconds = 300)
{
    private static readonly Uri s_baseUri = new("https://generativelanguage.googleapis.com/v1beta/models/");

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

    private static readonly string s_diagFolder = Path.Combine(
        Environment.ExpandEnvironmentVariables("%APPDATA%"), "SiNet", "Logs", "GeminiDiag");

    /// <summary>
    /// Authenticates and reads a template spreadsheet with colors as text.
    /// Call once before a batch loop and pass the result to the overload that accepts cached template text.
    /// </summary>
    public async Task<string> ReadTemplateTextAsync(
        string templateSpreadsheetId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSpreadsheetId);

        await authService.EnsureAuthenticatedAsync(ct);
        var sheetsService = authService.SheetsService
            ?? throw new InvalidOperationException("Sheets service not available after authentication.");

        return await ReadSheetWithColorsAsTextAsync(sheetsService, templateSpreadsheetId, ct);
    }

    /// <summary>
    /// Reads both spreadsheets via Sheets API with colors, builds a text prompt,
    /// sends to Gemini, and saves all diagnostic files for debugging.
    /// </summary>
    public async Task<ReportExtractionResult> ExtractWithAiAsync(
        string templateSpreadsheetId,
        string reportSpreadsheetId,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        return await ExtractWithAiCoreAsync(
            templateSpreadsheetId, reportSpreadsheetId,
            cachedTemplateText: null, onProgress, ct);
    }

    /// <summary>
    /// Extracts using a pre-read template text (avoids re-reading the template from Google Sheets).
    /// Use this overload in batch scenarios where the template is the same for all reports.
    /// </summary>
    public async Task<ReportExtractionResult> ExtractWithAiAsync(
        string templateSpreadsheetId,
        string reportSpreadsheetId,
        string cachedTemplateText,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachedTemplateText);

        return await ExtractWithAiCoreAsync(
            templateSpreadsheetId, reportSpreadsheetId,
            cachedTemplateText, onProgress, ct);
    }

    private async Task<ReportExtractionResult> ExtractWithAiCoreAsync(
        string templateSpreadsheetId,
        string reportSpreadsheetId,
        string? cachedTemplateText,
        Action<string>? onProgress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSpreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportSpreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var warnings = new List<string>();
        Directory.CreateDirectory(s_diagFolder);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        var totalSw = Stopwatch.StartNew();

        try
        {
            AppLogger.Info($"[GeminiAI] ══════ START ══════ Mode=text-with-colors, Model={model}, Timeout={timeoutSeconds}s");
            AppLogger.Info($"[GeminiAI] Template: {templateSpreadsheetId}");
            AppLogger.Info($"[GeminiAI] Report:   {reportSpreadsheetId}");

            warnings.Add($"⏱ Start: {DateTime.Now:HH:mm:ss}");
            warnings.Add($"📋 Model: {model} | Timeout: {timeoutSeconds}s | Mode: text-with-colors");
            warnings.Add($"📄 Template ID: {templateSpreadsheetId}");
            warnings.Add($"📄 Report ID:   {reportSpreadsheetId}");
            warnings.Add("");

            // ═══ Phase 1: Read sheets with colors ═══
            var phaseSw = Stopwatch.StartNew();
            await authService.EnsureAuthenticatedAsync(ct);
            var sheetsService = authService.SheetsService
                ?? throw new InvalidOperationException("Sheets service not available after authentication.");

            string templateText;
            if (cachedTemplateText != null)
            {
                templateText = cachedTemplateText;
                AppLogger.Info($"[GeminiAI] Phase 1: Using cached template ({templateText.Length:N0} chars)");
                warnings.Add($"✅ Template: {templateText.Length:N0} chars  (cached)");
            }
            else
            {
                onProgress?.Invoke("🔄 שלב 1/4: קורא תבנית עם צבעים...");
                AppLogger.Info("[GeminiAI] Phase 1: Reading template with colors...");
                templateText = await ReadSheetWithColorsAsTextAsync(sheetsService, templateSpreadsheetId, ct);
                AppLogger.Info($"[GeminiAI] Template read: {templateText.Length:N0} chars ({phaseSw.ElapsedMilliseconds}ms)");
                warnings.Add($"✅ Template: {templateText.Length:N0} chars  ({phaseSw.ElapsedMilliseconds:N0}ms)");
                SaveDiag(timestamp, "1_template.txt", templateText);
            }

            phaseSw.Restart();
            onProgress?.Invoke("🔄 שלב 2/4: קורא דוח סופי עם צבעים...");
            AppLogger.Info("[GeminiAI] Phase 2: Reading report with colors...");
            var reportText = await ReadSheetWithColorsAsTextAsync(sheetsService, reportSpreadsheetId, ct);
            AppLogger.Info($"[GeminiAI] Report read: {reportText.Length:N0} chars ({phaseSw.ElapsedMilliseconds}ms)");
            warnings.Add($"✅ Report:   {reportText.Length:N0} chars  ({phaseSw.ElapsedMilliseconds:N0}ms)");
            SaveDiag(timestamp, "2_report.txt", reportText);

            // ═══ Phase 2: Build prompt and call Gemini ═══
            phaseSw.Restart();
            onProgress?.Invoke("🤖 שלב 3/4: שולח טקסט ל-Gemini AI...");
            var prompt = BuildExtractionPrompt(templateText, reportText);
            AppLogger.Info($"[GeminiAI] Phase 3: Sending text to Gemini. Prompt={prompt.Length:N0} chars (~{prompt.Length / 4:N0} tokens est.)");
            warnings.Add($"📝 Prompt: {prompt.Length:N0} chars (~{prompt.Length / 4:N0} tokens est.)");
            SaveDiag(timestamp, "3_prompt.txt", prompt);

            var aiResponseText = await CallGeminiAsync(prompt, warnings, ct);
            AppLogger.Info($"[GeminiAI] Gemini responded: {aiResponseText.Length:N0} chars in {phaseSw.Elapsed.TotalSeconds:F1}s");
            warnings.Add($"✅ Gemini responded: {aiResponseText.Length:N0} chars  ({phaseSw.ElapsedMilliseconds:N0}ms / {phaseSw.Elapsed.TotalSeconds:F1}s)");
            SaveDiag(timestamp, "4_response.json", aiResponseText);

            // ═══ Phase 3: Parse ═══
            phaseSw.Restart();
            onProgress?.Invoke("📊 שלב 4/4: מנתח תוצאות...");
            AppLogger.Info("[GeminiAI] Phase 4: Parsing response...");
            var (sections, extractedFields) = ParseGeminiResponse(aiResponseText, warnings);
            warnings.Add("");
            warnings.Add($"📊 Parsed {sections.Count} sections  ({phaseSw.ElapsedMilliseconds}ms)");

            // ── Per-section summary ──
            var statusGroups = sections.GroupBy(s => s.StatusKey).OrderByDescending(g => g.Count());
            warnings.Add("── Status Breakdown ──");
            foreach (var g in statusGroups)
                warnings.Add($"  {g.Key}: {g.Count()}");

            var withNotes = sections.Count(s => !string.IsNullOrWhiteSpace(s.NoteText));
            var emptyNotes = sections.Count - withNotes;
            warnings.Add($"── Notes: {withNotes} with text, {emptyNotes} empty ──");

            if (sections.Count > 0)
            {
                warnings.Add("");
                warnings.Add("── Section Details ──");
                foreach (var s in sections)
                {
                    var notePreview = string.IsNullOrWhiteSpace(s.NoteText)
                        ? "(empty)"
                        : s.NoteText.Length > 60 ? s.NoteText[..60] + "…" : s.NoteText;
                    warnings.Add($"  [{s.SectionCode}] {s.StatusKey,-18} Row={s.ReportRow,3}  Note: {notePreview}");
                }
            }

            totalSw.Stop();
            warnings.Add("");
            warnings.Add($"⏱ Total: {totalSw.Elapsed.TotalSeconds:F1}s");
            warnings.Add($"📂 Diagnostics: {s_diagFolder}");

            AppLogger.Info($"[GeminiAI] ══════ DONE ══════ {sections.Count} sections, {withNotes} with notes, {totalSw.Elapsed.TotalSeconds:F1}s total");
            foreach (var s in sections)
            {
                var noteSnippet = string.IsNullOrWhiteSpace(s.NoteText) ? "" : $" \"{(s.NoteText.Length > 80 ? s.NoteText[..80] + "…" : s.NoteText)}\"";
                AppLogger.Info($"[GeminiAI]   [{s.SectionCode}/{s.NoteSubIndex}] {s.StatusKey} Row={s.ReportRow}{noteSnippet}");
            }

            // Save comprehensive summary file
            SaveDiag(timestamp, "5_summary.txt", string.Join("\n", warnings));

            return new ReportExtractionResult
            {
                TemplateSpreadsheetId = templateSpreadsheetId,
                ReportSpreadsheetId = reportSpreadsheetId,
                Sections = sections,
                Warnings = warnings,
                IsSuccess = true,
                GeneralFields = BuildGeneralFields(extractedFields, model, prompt.Length, aiResponseText.Length)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            AppLogger.Error(ex, $"[GeminiAI] FAILED after {totalSw.Elapsed.TotalSeconds:F1}s");
            warnings.Add("");
            warnings.Add($"❌ ERROR after {totalSw.Elapsed.TotalSeconds:F1}s");
            warnings.Add($"  Type: {ex.GetType().Name}");
            warnings.Add($"  Message: {ex.Message}");
            if (ex.InnerException != null)
                warnings.Add($"  Inner: {ex.InnerException.Message}");

            var errorDetail = new StringBuilder();
            errorDetail.AppendLine("═══ ERROR LOG ═══");
            errorDetail.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            errorDetail.AppendLine($"Elapsed: {totalSw.Elapsed.TotalSeconds:F1}s");
            errorDetail.AppendLine();
            errorDetail.AppendLine("── Warnings up to error ──");
            foreach (var w in warnings)
                errorDetail.AppendLine(w);
            errorDetail.AppendLine();
            errorDetail.AppendLine("── Full Exception ──");
            errorDetail.AppendLine(ex.ToString());
            SaveDiag(timestamp, "ERROR.txt", errorDetail.ToString());

            return new ReportExtractionResult
            {
                TemplateSpreadsheetId = templateSpreadsheetId,
                ReportSpreadsheetId = reportSpreadsheetId,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Warnings = warnings
            };
        }
    }

    #region Diagnostics

    private static void SaveDiag(string timestamp, string fileName, string content)
    {
        try
        {
            var path = Path.Combine(s_diagFolder, $"{timestamp}_{fileName}");
            File.WriteAllText(path, content, Encoding.UTF8);
        }
        catch { /* diagnostics should never crash the main flow */ }
    }

    #endregion

    #region Sheet Reading

    private static async Task<string> ReadSheetWithColorsAsTextAsync(
        SheetsService sheetsService, string spreadsheetId, CancellationToken ct)
    {
        var getRequest = sheetsService.Spreadsheets.Get(spreadsheetId);
        getRequest.IncludeGridData = true;
        getRequest.Fields = "sheets.properties.title,sheets.data.rowData.values(" +
            "effectiveFormat.backgroundColor,effectiveValue,formattedValue)";

        var spreadsheet = await getRequest.ExecuteAsync(ct);
        var sheet = spreadsheet.Sheets[0];
        var gridData = sheet.Data?.FirstOrDefault();

        if (gridData?.RowData == null) return "(empty sheet)";

        var sb = new StringBuilder();
        sb.AppendLine($"Sheet: {sheet.Properties?.Title ?? "?"}");
        sb.AppendLine();

        int nonEmptyRows = 0;
        for (int r = 0; r < gridData.RowData.Count; r++)
        {
            var rowData = gridData.RowData[r];
            if (rowData?.Values == null) continue;

            var cells = new List<string>();
            bool hasContent = false;

            foreach (var cell in rowData.Values)
            {
                var text = cell?.FormattedValue
                    ?? cell?.EffectiveValue?.StringValue
                    ?? cell?.EffectiveValue?.NumberValue?.ToString()
                    ?? "";

                var bg = cell?.EffectiveFormat?.BackgroundColor;
                string colorTag = "";
                if (bg != null && !IsWhiteOrDefault(bg))
                    colorTag = $"[BG:{ColorToHex(bg)}] ";

                if (!string.IsNullOrWhiteSpace(text)) hasContent = true;
                cells.Add($"{colorTag}{text}");
            }

            if (!hasContent) continue;
            nonEmptyRows++;
            sb.AppendLine($"Row {r + 1}: | {string.Join(" | ", cells)} |");
        }

        AppLogger.Info($"[GeminiAI] Sheet '{sheet.Properties?.Title}': {gridData.RowData.Count} total rows, {nonEmptyRows} non-empty rows, {sb.Length:N0} chars");
        return sb.ToString();
    }

    private static bool IsWhiteOrDefault(SheetsColor color)
    {
        var r = color.Red ?? 1f;
        var g = color.Green ?? 1f;
        var b = color.Blue ?? 1f;
        return r > 0.98f && g > 0.98f && b > 0.98f;
    }

    private static string ColorToHex(SheetsColor color)
    {
        var r = (int)((color.Red ?? 1f) * 255);
        var g = (int)((color.Green ?? 1f) * 255);
        var b = (int)((color.Blue ?? 1f) * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    #endregion

    #region Prompt

    private static string BuildExtractionPrompt(string templateText, string reportText)
    {
        return $$"""
            You are an expert at analyzing Hebrew construction inspection reports (דוחות ביקורת).

            ## Data Format
            Each row is formatted as: `Row N: | cell1 | cell2 | ...`
            Cells with a non-white background color are prefixed: `[BG:#RRGGBB] text`

            ## TEMPLATE (contains <<tags>> that define the structure):
            {{templateText}}

            ## FINAL REPORT (contains actual values with background colors):
            {{reportText}}

            ## CRITICAL: Sub-Row Structure
            Each section (e.g. 1.1) can contain MULTIPLE sub-rows (1.1.1, 1.1.2, 1.1.3...).
            Each sub-row can have a DIFFERENT background color (= different status).
            You MUST return a SEPARATE JSON object for EACH sub-row, NOT one per section.

            Example: Section 1.1 might have:
            - Sub-row 1.1.1 → [BG:#D9EAD3] = Passed
            - Sub-row 1.1.2 → [BG:#EA9999] = Failed, with note text "חסר שילוט..."
            - Sub-row 1.1.3 → [BG:#FCE5CD] = RecurringFailed, with note text "לא תוקן..."
            → This produces 3 JSON objects, each with SectionCode="1.1" but different NoteSubIndex.

            If a section has NO sub-rows (single row only), return one object with empty NoteSubIndex.

            ## Color Meanings (from [BG:...] tags)
            - Green ≈ #D9EAD3 → Passed (מקובל)
            - Red/Pink ≈ #F2CECC / #EA9999 → Failed — has a remark (הערה)
            - Orange ≈ #FCE5CD → Recurring Failed (הערה חוזרת)
            - Light Gray ≈ #EEEEEE → Not Applicable (לא רלוונטי)
            - Dark Gray ≈ #C0C0C0 to #D9D9D9 → Resolved/Closed (נסגר/בוצע)
            - Blue ≈ #A4C2F4 / #C9DAF8 / #CFE2F3 → PartiallyResolved (טופל חלקית)
            - No [BG:] tag (white) → check if section exists but has no status

            ## Text-Based Status Patterns
            - Note containing "בוצע" or "תוקן" (possibly followed by "ב DATE") → Resolved
            - Note containing "בוצע חלקית" → PartiallyResolved
            - Bold black text in notes → important remark (still Failed status)
            - If note text ends with "בוצע ב DD.MM.YY" or "בוצע ב DD.MM.YYYY" → extract that date as ClosedDate

            ## Designer Response Column (תגובת המתכנן)
            The report may have a column titled "תגובת המתכנן" or "תגובת מתכנן".
            If present, extract its text for each section into DesignerResponse.

            ## General Fields (שדות חופשיים)
            The TEMPLATE contains <<tags>> that are NOT section-specific (no Status_ prefix, no $ suffix).
            These are general/header fields like <<שם הפרויקט>>, <<תאריך>>, <<בודק>>, <<כתובת>>, etc.
            Find these tags in the TEMPLATE, locate the corresponding cells in the FINAL REPORT,
            and extract the actual filled-in values.
            Return them as key-value pairs in "generalFields" — the key is the tag name (without << >>).

            ## Task
            1. **General Fields**: Scan the TEMPLATE for all <<tag>> patterns that do NOT start with "Status_" or end with "$".
               For each tag, find the matching position in the FINAL REPORT and read the actual value.
            2. **Sections**: For each section in the TEMPLATE (tags like <<Status_X.Y ...>>):
               a. Find the matching area in the FINAL REPORT
               b. Scan ALL sub-rows within that section (1.1.1, 1.1.2, 1.1.3...)
               c. For EACH sub-row: read the [BG:] color → StatusKey, read note text → NoteText
               d. COPY the note text EXACTLY — every word, every character. Do NOT summarize.
               e. If "בוצע ב DATE" appears in the note, extract the date string as ClosedDate
               f. If a "תגובת המתכנן" column exists, copy its text into DesignerResponse
               g. Return one JSON object per sub-row

            ## JSON Output Format
            Return a JSON **object** (NOT an array) with two keys:
            {
              "generalFields": {
                "שם הפרויקט": "actual project name from report",
                "תאריך": "01/01/2025",
                "בודק": "ישראל ישראלי"
              },
              "sections": [
                {
                  "SectionCode": "1.1",
                  "ChapterTitle": "כללי",
                  "SectionTitle": "הערה כללית",
                  "StatusKey": "Passed",
                  "StatusColorHex": "#D9EAD3",
                  "NoteText": "",
                  "DesignerResponse": "",
                  "ClosedDate": "",
                  "NoteSubIndex": "1.1.1",
                  "IsResolved": false,
                  "ReportRow": 5
                }
              ]
            }

            StatusKey must be one of: Passed, Failed, RecurringFailed, NotApplicable, Resolved, PartiallyResolved, Unknown
            IsResolved = true when StatusKey is "Resolved" OR note contains "בוצע" (but not "בוצע חלקית")
            ClosedDate = date string extracted from "בוצע ב DATE" or "תוקן ב DATE" if present, else empty
            DesignerResponse = text from the designer response column if it exists, else empty
            ReportRow = the Row number from the data above (1-based)

            Return ONLY the JSON object. No markdown fences. No explanation.
            """;
    }

    #endregion

    #region Gemini API

    private async Task<string> CallGeminiAsync(string prompt, List<string> log, CancellationToken ct)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0.1,
                maxOutputTokens = 65536
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        AppLogger.Info($"[GeminiAI] HTTP POST payload: {json.Length:N0} chars");
        log.Add($"📡 HTTP POST payload: {json.Length:N0} chars");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{s_baseUri}{Uri.EscapeDataString(model)}:generateContent?key=***";
        AppLogger.Info($"[GeminiAI] URL: {url}");
        log.Add($"📡 URL: {url}");

        var sw = Stopwatch.StartNew();
        var actualUrl = $"{s_baseUri}{Uri.EscapeDataString(model)}:generateContent?key={apiKey}";
        var response = await _httpClient.PostAsync(actualUrl, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        AppLogger.Info($"[GeminiAI] HTTP {(int)response.StatusCode} {response.StatusCode} — {sw.Elapsed.TotalSeconds:F1}s, {responseText.Length:N0} chars");
        log.Add($"📡 HTTP {(int)response.StatusCode} {response.StatusCode}  ({sw.Elapsed.TotalSeconds:F1}s)");
        log.Add($"📡 Response size: {responseText.Length:N0} chars");

        if (!response.IsSuccessStatusCode)
        {
            AppLogger.Error($"[GeminiAI] HTTP ERROR {(int)response.StatusCode}: {(responseText.Length > 500 ? responseText[..500] : responseText)}");
            log.Add("❌ Error response body (first 500 chars):");
            log.Add($"  {(responseText.Length > 500 ? responseText[..500] + "…" : responseText)}");
            throw new InvalidOperationException(FormatGeminiError(response.StatusCode, responseText));
        }

        using var doc = JsonDocument.Parse(responseText);
        var candidates = doc.RootElement.GetProperty("candidates");
        var firstCandidate = candidates[0];

        // Log usage metadata if available
        if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
        {
            var promptTokens = usage.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : 0;
            var respTokens = usage.TryGetProperty("candidatesTokenCount", out var rt) ? rt.GetInt32() : 0;
            var totalTokens = usage.TryGetProperty("totalTokenCount", out var tt) ? tt.GetInt32() : 0;
            AppLogger.Info($"[GeminiAI] Tokens — prompt: {promptTokens:N0}, response: {respTokens:N0}, total: {totalTokens:N0}");
            log.Add($"🔢 Tokens — prompt: {promptTokens:N0}, response: {respTokens:N0}, total: {totalTokens:N0}");
        }

        // Log finish reason
        if (firstCandidate.TryGetProperty("finishReason", out var reason))
        {
            AppLogger.Info($"[GeminiAI] Finish reason: {reason.GetString()}");
            log.Add($"🏁 Finish reason: {reason.GetString()}");
        }

        var contentProp = firstCandidate.GetProperty("content");
        var parts = contentProp.GetProperty("parts");
        var textPart = parts[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Gemini returned empty text.");

        return textPart;
    }

    private static string FormatGeminiError(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                var code = errorObj.TryGetProperty("code", out var c) ? c.GetInt32().ToString() : "?";
                var status = errorObj.TryGetProperty("status", out var s) ? s.GetString() : null;
                var message = errorObj.TryGetProperty("message", out var m) ? m.GetString() : null;

                var sb = new StringBuilder();
                sb.AppendLine($"שגיאת Gemini API  —  קוד: {code} ({status ?? statusCode.ToString()})");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    sb.AppendLine("הודעה:");
                    sb.AppendLine($"  {message}");
                }
                return sb.ToString().TrimEnd();
            }
        }
        catch { /* fall through */ }

        var preview = responseBody.Length > 400 ? responseBody[..400] + "…" : responseBody;
        return $"שגיאת Gemini API ({statusCode}):\n{preview}";
    }

    #endregion

    #region Response Parsing

    /// <summary>
    /// Merges AI-extracted general fields with AI metadata into a single dictionary.
    /// Extracted report fields come first; AI metadata keys are prefixed with "_".
    /// </summary>
    private static Dictionary<string, string> BuildGeneralFields(
        Dictionary<string, string> extractedFields,
        string aiModel, int promptChars, int responseChars)
    {
        var merged = new Dictionary<string, string>(extractedFields, StringComparer.OrdinalIgnoreCase);

        // AI metadata (prefixed with _ to separate from report data)
        merged["_AI_Model"] = aiModel;
        merged["_AI_Mode"] = "text-with-colors";
        merged["_AI_PromptChars"] = promptChars.ToString();
        merged["_AI_ResponseChars"] = responseChars.ToString();
        merged["_DiagFolder"] = s_diagFolder;

        return merged;
    }

    private static (List<ExtractedSectionData> Sections, Dictionary<string, string> GeneralFields) ParseGeminiResponse(string aiResponse, List<string> warnings)
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Try new format: { "generalFields": {...}, "sections": [...] }
            List<GeminiSectionDto>? items = null;
            Dictionary<string, string>? generalFields = null;

            using var doc = JsonDocument.Parse(aiResponse);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                // New format — object with sections + generalFields
                if (doc.RootElement.TryGetProperty("generalFields", out var gfProp))
                {
                    generalFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in gfProp.EnumerateObject())
                    {
                        var val = prop.Value.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(val))
                            generalFields[prop.Name] = val;
                    }
                    warnings.Add($"📋 General fields extracted: {generalFields.Count}");
                }

                if (doc.RootElement.TryGetProperty("sections", out var secProp))
                {
                    items = JsonSerializer.Deserialize<List<GeminiSectionDto>>(secProp.GetRawText(), jsonOptions);
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // Legacy format — plain array (backward compatible)
                items = JsonSerializer.Deserialize<List<GeminiSectionDto>>(aiResponse, jsonOptions);
                warnings.Add("ℹ AI returned legacy array format (no generalFields).");
            }

            if (items == null || items.Count == 0)
            {
                warnings.Add("AI returned empty or null sections array.");
                return ([], generalFields ?? new(StringComparer.OrdinalIgnoreCase));
            }

            var sections = items.Select(dto =>
            {
                var noteText = dto.NoteText ?? "";
                var designerResponse = dto.DesignerResponse ?? "";

                // Post-process: extract closure date from note text
                DateTime? closedDate = NoteSplitter.ExtractClosureDate(noteText);

                // Post-process: detect "בוצע" / "בוצע חלקית" / "תוקן" from note text
                var statusKey = dto.StatusKey ?? "Unknown";
                var isResolved = dto.IsResolved;

                var executionStatus = NoteSplitter.DetectExecutionStatus(noteText);
                if (executionStatus != null)
                {
                    // Override status if note text contains execution markers
                    if (executionStatus == "Resolved")
                    {
                        isResolved = true;
                        if (statusKey is "Failed" or "Unknown")
                            statusKey = "Resolved";
                    }
                    else if (executionStatus == "PartiallyResolved")
                    {
                        if (statusKey is "Failed" or "Unknown")
                            statusKey = "PartiallyResolved";
                    }
                }

                if (closedDate != null)
                    isResolved = true;

                return new ExtractedSectionData
                {
                    SectionCode = dto.SectionCode ?? "",
                    ChapterTitle = dto.ChapterTitle ?? "",
                    SectionTitle = dto.SectionTitle ?? "",
                    StatusKey = statusKey,
                    StatusColorHex = dto.StatusColorHex ?? "",
                    NoteText = noteText,
                    DesignerResponse = designerResponse,
                    NoteSubIndex = dto.NoteSubIndex ?? "",
                    IsResolved = isResolved,
                    ReportRow = dto.ReportRow,
                    DetectionMethod = "gemini-ai",
                    OriginalCellRef = "",
                    WasSplit = false,
                    SplitIndex = 0,
                    SplitSourceText = "",
                    ClosedDate = closedDate,
                    HeaderValidation = "",
                    TemplateStatusTag = "",
                    TemplateNoteTag = ""
                };
            }).ToList();

            return (sections, generalFields ?? new(StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException ex)
        {
            AppLogger.Error($"[GeminiAI] JSON parse error: {ex.Message}");
            warnings.Add($"JSON parse error: {ex.Message}");
            var preview = aiResponse.Length > 1000 ? aiResponse[..1000] : aiResponse;
            warnings.Add($"Raw AI response (first 1000 chars): {preview}");
            AppLogger.Error($"[GeminiAI] Raw response preview: {preview}");
            return ([], new(StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class GeminiSectionDto
    {
        public string? SectionCode { get; set; }
        public string? ChapterTitle { get; set; }
        public string? SectionTitle { get; set; }
        public string? StatusKey { get; set; }
        public string? StatusColorHex { get; set; }
        public string? NoteText { get; set; }
        public string? DesignerResponse { get; set; }
        public string? ClosedDate { get; set; }
        public string? NoteSubIndex { get; set; }
        public bool IsResolved { get; set; }
        public int ReportRow { get; set; }
    }

    #endregion
}
