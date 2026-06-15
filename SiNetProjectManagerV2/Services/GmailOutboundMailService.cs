using System.Text;
using System.IO;
using System.Net.Mail;
using Google.Apis.Gmail.v1.Data;
using SiNetSQL.DTOs.Email;
using SiNetSQL.Services.EmailOutbound;
using SiOffice.GoogleConnector;
using SiOffice.GoogleConnector.Logging;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Sends outbound email through the existing authenticated Gmail API session.
/// </summary>
public sealed class GmailOutboundMailService : IOutboundMailService
{
    private readonly GoogleService _googleService;
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    public GmailOutboundMailService(GoogleService googleService)
    {
        _googleService = googleService ?? throw new ArgumentNullException(nameof(googleService));
    }

    public bool IsAuthenticated => _googleService.IsAuthenticated;

    public bool IsServiceAvailable => _googleService.IsGmailServiceAvailable;

    public async Task<bool> EnsureAuthenticatedAsync(
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ReportLogger.Info(
            $"Operation={operationName} Step=EnsureGmailAuthenticated GoogleServiceLoggedIn={_googleService.IsAuthenticated} " +
            $"GmailServiceAvailable={_googleService.IsGmailServiceAvailable} Result=Started Reason=(none)");

        if (_googleService.IsAuthenticated && _googleService.IsGmailServiceAvailable)
        {
            ReportLogger.Info(
                $"Operation={operationName} Step=EnsureGmailAuthenticated GoogleServiceLoggedIn=True GmailServiceAvailable=True Result=Success Reason=AlreadyAuthenticated");
            return true;
        }

        await _loginGate.WaitAsync(cancellationToken);
        try
        {
            if (_googleService.IsAuthenticated && _googleService.IsGmailServiceAvailable)
            {
                ReportLogger.Info(
                    $"Operation={operationName} Step=EnsureGmailAuthenticated GoogleServiceLoggedIn=True GmailServiceAvailable=True Result=Success Reason=AlreadyAuthenticatedAfterWait");
                return true;
            }

            var credentialsPath = ResolveGoogleCredentialsPath();
            if (string.IsNullOrWhiteSpace(credentialsPath))
            {
                ReportLogger.Warn(
                    $"Operation={operationName} Step=EnsureGmailAuthenticated GoogleServiceLoggedIn={_googleService.IsAuthenticated} " +
                    $"GmailServiceAvailable={_googleService.IsGmailServiceAvailable} Result=Failed Reason=CredentialsNotFound");
                return false;
            }

            await _googleService.LoginAsync(credentialsPath);
            var success = _googleService.IsAuthenticated && _googleService.IsGmailServiceAvailable;

            ReportLogger.Info(
                $"Operation={operationName} Step=EnsureGmailAuthenticated GoogleServiceLoggedIn={_googleService.IsAuthenticated} " +
                $"GmailServiceAvailable={_googleService.IsGmailServiceAvailable} Result={(success ? "Success" : "Failed")} Reason={(success ? "LoginCompleted" : "LoginDidNotInitializeGmail")}");

            return success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ReportLogger.Warn(
                $"Operation={operationName} Step=EnsureGmailAuthenticated GoogleServiceLoggedIn={_googleService.IsAuthenticated} " +
                $"GmailServiceAvailable={_googleService.IsGmailServiceAvailable} Result=Failed Reason={ex.Message}");
            return false;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    public async Task<EmailSendResult> SendAsync(
        EmailSendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isAuthenticated = await EnsureAuthenticatedAsync("SendEmailViaGmailApi", cancellationToken);
        if (!isAuthenticated)
        {
            const string message = "לא ניתן לשלוח מייל כי לא בוצעה התחברות ל-Gmail.";
            LogSendAttempt(request, null, message);
            return EmailSendResult.Failed(message);
        }

        var validationError = ValidateRequest(request);
        if (validationError != null)
        {
            LogSendAttempt(request, null, validationError);
            return EmailSendResult.Failed(validationError);
        }

        var totalAttachmentBytes = request.Attachments.Sum(a => a.SizeBytes);

        try
        {
            var rawMessage = BuildRawMessage(request);
            var gmailMessage = new Message { Raw = rawMessage };
            if (!string.IsNullOrEmpty(request.ThreadId))
            {
                gmailMessage.ThreadId = request.ThreadId;
            }

            var sent = await _googleService.SendRawMessageAsync(
                gmailMessage,
                request.RelatedEntityType,
                request.RelatedEntityId,
                cancellationToken);

            var result = new EmailSendResult
            {
                Success = true,
                GmailMessageId = sent.Id,
                GmailThreadId = sent.ThreadId,
                SentAtUtc = DateTime.UtcNow
            };

            ReportLogger.Info(
                $"Operation=SendEmailViaGmailApi EntityType={request.RelatedEntityType ?? "(none)"} EntityId={request.RelatedEntityId?.ToString() ?? "(none)"} " +
                $"From={request.FromAddress ?? "(default)"} ToCount={request.To.Count} CcCount={request.Cc.Count} BccCount={request.Bcc.Count} " +
                $"Subject={request.Subject} AttachmentsCount={request.Attachments.Count} TotalAttachmentBytes={totalAttachmentBytes} " +
                $"GmailMessageId={result.GmailMessageId ?? "(none)"} GmailThreadId={result.GmailThreadId ?? "(none)"} Result=Success Reason=(none)");

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = GetUserFacingError(ex);
            LogSendAttempt(request, null, message);
            return EmailSendResult.Failed(message);
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableFromAddressesAsync(CancellationToken cancellationToken = default)
    {
        var isAuthenticated = await EnsureAuthenticatedAsync("PrepareInspectionReportEmail", cancellationToken);
        if (!isAuthenticated)
            return [];

        var addresses = await _googleService.GetSendAsAddressesAsync(cancellationToken);
        return addresses;
    }

    public async Task<string> GetCurrentUserEmailAsync()
    {
        var isAuthenticated = await EnsureAuthenticatedAsync("PrepareInspectionReportEmail", CancellationToken.None);
        return isAuthenticated ? await _googleService.GetCurrentUserEmailAsync() : "Unknown";
    }

    private static string? ResolveGoogleCredentialsPath()
    {
        var configured = AppConfiguration.GetGoogleClientSecretsPath();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var credentialsPaths = new[]
        {
            "credentials.json",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiOffice", "credentials.json")
        };

        return credentialsPaths.FirstOrDefault(File.Exists);
    }

    private static string? ValidateRequest(EmailSendRequest request)
    {
        if (request.To.Count == 0 || request.To.All(string.IsNullOrWhiteSpace))
            return "יש להזין לפחות נמען אחד בשדה To.";

        if (string.IsNullOrWhiteSpace(request.Subject))
            return "יש להזין נושא למייל.";

        if (string.IsNullOrWhiteSpace(request.BodyHtml) && string.IsNullOrWhiteSpace(request.BodyText))
            return "יש להזין תוכן למייל.";

        var invalidAddresses = request.To
            .Concat(request.Cc)
            .Concat(request.Bcc)
            .Where(a => !IsValidEmailAddress(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalidAddresses.Count > 0)
            return $"נמצאו כתובות מייל לא תקינות: {string.Join(", ", invalidAddresses)}";

        foreach (var attachment in request.Attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
                return $"הקובץ המצורף לא נמצא: {attachment.FileName}";
        }

        return null;
    }

    private static bool IsValidEmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string BuildRawMessage(EmailSendRequest request)
    {
        var boundary = "----=_SiNet_" + Guid.NewGuid().ToString("N");
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            if (!string.IsNullOrWhiteSpace(request.FromAddress))
                writer.WriteLine($"From: {request.FromAddress.Trim()}");
            writer.WriteLine($"To: {string.Join(", ", request.To)}");
            if (request.Cc.Count > 0) writer.WriteLine($"Cc: {string.Join(", ", request.Cc)}");
            if (request.Bcc.Count > 0) writer.WriteLine($"Bcc: {string.Join(", ", request.Bcc)}");
            writer.WriteLine($"Subject: {EncodeHeader(request.Subject.Trim())}");
            if (!string.IsNullOrWhiteSpace(request.InReplyTo))
                writer.WriteLine($"In-Reply-To: {request.InReplyTo.Trim()}");
            if (!string.IsNullOrWhiteSpace(request.References))
                writer.WriteLine($"References: {request.References.Trim()}");
            writer.WriteLine("MIME-Version: 1.0");

            if (request.Attachments.Count == 0)
            {
                writer.WriteLine("Content-Type: text/plain; charset=utf-8");
                writer.WriteLine("Content-Transfer-Encoding: base64");
                writer.WriteLine();
                writer.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(request.BodyText ?? string.Empty)));
            }
            else
            {
                writer.WriteLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
                writer.WriteLine();
                writer.WriteLine($"--{boundary}");
                writer.WriteLine("Content-Type: text/plain; charset=utf-8");
                writer.WriteLine("Content-Transfer-Encoding: base64");
                writer.WriteLine();
                writer.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(request.BodyText ?? string.Empty)));

                foreach (var attachment in request.Attachments)
                {
                    var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
                        ? "application/octet-stream"
                        : attachment.ContentType;
                    var fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                        ? Path.GetFileName(attachment.LocalPath)
                        : attachment.FileName;

                    writer.WriteLine($"--{boundary}");
                    writer.WriteLine($"Content-Type: {contentType}; name=\"{EscapeQuoted(fileName)}\"");
                    writer.WriteLine("Content-Transfer-Encoding: base64");
                    writer.WriteLine($"Content-Disposition: attachment; filename=\"{EscapeQuoted(fileName)}\"");
                    writer.WriteLine();
                    writer.Flush();

                    var attachmentBytes = File.ReadAllBytes(attachment.LocalPath);
                    writer.WriteLine(Convert.ToBase64String(attachmentBytes, Base64FormattingOptions.InsertLineBreaks));
                }

                writer.WriteLine($"--{boundary}--");
            }
        }

        return Base64UrlEncode(stream.ToArray());
    }

    private static string EncodeHeader(string value)
    {
        if (value.All(c => c <= 127))
            return value;

        return $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
    }

    private static string EscapeQuoted(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .Replace('+', '-')
        .Replace('/', '_')
        .Replace("=", string.Empty);

    private static string GetUserFacingError(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("sendAs", StringComparison.OrdinalIgnoreCase)
            || message.Contains("From", StringComparison.OrdinalIgnoreCase))
        {
            return "אין הרשאה לשלוח מהכתובת שנבחרה. בחר כתובת שליחה מורשית ונסה שוב.";
        }

        if (message.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
            || message.Contains("scope", StringComparison.OrdinalIgnoreCase))
        {
            return "חסרה הרשאת Gmail לשליחת מיילים. ייתכן שנדרש אישור OAuth מחדש.";
        }

        return $"שליחת המייל נכשלה: {message}";
    }

    private static void LogSendAttempt(EmailSendRequest request, string? gmailMessageId, string reason)
    {
        var totalAttachmentBytes = request.Attachments.Sum(a => a.SizeBytes);
        ReportLogger.Warn(
            $"Operation=SendEmailViaGmailApi EntityType={request.RelatedEntityType ?? "(none)"} EntityId={request.RelatedEntityId?.ToString() ?? "(none)"} " +
            $"From={request.FromAddress ?? "(default)"} ToCount={request.To.Count} CcCount={request.Cc.Count} BccCount={request.Bcc.Count} " +
            $"Subject={request.Subject} AttachmentsCount={request.Attachments.Count} TotalAttachmentBytes={totalAttachmentBytes} " +
            $"GmailMessageId={gmailMessageId ?? "(none)"} GmailThreadId=(none) Result=Failed Reason={reason}");
    }
}
