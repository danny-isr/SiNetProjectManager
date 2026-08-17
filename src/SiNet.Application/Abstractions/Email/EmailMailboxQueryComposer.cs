namespace SiNet.Application.Abstractions.Email;

/// <summary>Builds Gmail search query strings from <see cref="EmailMailboxQuery"/>.</summary>
public static class EmailMailboxQueryComposer
{
    /// <summary>Full Inbox label (default Scope base). Category is applied separately.</summary>
    public const string InboxQuery = "label:INBOX";

    /// <summary>
    /// Historical Primary-Inbox compound query. Prefer <see cref="InboxQuery"/> + <see cref="EmailMailboxCategory.Primary"/>.
    /// Kept for diagnostics / compatibility callers.
    /// </summary>
    [Obsolete("Use InboxQuery with EmailMailboxCategory.Primary instead.")]
    public const string InboxPrimaryQuery = "label:INBOX category:primary";

    public const string AllMailQuery = "-in:spam -in:trash -in:drafts -in:sent";

    /// <summary>Deprecated compound unread+primary. Prefer Inbox + Category + UnreadOnly.</summary>
    [Obsolete("Use InboxQuery with Category and UnreadOnly instead.")]
    public const string InboxPrimaryUnreadQuery = "label:INBOX category:primary is:unread";

    public const string AllMailUnreadQuery = "-in:spam -in:trash -in:drafts -in:sent is:unread";

    /// <summary>
    /// Maps deprecated <see cref="EmailMailboxScope.Unread"/> to Inbox + UnreadOnly.
    /// Safe to call repeatedly.
    /// </summary>
#pragma warning disable CS0618 // Unread is retained for compatibility mapping
    public static EmailMailboxQuery Normalize(EmailMailboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MailboxScope != EmailMailboxScope.Unread)
            return query;

        return query with
        {
            MailboxScope = EmailMailboxScope.Inbox,
            UnreadOnly = true,
        };
    }
#pragma warning restore CS0618

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
        query = Normalize(query);

        // Exact Message-ID locate must not be AND-ed with AllMail exclusions (-in:sent, …);
        // Gmail's rfc822msgid: operator already searches the account.
        if (!string.IsNullOrWhiteSpace(query.FreeText)
            && query.FreeText.TrimStart().StartsWith("rfc822msgid:", StringComparison.OrdinalIgnoreCase))
        {
            return query.FreeText.Trim();
        }

        var parts = new List<string> { ResolveScopeBaseQuery(query, inboxQueryOverride) };

        var categoryClause = ResolveCategoryClause(query.Category);
        if (categoryClause is not null)
            parts.Add(categoryClause);

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

        if (query.UnreadOnly)
        {
            parts.Add("is:unread");
        }

        return string.Join(' ', parts);
    }

    public static string ResolveScopeBaseQuery(EmailMailboxQuery query, string? inboxQueryOverride)
    {
        query = Normalize(query);
        return query.MailboxScope switch
        {
            EmailMailboxScope.AllMail => AllMailQuery,
            EmailMailboxScope.Label when !string.IsNullOrWhiteSpace(query.LabelName)
                => $"label:{QuoteGmailTerm(query.LabelName.Trim())}",
            EmailMailboxScope.Sent => "in:sent",
            EmailMailboxScope.Inbox => string.IsNullOrWhiteSpace(inboxQueryOverride)
                ? InboxQuery
                : inboxQueryOverride.Trim(),
            _ => string.IsNullOrWhiteSpace(inboxQueryOverride)
                ? InboxQuery
                : inboxQueryOverride.Trim(),
        };
    }

    public static string? ResolveCategoryClause(EmailMailboxCategory category) =>
        category switch
        {
            EmailMailboxCategory.All => null,
            EmailMailboxCategory.Primary => "category:primary",
            EmailMailboxCategory.Updates => "category:updates",
            EmailMailboxCategory.Promotions => "category:promotions",
            EmailMailboxCategory.Social => "category:social",
            EmailMailboxCategory.Forums => "category:forums",
            _ => null,
        };

    public static string BuildUnreadCountQuery(EmailMailboxQuery query, string? inboxQueryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = Normalize(query);

        var baseParts = new List<string> { ResolveScopeBaseQuery(query, inboxQueryOverride) };
        var categoryClause = ResolveCategoryClause(query.Category);
        if (categoryClause is not null)
            baseParts.Add(categoryClause);
        baseParts.Add("is:unread");
        return string.Join(' ', baseParts);
    }

    public static bool HasNonScopeListFilters(EmailMailboxQuery query)
    {
        query = Normalize(query);
        return !string.IsNullOrWhiteSpace(query.Subject)
            || !string.IsNullOrWhiteSpace(query.FromOrTo)
            || !string.IsNullOrWhiteSpace(query.FreeText)
            || query.ProjectLinkFilter != EmailProjectLinkFilter.All
            || query.AttachmentsOnly
            || query.UnreadOnly
            || query.Category != EmailMailboxCategory.All;
    }

    public static string DescribeMailboxScope(EmailMailboxQuery query)
    {
        query = Normalize(query);
        var scope = query.MailboxScope switch
        {
            EmailMailboxScope.Inbox => "Inbox",
            EmailMailboxScope.AllMail => "AllMail",
            EmailMailboxScope.Label => string.IsNullOrWhiteSpace(query.LabelName)
                ? "Label"
                : $"Label:{query.LabelName}",
            EmailMailboxScope.Sent => "Sent",
            _ => query.MailboxScope.ToString(),
        };

        if (query.Category != EmailMailboxCategory.All)
            scope = $"{scope}/{query.Category}";

        if (query.UnreadOnly)
            scope = $"{scope}+Unread";

        return scope;
    }

    private static string QuoteGmailTerm(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
