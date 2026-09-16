using System.Globalization;
using System.Text;
using System.Text.Json;
using SiNet.Application.Ai;

namespace SiNet.Application.ProjectIntelligence;

public sealed class ProjectIntelligenceReranker(IAiCompletionService completion)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private static readonly string Instructions =
        """
        Rerank supplied project candidates for one email Subject.
        Return JSON only: {"rankedProjectIds":[123,456]}
        Use only ids from the candidate list. Never invent a new id.
        Most likely first. If unsure keep the original order.
        """;

    private readonly IAiCompletionService _completion = completion ?? throw new ArgumentNullException(nameof(completion));

    public async Task<IReadOnlyList<int>> RerankAsync(
        string? subject,
        IReadOnlyList<ProjectIntelligenceHit> localHits,
        IReadOnlyDictionary<int, ProjectIntelligenceProfile> profiles,
        AiCompletionLevel level,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localHits);
        ArgumentNullException.ThrowIfNull(profiles);
        if (localHits.Count == 0)
        {
            return [];
        }

        var allowed = localHits.Select(static h => h.ProjectId).ToHashSet();
        var prompt = BuildPrompt(subject, localHits, profiles);
        var result = await _completion.CompleteAsync(
                new AiCompletionRequest(prompt, level, Instructions, ExpectJson: true, Timeout: Timeout),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return localHits.Select(static h => h.ProjectId).ToArray();
        }

        return Merge(ParseIds(result.Text, allowed), localHits);
    }

    public static string BuildPrompt(
        string? subject,
        IReadOnlyList<ProjectIntelligenceHit> localHits,
        IReadOnlyDictionary<int, ProjectIntelligenceProfile> profiles)
    {
        var sb = new StringBuilder()
            .AppendLine("SUBJECT:")
            .AppendLine(ProjectIntelligenceText.Clean(subject))
            .AppendLine()
            .AppendLine("CANDIDATES:");
        foreach (var hit in localHits)
        {
            profiles.TryGetValue(hit.ProjectId, out var profile);
            var facts = profile?.Facts;
            sb.Append(hit.ProjectId).Append(" | ")
                .Append(facts?.ProjectNumber).Append(" | ")
                .Append(facts?.CanonicalName).Append(" | ")
                .Append(facts?.Place).Append(" | ")
                .Append(facts?.Client)
                .AppendLine();
        }

        return sb.ToString();
    }

    public static IReadOnlyList<int> ParseIds(string? raw, IReadOnlySet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var json = raw.Trim();
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            json = json[start..(end + 1)];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rankedProjectIds", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var ids = new List<int>();
            var seen = new HashSet<int>();
            foreach (var item in array.EnumerateArray())
            {
                if (!TryReadId(item, out var id) || !allowed.Contains(id) || !seen.Add(id))
                {
                    continue;
                }

                ids.Add(id);
            }

            return ids;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<int> Merge(IReadOnlyList<int> ranked, IReadOnlyList<ProjectIntelligenceHit> localHits)
    {
        var seen = new HashSet<int>(ranked);
        var merged = ranked.ToList();
        foreach (var hit in localHits)
        {
            if (seen.Add(hit.ProjectId))
            {
                merged.Add(hit.ProjectId);
            }
        }

        return merged;
    }

    private static bool TryReadId(JsonElement item, out int id)
    {
        if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out id))
        {
            return true;
        }

        if (item.ValueKind == JsonValueKind.String
            && int.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        id = 0;
        return false;
    }
}
