using System.Security.Cryptography;
using System.Text;
using System.IO;

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

    /// <summary>
    /// Short ACC folder key from <paramref name="messageUniqueId"/> (first 16 hex chars of SHA256).
    /// Ported from legacy <c>MessageKeyGenerator.GetMessageKey</c>.
    /// </summary>
    public static string GetMessageKey(string messageUniqueId)
    {
        if (string.IsNullOrEmpty(messageUniqueId))
            throw new ArgumentNullException(nameof(messageUniqueId));

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(messageUniqueId));
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..16];
    }

    /// <summary>SHA-256 of <paramref name="data"/> as lowercase hex (64 chars).</summary>
    public static string ComputeSha256Hex(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new ArgumentException("Data must not be empty.", nameof(data));

        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    /// <summary>Sanitizes a filename for ACC / filesystem storage.</summary>
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed_attachment";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName);
        foreach (var c in invalidChars)
        {
            sanitized.Replace(c, '_');
        }

        sanitized.Replace(':', '_');
        sanitized.Replace(';', '_');
        sanitized.Replace('#', '_');

        var result = sanitized.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "unnamed_attachment" : result;
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
