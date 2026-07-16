using System.Security.Cryptography;
using System.Text;

namespace SiNet.Application.Email;

/// <summary>
/// RFC 2822-first message identity helper (ported from legacy <c>MessageKeyGenerator</c>).
/// </summary>
public static class EmailMessageIdentity
{
    public static string GetMessageUniqueId(string? internetMessageId, string gmailMessageId)
    {
        if (!string.IsNullOrWhiteSpace(internetMessageId))
        {
            var cleaned = internetMessageId.Trim().Trim('<', '>').Trim();
            if (!string.IsNullOrEmpty(cleaned))
            {
                return cleaned.Length <= 255 ? cleaned : cleaned[..255];
            }
        }

        return $"gmail:{gmailMessageId}";
    }

    /// <summary>
    /// Global, cross-mailbox thread business identity, derived from RFC 2822 headers.
    /// Resolution order: first id in <paramref name="references"/> → <paramref name="inReplyTo"/> →
    /// <paramref name="internetMessageId"/> itself (a standalone message is a thread of one).
    /// Ported from legacy <c>MessageKeyGenerator.GetThreadUniqueId</c>.
    /// </summary>
    public static string GetThreadUniqueId(string? references, string? inReplyTo, string internetMessageId)
    {
        if (string.IsNullOrWhiteSpace(internetMessageId))
            throw new ArgumentException("InternetMessageId is required to derive ThreadUniqueId.", nameof(internetMessageId));

        var firstRef = ExtractFirstMessageId(references);
        if (!string.IsNullOrEmpty(firstRef))
            return Truncate(firstRef, 255);

        var parent = CleanMessageId(inReplyTo);
        if (!string.IsNullOrEmpty(parent))
            return Truncate(parent, 255);

        var self = CleanMessageId(internetMessageId)!;
        return Truncate(self, 255);
    }

    /// <summary>
    /// Short, deterministic, filesystem-safe key (first 16 hex chars of SHA256) derived from a
    /// <see cref="GetThreadUniqueId"/> result. Ported from legacy <c>MessageKeyGenerator.GetThreadKey</c>.
    /// </summary>
    public static string GetThreadKey(string threadUniqueId)
    {
        if (string.IsNullOrEmpty(threadUniqueId))
            throw new ArgumentNullException(nameof(threadUniqueId));

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(threadUniqueId));
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..16];
    }

    private static string? ExtractFirstMessageId(string? references)
    {
        if (string.IsNullOrWhiteSpace(references))
            return null;

        foreach (var token in references.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = CleanMessageId(token);
            if (!string.IsNullOrEmpty(cleaned))
                return cleaned;
        }
        return null;
    }

    private static string? CleanMessageId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var cleaned = raw.Trim().Trim('<', '>').Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
