namespace SiNet.Application.Abstractions.Email;

/// <summary>Builds Gmail search query strings from <see cref="EmailMailboxQuery"/>.</summary>
public static class EmailMailboxQueryComposer
{
    public const string InboxPrimaryQuery = "label:INBOX category:primary";
    public const string AllMailQuery = "-in:spam -in:trash -in:drafts -in:sent";
    public const string InboxPrimaryUnreadQuery = "label:INBOX category:primary is:unread";
    public const string AllMailUnreadQuery = "-in:spam -in:trash -in:drafts -in:sent is:unread";

    public static string BuildSearchQuery(EmailMailboxQuery query, string? inboxQueryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(query);

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
            _ => query.MailboxScope.ToString(),
        };

    private static string QuoteGmailTerm(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
