namespace SiNet.Application.Email;

/// <summary>Pure retention for <see cref="GmailLabelJournalEntry"/> lists (30-day hard cap).</summary>
public static class GmailLabelJournalRetention
{
    public static IReadOnlyList<GmailLabelJournalEntry> Prune(
        IReadOnlyList<GmailLabelJournalEntry> entries,
        DateTime utcNow,
        int retentionDays = IGmailLabelChangeJournal.RetentionDays)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (retentionDays <= 0)
            return entries;

        var cutoff = utcNow.AddDays(-retentionDays);
        return entries
            .Where(e => e.ChangedAtUtc >= cutoff)
            .OrderBy(e => e.ChangedAtUtc)
            .ToList();
    }

    /// <summary>Safe file-name fragment from a mailbox email.</summary>
    public static string SanitizeMailboxFileName(string mailboxEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxEmail);
        var invalid = Path.GetInvalidFileNameChars();
        var chars = mailboxEmail.Trim().ToLowerInvariant().Select(c =>
            invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var name = new string(chars);
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }
}
