using SiNet.Application.Email;

namespace SiNet.Application.ProjectIntelligence;

public static class ProjectIntelligenceObservedExtractor
{
    public const int MaxSubjectsPerProject = 40;
    public const int MaxPhrasesPerProject = 24;

    public static ProjectIntelligenceObserved Extract(
        IEnumerable<string?> rawSubjects,
        ProjectIntelligenceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rawSubjects);
        ArgumentNullException.ThrowIfNull(facts);

        var cleaned = new List<string>();
        var seenSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawSubjects)
        {
            var subject = ProjectIntelligenceText.Clean(raw);
            if (subject.Length < 4 || !seenSubjects.Add(subject))
            {
                continue;
            }

            cleaned.Add(subject);
            if (cleaned.Count >= MaxSubjectsPerProject)
            {
                break;
            }
        }

        var phrases = new List<string>();
        var seenPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subject in cleaned)
        {
            foreach (var phrase in DistinctivePhrasesFrom(subject, facts))
            {
                if (seenPhrases.Add(phrase))
                {
                    phrases.Add(phrase);
                }

                if (phrases.Count >= MaxPhrasesPerProject)
                {
                    return new ProjectIntelligenceObserved(cleaned, phrases);
                }
            }
        }

        return new ProjectIntelligenceObserved(cleaned, phrases);
    }

    private static IEnumerable<string> DistinctivePhrasesFrom(string subject, ProjectIntelligenceFacts facts)
    {
        var tokens = EmailFilingSubjectQuery.MeaningfulTokens(subject);
        if (tokens.Count >= 2 && tokens.Count <= 6
            && ProjectIntelligenceTermSanitizer.IsUseful(subject, EmptyNumbers, EmptyIds))
        {
            yield return subject;
        }

        for (var size = 4; size >= 2; size--)
        {
            for (var i = 0; i + size <= tokens.Count; i++)
            {
                var slice = tokens.Skip(i).Take(size).ToArray();
                if (slice.All(ProjectIntelligenceTermSanitizer.IsGenericToken))
                {
                    continue;
                }

                var phrase = string.Join(' ', slice);
                var hasNumber = slice.Any(static t => t.Any(char.IsDigit));
                var hasPlace = !string.IsNullOrWhiteSpace(facts.Place)
                    && slice.Any(t => facts.Place!.Contains(t, StringComparison.OrdinalIgnoreCase)
                        || t.Contains(facts.Place, StringComparison.OrdinalIgnoreCase));
                if (size >= 3 || hasNumber || hasPlace)
                {
                    yield return phrase;
                }
            }
        }
    }

    private static readonly HashSet<string> EmptyNumbers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<int> EmptyIds = [];
}
