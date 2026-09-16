using System.Text;
using System.Text.Json;
using SiNet.Application.Ai;

namespace SiNet.Application.ProjectIntelligence;

public sealed class ProjectIntelligenceEnrichmentService(IAiCompletionService completion)
{
    public const int MaxSubjectsInPrompt = 12;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private static readonly string Instructions =
        """
        You enrich ONE known project for later search.
        Return JSON only:
        {"aliases":[],"abbreviations":[],"distinctivePhrases":[],"placeVariants":[],"keywords":[]}
        Each value is a short search term or phrase (1-6 words). Hebrew allowed.
        Do not invent a ProjectId. Do not assign another project. Do not write sentences.
        """;

    private readonly IAiCompletionService _completion = completion ?? throw new ArgumentNullException(nameof(completion));

    public async Task<ProjectIntelligenceAiDerived> EnrichAsync(
        ProjectIntelligenceFacts facts,
        IReadOnlyList<string> confirmedSubjects,
        IReadOnlySet<string> foreignProjectNumbers,
        IReadOnlySet<int> foreignProjectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(confirmedSubjects);
        ArgumentNullException.ThrowIfNull(foreignProjectNumbers);
        ArgumentNullException.ThrowIfNull(foreignProjectIds);

        var prompt = BuildPrompt(facts, confirmedSubjects);
        var result = await _completion.CompleteAsync(
                new AiCompletionRequest(
                    prompt,
                    AiCompletionLevel.DeepAnalysis,
                    Instructions,
                    ExpectJson: true,
                    Timeout: Timeout),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Text))
        {
            return new ProjectIntelligenceAiDerived([], [], [], [], [], result.ResolvedProvider, result.ResolvedModel);
        }

        return Parse(result.Text, facts, foreignProjectNumbers, foreignProjectIds, result.ResolvedProvider, result.ResolvedModel);
    }

    public static string BuildPrompt(ProjectIntelligenceFacts facts, IReadOnlyList<string> confirmedSubjects)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(confirmedSubjects);

        var sb = new StringBuilder()
            .AppendLine("KNOWN PROJECT (do not change ProjectId):")
            .Append("ProjectId=").Append(facts.ProjectId)
            .Append(" Number=").Append(facts.ProjectNumber)
            .Append(" Name=").Append(facts.CanonicalName)
            .Append(" Place=").Append(facts.Place)
            .Append(" Client=").Append(facts.Client)
            .AppendLine()
            .AppendLine("CONFIRMED HISTORICAL SUBJECTS:");

        foreach (var subject in confirmedSubjects.Take(MaxSubjectsInPrompt))
        {
            sb.Append("- ").AppendLine(subject);
        }

        if (confirmedSubjects.Count == 0)
        {
            sb.AppendLine("(none)");
        }

        return sb.ToString();
    }

    public static ProjectIntelligenceAiDerived Parse(
        string? raw,
        ProjectIntelligenceFacts facts,
        IReadOnlySet<string> foreignProjectNumbers,
        IReadOnlySet<int> foreignProjectIds,
        string? provider,
        string? model)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(foreignProjectNumbers);
        ArgumentNullException.ThrowIfNull(foreignProjectIds);

        if (!TryReadObject(raw, out var root))
        {
            return new ProjectIntelligenceAiDerived([], [], [], [], [], provider, model);
        }

        var aliases = ReadList(root, "aliases");
        var abbreviations = ReadList(root, "abbreviations");
        var phrases = ReadList(root, "distinctivePhrases");
        var places = ReadList(root, "placeVariants");
        var keywords = ReadList(root, "keywords");
        var ownNumber = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { facts.ProjectNumber };
        var others = new HashSet<string>(foreignProjectNumbers, StringComparer.OrdinalIgnoreCase);
        others.ExceptWith(ownNumber);
        var otherIds = new HashSet<int>(foreignProjectIds);
        otherIds.Remove(facts.ProjectId);

        return new ProjectIntelligenceAiDerived(
            ProjectIntelligenceTermSanitizer.Sanitize(aliases, others, otherIds),
            ProjectIntelligenceTermSanitizer.Sanitize(abbreviations, others, otherIds),
            ProjectIntelligenceTermSanitizer.Sanitize(phrases, others, otherIds),
            ProjectIntelligenceTermSanitizer.Sanitize(places, others, otherIds),
            ProjectIntelligenceTermSanitizer.Sanitize(keywords, others, otherIds),
            provider,
            model);
    }

    private static bool TryReadObject(string? raw, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var json = raw.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            json = json[start..(end + 1)];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ReadList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                yield return text;
            }
        }
    }
}
