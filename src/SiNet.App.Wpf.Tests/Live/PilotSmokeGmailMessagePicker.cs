using SiNet.Application.Abstractions.Email;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Resolves which Gmail message the L4W tier operates on. Supports an explicit subject token or
/// automatic selection of the newest AllMail message that carries business attachments.
/// </summary>
internal static class PilotSmokeGmailMessagePicker
{
    internal sealed record ChosenMessage(
        string MessageId,
        string ThreadId,
        string? InternetMessageId,
        string Subject,
        int AttachmentCount,
        string SelectionMode);

    internal static bool IsAutoSubjectToken(string? subjectToken) =>
        string.IsNullOrWhiteSpace(subjectToken)
        || string.Equals(subjectToken, "*", StringComparison.Ordinal)
        || string.Equals(subjectToken, "AUTO", StringComparison.OrdinalIgnoreCase);

    internal static async Task<ChosenMessage> ResolveAsync(
        IEmailGateway gateway,
        string? subjectToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        if (!IsAutoSubjectToken(subjectToken))
        {
            var bySubject = await QueryAsync(
                gateway,
                subject: subjectToken,
                attachmentsOnly: false,
                pageSize: 50,
                cancellationToken);

            var subjectMatches = bySubject
                .Where(m => m.Subject.Contains(subjectToken!, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.ReceivedAt)
                .ToList();

            var withAttachments = subjectMatches.FirstOrDefault(m => m.AttachmentCount > 0);
            if (withAttachments is not null)
            {
                return ToChosen(withAttachments, $"subject token '{subjectToken}' (with attachments)");
            }

            if (subjectMatches.Count > 0)
            {
                var newest = subjectMatches[0];
                return ToChosen(
                    newest,
                    $"subject token '{subjectToken}' (newest match; no Gmail-reported attachments — ingest may still create body PDF)");
            }
        }

        var autoCandidates = await QueryAsync(
            gateway,
            subject: null,
            attachmentsOnly: true,
            pageSize: 100,
            cancellationToken);

        var auto = autoCandidates
            .OrderByDescending(m => m.ReceivedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No message with attachments found in AllMail. The smoke needs at least one inbound "
                + "message with a business attachment in the declared mailbox.");

        return ToChosen(auto, "auto: newest AllMail message with attachments");
    }

    private static async Task<IReadOnlyList<EmailSummary>> QueryAsync(
        IEmailGateway gateway,
        string? subject,
        bool attachmentsOnly,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await gateway.GetMailboxPageAsync(
            new EmailMailboxQuery
            {
                Subject = subject,
                MailboxScope = EmailMailboxScope.AllMail,
                AttachmentsOnly = attachmentsOnly,
                PageSize = pageSize,
            },
            pageToken: null,
            cancellationToken);

        return page.Items;
    }

    private static ChosenMessage ToChosen(EmailSummary item, string mode) =>
        new(
            item.MessageId,
            item.ThreadId,
            item.InternetMessageId,
            item.Subject,
            item.AttachmentCount,
            mode);
}
