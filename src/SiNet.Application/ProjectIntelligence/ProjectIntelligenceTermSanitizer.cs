using SiNet.Application.Email;

namespace SiNet.Application.ProjectIntelligence;

public static class ProjectIntelligenceTermSanitizer
{
    public const int MaxAiTerms = 16;
    public const int MaxTokensInTerm = 6;

    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "re", "fw", "fwd", "the", "a", "an", "and", "or", "of", "to", "for", "on", "in", "at",
        "please", "hello", "hi", "update", "email", "mail", "regarding", "subject",
        "שלום", "היי", "עדכון", "מייל", "בנוגע", "של", "את", "על", "עם", "אל",
        "פרויקט", "תכנון", "בקשה", "הצעת", "הצעה", "מחיר", "הנדסה", "בדיקה", "הגשה", "מלאה", "עדכני", "נספח",
        "תנועה",
        "כולל", "מסמך", "מסמכים", "קובץ", "קבצים", "דוח", "דו״ח", "חוות", "דעת",
        "תודה", "בברכה", "נא", "אנא", "דחוף", "חדש", "ישן", "גרסה", "סופי",
        "project", "planning", "request", "engineering", "check", "file", "files",
    };

    public static IReadOnlyList<string> Sanitize(
        IEnumerable<string?> candidates,
        IReadOnlySet<string> foreignProjectNumbers,
        IReadOnlySet<int> foreignProjectIds)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(foreignProjectNumbers);
        ArgumentNullException.ThrowIfNull(foreignProjectIds);

        var accepted = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in candidates)
        {
            if (accepted.Count >= MaxAiTerms)
            {
                break;
            }

            var term = ProjectIntelligenceText.Clean(raw);
            if (!IsUseful(term, foreignProjectNumbers, foreignProjectIds) || !seen.Add(term))
            {
                continue;
            }

            accepted.Add(term);
        }

        return accepted;
    }

    public static bool IsGenericToken(string token)
        => string.IsNullOrWhiteSpace(token) || Generic.Contains(token.Trim());

    public static bool IsUseful(
        string? term,
        IReadOnlySet<string> foreignProjectNumbers,
        IReadOnlySet<int> foreignProjectIds)
    {
        ArgumentNullException.ThrowIfNull(foreignProjectNumbers);
        ArgumentNullException.ThrowIfNull(foreignProjectIds);

        var cleaned = ProjectIntelligenceText.Clean(term);
        if (cleaned.Length < 3 || cleaned.Length > 80)
        {
            return false;
        }

        var tokens = EmailFilingSubjectQuery.MeaningfulTokens(cleaned);
        if (tokens.Count == 0 || tokens.Count > MaxTokensInTerm)
        {
            return false;
        }

        if (tokens.Count == 1 && (IsGenericToken(tokens[0]) || tokens[0].Length < 3))
        {
            return false;
        }

        if (tokens.All(IsGenericToken))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (foreignProjectNumbers.Contains(token))
            {
                return false;
            }

            if (int.TryParse(token, out var id) && foreignProjectIds.Contains(id))
            {
                return false;
            }
        }

        return true;
    }
}
