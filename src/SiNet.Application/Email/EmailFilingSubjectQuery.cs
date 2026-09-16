namespace SiNet.Application.Email;

/// <summary>Deterministic Subject → project-picker search text. No LLM.</summary>
public static class EmailFilingSubjectQuery
{
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "re", "fw", "fwd", "the", "a", "an", "and", "or", "of", "to", "for", "on", "in", "at",
        "please", "hello", "hi", "update", "email", "mail", "regarding", "subject",
        "שלום", "היי", "עדכון", "מייל", "בנוגע", "של", "את", "על", "עם", "אל",
    };

    public static string CleanSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        var text = subject.Trim();
        while (true)
        {
            var next = StripOnePrefix(text);
            if (string.Equals(next, text, StringComparison.Ordinal))
            {
                break;
            }

            text = next;
        }

        return text.Trim();
    }

    public static IReadOnlyList<string> MeaningfulTokens(string cleanedSubject)
    {
        if (string.IsNullOrWhiteSpace(cleanedSubject))
        {
            return [];
        }

        return cleanedSubject
            .Split([' ', '\t', '\r', '\n', ',', ';', '/', '\\', '|', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static token => token.Trim('(', ')', '[', ']', '"', '\'', ':', '?', '!'))
            .Where(static token => token.Length >= 2 && !NoiseTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Narrows tokens while <paramref name="countMatches"/> returns &gt; 0.
    /// Stops at one result. Zero results keep the last non-empty query.
    /// </summary>
    public static string BuildSearchText(string? subject, Func<string, int> countMatches)
    {
        ArgumentNullException.ThrowIfNull(countMatches);
        var cleaned = CleanSubject(subject);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var tokens = MeaningfulTokens(cleaned);
        if (tokens.Count == 0)
        {
            return cleaned;
        }

        var best = cleaned;
        var current = string.Empty;
        foreach (var token in tokens)
        {
            var candidate = string.IsNullOrEmpty(current) ? token : current + " " + token;
            var count = countMatches(candidate);
            if (count <= 0)
            {
                break;
            }

            current = candidate;
            best = candidate;
            if (count == 1)
            {
                break;
            }
        }

        return best;
    }

    private static string StripOnePrefix(string text)
    {
        foreach (var prefix in new[] { "RE:", "FW:", "FWD:", "העברה:", "תשובה:" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return text[prefix.Length..].TrimStart();
            }
        }

        return text;
    }
}
