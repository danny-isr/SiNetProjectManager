namespace SiNet.Application.Email;

/// <summary>Gmail API system labels excluded from the mailbox label audit table (DEV-026).</summary>
public static class GmailSystemLabelNames
{
    private static readonly HashSet<string> KnownSystem = new(StringComparer.OrdinalIgnoreCase)
    {
        "INBOX",
        "SENT",
        "DRAFT",
        "SPAM",
        "TRASH",
        "UNREAD",
        "STARRED",
        "IMPORTANT",
        "CHAT",
        "CATEGORY_PERSONAL",
        "CATEGORY_SOCIAL",
        "CATEGORY_PROMOTIONS",
        "CATEGORY_UPDATES",
        "CATEGORY_FORUMS",
    };

    public static bool IsSystemLabel(string? labelName, string? labelType = null)
    {
        if (string.Equals(labelType, "system", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(labelName))
        {
            return true;
        }

        var name = labelName.Trim();
        if (KnownSystem.Contains(name))
        {
            return true;
        }

        return name.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase);
    }
}
