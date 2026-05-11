using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNetSQL.Data;
using SiNetSQL.DTOs.Email;
using SiNetSQL.Services.EmailOutbound;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services;

public sealed class InspectionReportEmailBuilder : IInspectionReportEmailBuilder
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory;
    private readonly GoogleAuthService _googleAuthService;
    private readonly IOutboundMailService _mailService;
    private readonly ILogger<InspectionReportEmailBuilder>? _logger;

    public InspectionReportEmailBuilder(
        IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
        GoogleAuthService googleAuthService,
        IOutboundMailService mailService,
        ILogger<InspectionReportEmailBuilder>? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _googleAuthService = googleAuthService ?? throw new ArgumentNullException(nameof(googleAuthService));
        _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));
        _logger = logger;
    }

    public async Task<EmailComposerContext> BuildAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Operation=PrepareInspectionReportEmail ReportId={ReportId} Result=Started", reportId);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var report = await db.InspectionReports
            .AsNoTracking()
            .Include(r => r.Project)
                .ThenInclude(p => p.ProjectPlanners)
                    .ThenInclude(pp => pp.Contacts)
            .Include(r => r.Inspector)
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken)
            ?? throw new InvalidOperationException($"Inspection report {reportId} was not found.");

        if (string.IsNullOrWhiteSpace(report.SentSpreadsheetId))
            throw new InvalidOperationException("יש לייצא את הדוח לפני שליחה במייל.");

        if (string.IsNullOrWhiteSpace(report.SentSpreadsheetUrl))
            throw new InvalidOperationException("לדוח אין קישור Google Sheets שמור. יש לייצא את הדוח מחדש לפני שליחה במייל.");

        var gmailReady = await _mailService.EnsureAuthenticatedAsync("PrepareInspectionReportEmail", cancellationToken);
        if (!gmailReady)
        {
            _logger?.LogWarning(
                "Operation=PrepareInspectionReportEmail Step=EnsureGmailAuthenticated ReportId={ReportId} Result=Failed Reason=GmailLoginFailed",
                report.ReportId);
            throw new InvalidOperationException("לא ניתן לשלוח מייל כי לא בוצעה התחברות ל-Gmail.");
        }

        string pdfPath;
        try
        {
            pdfPath = await ExportSpreadsheetPdfAsync(
                report.SentSpreadsheetId,
                BuildPdfFileName(report.Project?.Number, report.ReportNumber),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Operation=PrepareInspectionReportEmail ReportId={ReportId} ReportNumber={ReportNumber} ProjectId={ProjectId} SpreadsheetId={SpreadsheetId} SpreadsheetUrlExists={SpreadsheetUrlExists} PdfCreated=False Result=Failed Reason={Reason}",
                report.ReportId,
                report.ReportNumber,
                report.ProjectId,
                report.SentSpreadsheetId,
                !string.IsNullOrWhiteSpace(report.SentSpreadsheetUrl),
                ex.Message);
            throw new InvalidOperationException($"יצירת PDF מהדוח נכשלה: {ex.Message}", ex);
        }

        var plannerEmails = report.Project?.ProjectPlanners
            .Select(pp => pp.Contacts?.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var recipientSuggestions = await BuildRecipientSuggestionsAsync(db, report.ProjectId, plannerEmails, cancellationToken);

        var availableFrom = (await _mailService.GetAvailableFromAddressesAsync(cancellationToken)).ToList();
        var from = availableFrom.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(from))
        {
            var currentUser = await _mailService.GetCurrentUserEmailAsync();
            if (!string.IsNullOrWhiteSpace(currentUser) && currentUser != "Unknown")
            {
                from = currentUser;
                availableFrom.Add(currentUser);
            }
        }

        var subjectProject = !string.IsNullOrWhiteSpace(report.Project?.Title)
            ? report.Project.Title
            : report.Project?.NameAndNumber ?? $"פרויקט {report.ProjectId}";

        var inspectorName = !string.IsNullOrWhiteSpace(report.InspectorName)
            ? report.InspectorName
            : report.Inspector?.Name ?? report.Inspector?.LoginName ?? string.Empty;

        var body = $"שלום,{Environment.NewLine}{Environment.NewLine}" +
                   $"מצורף דוח בדיקה מס' {report.ReportNumber}.{Environment.NewLine}{Environment.NewLine}" +
                   $"קישור לדוח Google Sheets:{Environment.NewLine}" +
                   $"{report.SentSpreadsheetUrl}{Environment.NewLine}{Environment.NewLine}" +
                   $"מילוי תגובת המתכנן ישירות בגיליון מזרז את תהליך הבדיקה.{Environment.NewLine}{Environment.NewLine}" +
                   $"בברכה,{Environment.NewLine}" +
                   $"{inspectorName}";

        var pdfInfo = new FileInfo(pdfPath);
        var context = new EmailComposerContext
        {
            EntityType = "InspectionReport",
            EntityId = report.ReportId,
            FromAddress = from,
            AvailableFromAddresses = availableFrom,
            RecipientSuggestions = recipientSuggestions,
            To = plannerEmails,
            Subject = $"דוח בדיקה מס' {report.ReportNumber} - {subjectProject}",
            Body = body,
            RelatedGoogleSheetUrl = report.SentSpreadsheetUrl,
            RelatedPdfPath = pdfPath,
            UserMessage = plannerEmails.Count == 0 ? "לא נמצא מייל מתכנן, נא להשלים נמען ידנית." : null,
            Attachments =
            [
                new EmailAttachmentInfo
                {
                    FileName = pdfInfo.Name,
                    LocalPath = pdfInfo.FullName,
                    ContentType = "application/pdf",
                    SizeBytes = pdfInfo.Length,
                    IsTemporary = true,
                    SourceDescription = "PDF דוח ביקורת"
                }
            ]
        };

        _logger?.LogInformation(
            "Operation=PrepareInspectionReportEmail ReportId={ReportId} ReportNumber={ReportNumber} ProjectId={ProjectId} SpreadsheetId={SpreadsheetId} SpreadsheetUrlExists={SpreadsheetUrlExists} PdfCreated=True PdfPath={PdfPath} ExtraAttachmentsCount=0 PlannerEmailFound={PlannerEmailFound} Result=Success Reason=(none)",
            report.ReportId,
            report.ReportNumber,
            report.ProjectId,
            report.SentSpreadsheetId,
            !string.IsNullOrWhiteSpace(report.SentSpreadsheetUrl),
            pdfPath,
            plannerEmails.Count > 0);

        return context;
    }

    private static async Task<List<EmailRecipientSuggestion>> BuildRecipientSuggestionsAsync(
        SiNetSQLDbContext db,
        int projectId,
        IEnumerable<string> preferredEmails,
        CancellationToken cancellationToken)
    {
        var suggestions = new Dictionary<string, EmailRecipientSuggestion>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in preferredEmails)
            AddEmailSuggestion(suggestions, null, email, "Planner");

        var projectContacts = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Where(p => p.Contacts != null && p.Contacts.Email != null && p.Contacts.Email != "")
            .Select(p => new
            {
                p.Contacts!.FullName,
                p.Contacts.FirstName,
                p.Contacts.Email
            })
            .ToListAsync(cancellationToken);

        foreach (var contact in projectContacts)
            AddEmailSuggestion(suggestions, contact.FullName ?? contact.FirstName, contact.Email, "Project");

        var plannerContacts = await db.ProjectPlanners
            .AsNoTracking()
            .Where(pp => pp.ProjctId == projectId)
            .Where(pp => pp.Contacts != null && pp.Contacts.Email != null && pp.Contacts.Email != "")
            .Select(pp => new
            {
                pp.Contacts!.FullName,
                pp.Contacts.FirstName,
                pp.Contacts.Email
            })
            .ToListAsync(cancellationToken);

        foreach (var contact in plannerContacts)
            AddEmailSuggestion(suggestions, contact.FullName ?? contact.FirstName, contact.Email, "Planner");

        var contacts = await db.Contacts
            .AsNoTracking()
            .Where(c => c.Email != null && c.Email != "")
            .OrderBy(c => c.FullName ?? c.FirstName ?? c.Email)
            .Select(c => new
            {
                c.FullName,
                c.FirstName,
                c.Email
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
            AddEmailSuggestion(suggestions, contact.FullName ?? contact.FirstName, contact.Email, "Contact");

        return suggestions.Values
            .OrderBy(s => s.DisplayName ?? s.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddEmailSuggestion(
        Dictionary<string, EmailRecipientSuggestion> suggestions,
        string? displayName,
        string? value,
        string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var email in value.Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(email) || suggestions.ContainsKey(email))
                continue;

            suggestions[email] = new EmailRecipientSuggestion
            {
                DisplayName = displayName,
                Email = email,
                Source = source
            };
        }
    }

    private async Task<string> ExportSpreadsheetPdfAsync(
        string spreadsheetId,
        string fileName,
        CancellationToken cancellationToken)
    {
        await _googleAuthService.EnsureAuthenticatedAsync(cancellationToken);
        var driveService = _googleAuthService.DriveService
            ?? throw new InvalidOperationException("Google Drive service is not available after authentication.");

        var tempDir = Path.Combine(Path.GetTempPath(), "SiNet", "InspectionReports");
        Directory.CreateDirectory(tempDir);
        var pdfPath = Path.Combine(tempDir, fileName);

        var request = driveService.Files.Export(spreadsheetId, "application/pdf");
        using var stream = new FileStream(pdfPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await request.DownloadAsync(stream, cancellationToken);

        if (stream.Length == 0)
            throw new InvalidOperationException("Google Drive החזיר קובץ PDF ריק.");

        return pdfPath;
    }

    private static string BuildPdfFileName(float? projectNumber, int reportNumber)
    {
        var projectPart = projectNumber.HasValue
            ? projectNumber.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "UnknownProject";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            projectPart = projectPart.Replace(invalid, '_');

        return $"InspectionReport_{projectPart}_Report_{reportNumber}_{DateTime.Now:yyyyMMdd}.pdf";
    }
}
