namespace SiNet.Application.Abstractions.Email;

/// <summary>Builds Gmail search query strings from <see cref="EmailMailboxQuery"/>.</summary>
public static class EmailMailboxQueryComposer
{
    public const string InboxPrimaryQuery = "label:INBOX category:primary";
    public const string AllMailQuery = "-in:spam -in:trash -in:drafts -in:sent";
    public const string InboxPrimaryUnreadQuery = "label:INBOX category:primary is:unread";
    public const string AllMailUnreadQuery = "-in:spam -in:trash -in:drafts -in:sent is:unread";

    /// <summary>
    /// Builds a Gmail <c>rfc822msgid:</c> operator for exact Message-ID locate.
    /// Always quotes the id — Message-IDs contain <c>@</c> and mailbox tokens that break unquoted search.
    /// </summary>
    public static string BuildRfc822MessageIdSearchTerm(string internetMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internetMessageId);
        var raw = internetMessageId.Trim().Trim('<', '>').Trim();
        if (string.IsNullOrEmpty(raw))
            throw new ArgumentException("Internet Message-ID is empty after trim.", nameof(internetMessageId));

        // Strip embedded quotes so the operator stays well-formed.
        raw = raw.Replace("\"", string.Empty, StringComparison.Ordinal);
        return $"rfc822msgid:\"{raw}\"";
    }

    /// <summary>
    /// True when <paramref name="messageUniqueId"/> is a Gmail API id usable with Messages.Get
    /// (prefix <c>gmail:</c>, or a non-RFC822 token). RFC822 ids (contain <c>@</c>) must use
    /// <see cref="BuildRfc822MessageIdSearchTerm"/> instead — see <c>EmailMessageIdentity.GetMessageUniqueId</c>.
    /// </summary>
    public static string? TryGetGmailApiMessageId(string? messageUniqueId)
    {
        if (string.IsNullOrWhiteSpace(messageUniqueId))
            return null;

        var trimmed = messageUniqueId.Trim();
        const string gmailPrefix = "gmail:";
        if (trimmed.StartsWith(gmailPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = trimmed[gmailPrefix.Length..].Trim();
            return string.IsNullOrEmpty(id) ? null : id;
        }

        // RFC822 Message-ID always contains @; Gmail API ids do not.
        if (trimmed.Contains('@', StringComparison.Ordinal))
            return null;

        return trimmed;
    }

    public static string BuildSearchQuery(EmailMailboxQuery query, string? inboxQueryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Exact Message-ID locate must not be AND-ed with AllMail exclusions (-in:sent, …);
        // Gmail's rfc822msgid: operator already searches the account.
        if (!string.IsNullOrWhiteSpace(query.FreeText)
            && query.FreeText.TrimStart().StartsWith("rfc822msgid:", StringComparison.OrdinalIgnoreCase))
        {
            return query.FreeText.Trim();
        }

        var parts = new List<string> { ResolveScopeBaseQuery(query, inboxQueryOverride) };

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            parts.Add($"subject:{QuoteGmailTerm(query.Subject.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.FromOrTo))
        {
            var address = query.FromOrTo.Trim();
            parts.Add($"from:{QuoteGmailTerm(address)} OR to:{QuoteGmailTerm(address)}");
        }

        if (!string.IsNullOrWhiteSpace(query.FreeText))
        {
            parts.Add(query.FreeText.Trim());
        }

        if (query.AttachmentsOnly)
        {
            parts.Add("has:attachment");
        }

        if (query.UnreadOnly && query.MailboxScope != EmailMailboxScope.Unread)
        {
            parts.Add("is:unread");
        }

        return string.Join(' ', parts);
    }

    public static string ResolveScopeBaseQuery(EmailMailboxQuery query, string? inboxQueryOverride)
    {
        return query.MailboxScope switch
        {
            EmailMailboxScope.AllMail => AllMailQuery,
            EmailMailboxScope.Unread => InboxPrimaryUnreadQuery,
            EmailMailboxScope.Label when !string.IsNullOrWhiteSpace(query.LabelName)
                => $"label:{QuoteGmailTerm(query.LabelName.Trim())}",
            EmailMailboxScope.Sent => "in:sent",
            EmailMailboxScope.Inbox => string.IsNullOrWhiteSpace(inboxQueryOverride)
                ? InboxPrimaryQuery
                : inboxQueryOverride.Trim(),
            _ => string.IsNullOrWhiteSpace(inboxQueryOverride)
                ? InboxPrimaryQuery
                : inboxQueryOverride.Trim(),
        };
    }

    public static string BuildUnreadCountQuery(EmailMailboxQuery query, string? inboxQueryOverride = null) =>
        query.MailboxScope switch
        {
            EmailMailboxScope.AllMail => AllMailUnreadQuery,
            EmailMailboxScope.Unread => InboxPrimaryUnreadQuery,
            EmailMailboxScope.Label when !string.IsNullOrWhiteSpace(query.LabelName)
                => $"{ResolveScopeBaseQuery(query, inboxQueryOverride)} is:unread",
            EmailMailboxScope.Inbox => InboxPrimaryUnreadQuery,
            _ => InboxPrimaryUnreadQuery,
        };

    public static bool HasNonScopeListFilters(EmailMailboxQuery query) =>
        !string.IsNullOrWhiteSpace(query.Subject)
        || !string.IsNullOrWhiteSpace(query.FromOrTo)
        || !string.IsNullOrWhiteSpace(query.FreeText)
        || query.ProjectLinkFilter != EmailProjectLinkFilter.All
        || query.AttachmentsOnly
        || (query.UnreadOnly && query.MailboxScope != EmailMailboxScope.Unread);

    public static string DescribeMailboxScope(EmailMailboxQuery query) =>
        query.MailboxScope switch
        {
            EmailMailboxScope.Inbox => "Inbox",
            EmailMailboxScope.AllMail => "AllMail",
            EmailMailboxScope.Unread => "Unread",
            EmailMailboxScope.Label => string.IsNullOrWhiteSpace(query.LabelName)
                ? "Label"
                : $"Label:{query.LabelName}",
            EmailMailboxScope.Sent => "Sent",
            _ => query.MailboxScope.ToString(),
        };

    private static string QuoteGmailTerm(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
