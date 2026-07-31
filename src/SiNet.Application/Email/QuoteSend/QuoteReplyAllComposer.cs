using SiNet.Application.Abstractions.Email;
using SiNet.Domain.ValueObjects;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>Builds Reply-All / new-compose drafts for Proposal SendQuote.</summary>
public static class QuoteReplyAllComposer
{
    public static QuoteSendComposeDraft BuildReplyAll(
        EmailMessageDetails source,
        string? currentUserEmail,
        int projectId,
        string marker)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        var self = Normalize(currentUserEmail);
        var to = new List<string>();
        var cc = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddUnique(List<string> target, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;
            if (!EmailAddress.TryParse(raw, out var address))
                return;
            var value = address.Value;
            if (IsSelf(value, self))
                return;
            if (!seen.Add(value))
                return;
            target.Add(value);
        }

        AddUnique(to, source.From.Value);
        foreach (var addr in source.ToAddresses)
            AddUnique(cc, addr);
        foreach (var addr in source.CcAddresses)
            AddUnique(cc, addr);

        // If From was self (rare), promote first Cc into To so the draft stays sendable.
        if (to.Count == 0 && cc.Count > 0)
        {
            to.Add(cc[0]);
            cc.RemoveAt(0);
        }

        var subject = BuildReplySubject(source.Subject);
        var body = BuildBody(marker, isReply: true);
        return new QuoteSendComposeDraft(
            Mode: QuoteSendComposeMode.ReplyAll,
            To: to,
            Cc: cc,
            Subject: subject,
            Body: body,
            ThreadId: source.ThreadId,
            InReplyToMessageId: source.InternetMessageId,
            SourceGmailMessageId: source.MessageId,
            SourceInboxMessageId: null,
            Marker: marker,
            ProjectId: projectId);
    }

    public static QuoteSendComposeDraft BuildNewCompose(int projectId, string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        return new QuoteSendComposeDraft(
            Mode: QuoteSendComposeMode.NewCompose,
            To: Array.Empty<string>(),
            Cc: Array.Empty<string>(),
            Subject: $"הצעת מחיר — פרויקט {projectId}",
            Body: BuildBody(marker, isReply: false),
            ThreadId: null,
            InReplyToMessageId: null,
            SourceGmailMessageId: null,
            SourceInboxMessageId: null,
            Marker: marker,
            ProjectId: projectId);
    }

    public static string BuildBody(string marker, bool isReply)
    {
        var greeting = isReply
            ? "שלום," + Environment.NewLine + Environment.NewLine +
              "מצורפת הצעת מחיר בהמשך לפנייה."
            : "שלום," + Environment.NewLine + Environment.NewLine +
              "מצורפת הצעת מחיר.";

        return greeting + Environment.NewLine + Environment.NewLine +
               "---" + Environment.NewLine +
               $"סימן מעקב: {marker}";
    }

    public static string BuildReplySubject(string? originalSubject)
    {
        var subject = (originalSubject ?? string.Empty).Trim();
        if (subject.Length == 0)
            return "Re: הצעת מחיר";

        if (subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            || subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase)
            || subject.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }

        return "Re: " + subject;
    }

    private static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        return EmailAddress.TryParse(email, out var address) ? address.Value : email.Trim();
    }

    private static bool IsSelf(string address, string? self) =>
        self is not null && string.Equals(address, self, StringComparison.OrdinalIgnoreCase);
}
