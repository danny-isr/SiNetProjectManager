using SiNet.Domain.ValueObjects;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>Parses RFC 5322 address-list headers (To/Cc) into distinct mailbox addresses.</summary>
public static class EmailHeaderAddressParser
{
    public static IReadOnlyList<string> Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return Array.Empty<string>();

        var parts = SplitAddressList(header);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            if (!EmailAddress.TryParse(part, out var address))
                continue;
            if (seen.Add(address.Value))
                result.Add(address.Value);
        }

        return result;
    }

    /// <summary>
    /// Splits on commas that are outside angle brackets / quotes so display names with commas
    /// do not break the list.
    /// </summary>
    internal static IReadOnlyList<string> SplitAddressList(string header)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var angleDepth = 0;

        foreach (var ch in header)
        {
            if (ch == '"' && angleDepth == 0)
            {
                inQuotes = !inQuotes;
                current.Append(ch);
                continue;
            }

            if (!inQuotes)
            {
                if (ch == '<')
                    angleDepth++;
                else if (ch == '>' && angleDepth > 0)
                    angleDepth--;
                else if (ch == ',' && angleDepth == 0)
                {
                    var piece = current.ToString().Trim();
                    if (piece.Length > 0)
                        parts.Add(piece);
                    current.Clear();
                    continue;
                }
            }

            current.Append(ch);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0)
            parts.Add(last);

        return parts;
    }
}
