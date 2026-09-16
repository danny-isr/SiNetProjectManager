using System.Security.Cryptography;
using System.Text;
using SiNet.Application.Email;

namespace SiNet.Application.ProjectIntelligence;

internal static class ProjectIntelligenceText
{
    public const string GeneratorVersion = "project-intelligence-phase1-2026-09-16";

    public static string Clean(string? subject)
        => NormalizePunctuation(EmailFilingSubjectQuery.CleanSubject(subject));

    public static string NormalizePunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var chars = text.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is ',' or ';' or '/' or '\\' or '|' or '_' or ':' or '—' or '–' or '"' or '\''
                or '(' or ')' or '[' or ']' or '{' or '}' or '?' or '!' or '*')
            {
                chars[i] = ' ';
            }
        }

        var collapsed = string.Join(
            ' ',
            new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return collapsed;
    }

    public static bool ContainsPhrase(string haystack, string phrase)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        return haystack.Contains(phrase, StringComparison.OrdinalIgnoreCase);
    }

    public static string SourceHash(ProjectIntelligenceFacts facts, IEnumerable<string> observedSubjects)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(observedSubjects);

        var sb = new StringBuilder()
            .Append(facts.ProjectId).Append('|')
            .Append(facts.ProjectNumber).Append('|')
            .Append(facts.CanonicalName).Append('|')
            .Append(facts.ProjectLabelName).Append('|')
            .Append(facts.Place).Append('|')
            .Append(facts.Client).Append('|')
            .Append(facts.IsActive).Append('|')
            .Append(GeneratorVersion);
        foreach (var subject in observedSubjects.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('\n').Append(subject);
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
