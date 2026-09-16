using SiNet.Application.Email;

namespace SiNet.Application.ProjectIntelligence;

public sealed class ProjectIntelligenceRetriever
{
    public const int DefaultTopN = 10;
    public const int MinimumScore = 12;

    public IReadOnlyList<ProjectIntelligenceHit> Retrieve(
        string? subject,
        IReadOnlyList<ProjectIntelligenceProfile> catalog,
        ProjectIntelligenceLayers layers = ProjectIntelligenceLayers.All,
        int topN = DefaultTopN)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var cleaned = ProjectIntelligenceText.Clean(subject);
        if (cleaned.Length == 0 || catalog.Count == 0)
        {
            return [];
        }

        var tokens = EmailFilingSubjectQuery.MeaningfulTokens(cleaned);
        var hits = new List<ProjectIntelligenceHit>(Math.Min(catalog.Count, 64));
        foreach (var profile in catalog)
        {
            var hit = Score(cleaned, tokens, profile, layers);
            if (hit.Score >= MinimumScore)
            {
                hits.Add(hit);
            }
        }

        return hits
            .OrderByDescending(static h => h.Score)
            .ThenBy(static h => h.ProjectId)
            .Take(topN > 0 ? topN : DefaultTopN)
            .ToArray();
    }

    public static ProjectIntelligenceHit Score(
        string cleanedSubject,
        IReadOnlyList<string> subjectTokens,
        ProjectIntelligenceProfile profile,
        ProjectIntelligenceLayers layers)
    {
        ArgumentNullException.ThrowIfNull(cleanedSubject);
        ArgumentNullException.ThrowIfNull(subjectTokens);
        ArgumentNullException.ThrowIfNull(profile);

        var signals = new List<string>();
        var score = 0;
        var facts = profile.Facts;

        if (layers.HasFlag(ProjectIntelligenceLayers.Facts))
        {
            if (HasToken(subjectTokens, facts.ProjectNumber))
            {
                Add(ref score, signals, 48, $"מספר פרויקט מדויק {facts.ProjectNumber}");
            }

            if (IsDistinctive(facts.CanonicalName) && ProjectIntelligenceText.ContainsPhrase(cleanedSubject, facts.CanonicalName))
            {
                Add(ref score, signals, 42, $"שם רשמי \"{facts.CanonicalName}\"");
            }
            else if (IsDistinctive(facts.ProjectLabelName)
                && ProjectIntelligenceText.ContainsPhrase(cleanedSubject, facts.ProjectLabelName!))
            {
                Add(ref score, signals, 36, $"תווית \"{facts.ProjectLabelName}\"");
            }
            else if (TryNamePhrase(cleanedSubject, facts, out var namePhrase))
            {
                Add(ref score, signals, 32, $"ביטוי שם \"{namePhrase}\"");
            }

            if (IsDistinctive(facts.Place) && ProjectIntelligenceText.ContainsPhrase(cleanedSubject, facts.Place!))
            {
                var streetHint = HasMatchingStreetHint(subjectTokens, facts);
                Add(ref score, signals, streetHint ? 24 : 10,
                    streetHint
                        ? $"יישוב+רמז כתובת \"{facts.Place}\""
                        : $"יישוב \"{facts.Place}\"");
            }

            if (IsDistinctive(facts.Client) && ProjectIntelligenceText.ContainsPhrase(cleanedSubject, facts.Client!))
            {
                var clientScore = IsDistinctive(facts.Place)
                    && ProjectIntelligenceText.ContainsPhrase(cleanedSubject, facts.Place!)
                    ? 14
                    : 8;
                Add(ref score, signals, clientScore, $"מזמין \"{facts.Client}\"");
            }

            score += TokenOverlap(subjectTokens, FactTokens(facts), signals, "עובדות");
        }

        if (layers.HasFlag(ProjectIntelligenceLayers.Observed))
        {
            foreach (var phrase in profile.Observed.DistinctivePhrases)
            {
                if (!ProjectIntelligenceText.ContainsPhrase(cleanedSubject, phrase)
                    && !ProjectIntelligenceText.ContainsPhrase(phrase, cleanedSubject))
                {
                    continue;
                }

                var weight = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3 ? 26 : 20;
                Add(ref score, signals, weight, $"ביטוי היסטורי \"{phrase}\"");
                break;
            }
        }

        if (layers.HasFlag(ProjectIntelligenceLayers.AiDerived))
        {
            foreach (var phrase in DistinctiveAiPhrases(profile.AiDerived))
            {
                if (!ProjectIntelligenceText.ContainsPhrase(cleanedSubject, phrase))
                {
                    continue;
                }

                var weight = phrase.Contains(' ') ? 22 : 12;
                Add(ref score, signals, weight, $"כינוי AI \"{phrase}\"");
                break;
            }
        }

        score += Fuzzy(subjectTokens, profile, layers, signals);
        return new ProjectIntelligenceHit(profile.ProjectId, Math.Min(100, score), signals);
    }

    /// <summary>
    /// Display-grade match: a named strong fact signal, not merely the highest numeric score.
    /// Calibrated from the existing weight table (number / name / label / name-phrase / Place+matching hint).
    /// </summary>
    public static bool HasMeaningfulMatch(ProjectIntelligenceHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return hit.Signals.Any(IsStrongSignal);
    }

    internal static bool IsStrongSignal(string signal)
        => signal.StartsWith("מספר פרויקט מדויק", StringComparison.Ordinal)
           || signal.StartsWith("שם רשמי", StringComparison.Ordinal)
           || signal.StartsWith("תווית", StringComparison.Ordinal)
           || signal.StartsWith("ביטוי שם", StringComparison.Ordinal)
           || signal.StartsWith("יישוב+רמז כתובת", StringComparison.Ordinal);

    private static IEnumerable<string> DistinctiveAiPhrases(ProjectIntelligenceAiDerived ai)
        => ai.Aliases.Concat(ai.Abbreviations).Concat(ai.DistinctivePhrases).Concat(ai.PlaceVariants).Concat(ai.Keywords);

    private static IReadOnlyList<string> FactTokens(ProjectIntelligenceFacts facts)
        => EmailFilingSubjectQuery.MeaningfulTokens(
            string.Join(' ', new[] { facts.ProjectNumber, facts.CanonicalName, facts.Place, facts.Client }
                .Where(static s => !string.IsNullOrWhiteSpace(s))));

    private static int TokenOverlap(
        IReadOnlyList<string> subjectTokens,
        IReadOnlyList<string> profileTokens,
        List<string> signals,
        string layer)
    {
        var overlap = subjectTokens
            .Where(t => !ProjectIntelligenceTermSanitizer.IsGenericToken(t))
            .Intersect(profileTokens, StringComparer.OrdinalIgnoreCase)
            .Where(t => t.Length >= 3)
            .Take(4)
            .ToArray();
        if (overlap.Length == 0)
        {
            return 0;
        }

        signals.Add($"חפיפת אסימונים ({layer}): {string.Join(", ", overlap)}");
        return Math.Min(16, overlap.Length * 4);
    }

    private static int Fuzzy(
        IReadOnlyList<string> subjectTokens,
        ProjectIntelligenceProfile profile,
        ProjectIntelligenceLayers layers,
        List<string> signals)
    {
        var candidates = new List<string>();
        if (layers.HasFlag(ProjectIntelligenceLayers.Facts))
        {
            candidates.AddRange(FactTokens(profile.Facts));
        }

        if (layers.HasFlag(ProjectIntelligenceLayers.Observed))
        {
            candidates.AddRange(profile.Observed.DistinctivePhrases.SelectMany(EmailFilingSubjectQuery.MeaningfulTokens));
        }

        foreach (var token in subjectTokens.Where(static t => t.Length >= 5 && !ProjectIntelligenceTermSanitizer.IsGenericToken(t)))
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Length < 5 || string.Equals(token, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ProjectIntelligenceText.Levenshtein(token, candidate) == 1)
                {
                    signals.Add($"דמיון איות {token}≈{candidate}");
                    return 3;
                }
            }
        }

        return 0;
    }

    private static bool HasToken(IReadOnlyList<string> tokens, string? value)
        => !string.IsNullOrWhiteSpace(value)
            && tokens.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase));

    private static bool TryNamePhrase(string cleanedSubject, ProjectIntelligenceFacts facts, out string phrase)
    {
        foreach (var candidate in DistinctiveNamePhrases(facts))
        {
            if (ProjectIntelligenceText.ContainsPhrase(cleanedSubject, candidate))
            {
                phrase = candidate;
                return true;
            }
        }

        phrase = string.Empty;
        return false;
    }

    private static IEnumerable<string> DistinctiveNamePhrases(ProjectIntelligenceFacts facts)
    {
        foreach (var source in new[] { facts.CanonicalName, facts.ProjectLabelName })
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var tokens = EmailFilingSubjectQuery.MeaningfulTokens(
                ProjectIntelligenceText.NormalizePunctuation(source));
            for (var n = 2; n <= 3; n++)
            {
                for (var i = 0; i + n <= tokens.Count; i++)
                {
                    var slice = new string[n];
                    var hasWord = false;
                    var hasGeneric = false;
                    for (var j = 0; j < n; j++)
                    {
                        slice[j] = tokens[i + j];
                        if (ProjectIntelligenceTermSanitizer.IsGenericToken(slice[j]))
                        {
                            hasGeneric = true;
                            break;
                        }

                        if (slice[j].Length >= 3 && !slice[j].All(char.IsDigit))
                        {
                            hasWord = true;
                        }
                    }

                    if (hasGeneric || !hasWord)
                    {
                        continue;
                    }

                    var joined = string.Join(' ', slice);
                    if (IsDistinctive(joined))
                    {
                        yield return joined;
                    }
                }
            }
        }
    }

    private static bool HasMatchingStreetHint(IReadOnlyList<string> subjectTokens, ProjectIntelligenceFacts facts)
    {
        var hints = subjectTokens
            .Where(static t => t.Any(char.IsDigit)
                || t.Contains("מגרש", StringComparison.OrdinalIgnoreCase)
                || t.Contains("רחוב", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (hints.Length == 0)
        {
            return false;
        }

        var factText = ProjectIntelligenceText.NormalizePunctuation(
            string.Join(
                ' ',
                new[] { facts.ProjectNumber, facts.CanonicalName, facts.ProjectLabelName }
                    .Where(static s => !string.IsNullOrWhiteSpace(s))));
        if (factText.Length == 0)
        {
            return false;
        }

        foreach (var hint in hints)
        {
            if (string.Equals(hint, facts.ProjectNumber, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (factText.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDistinctive(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length >= 3
            && !ProjectIntelligenceTermSanitizer.IsGenericToken(value);

    private static void Add(ref int score, List<string> signals, int weight, string signal)
    {
        score += weight;
        signals.Add(signal);
    }
}
