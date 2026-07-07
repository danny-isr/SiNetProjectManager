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
}
