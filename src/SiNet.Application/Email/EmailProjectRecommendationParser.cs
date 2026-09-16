using System.Globalization;
using System.Text.Json;

namespace SiNet.Application.Email;

/// <summary>Validates structured AI JSON against the exact candidate set that was sent.</summary>
public static class EmailProjectRecommendationParser
{
    public const int MaxRecommendations = 5;
    public const double MinimumDisplayConfidence = 0.35;
    public const double StrongConfidence = 0.55;

    public static IReadOnlyList<ParsedEmailProjectRecommendation> Parse(
        string? raw,
        IReadOnlySet<int> sentProjectIds)
    {
        ArgumentNullException.ThrowIfNull(sentProjectIds);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var json = UnwrapJson(raw);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("recommendations", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var seen = new HashSet<int>();
            var parsed = new List<ParsedEmailProjectRecommendation>();
            foreach (var item in array.EnumerateArray())
            {
                if (!TryReadProjectId(item, out var projectId))
                {
                    continue;
                }

                if (!sentProjectIds.Contains(projectId) || !seen.Add(projectId))
                {
                    continue;
                }

                if (!TryReadConfidence(item, out var confidence) || confidence < MinimumDisplayConfidence)
                {
                    continue;
                }

                var reason = item.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                    ? reasonEl.GetString()
                    : null;
                parsed.Add(new ParsedEmailProjectRecommendation(projectId, confidence, reason));
            }

            parsed.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));
            if (parsed.Count > 3 && parsed.Count(static x => x.Confidence >= StrongConfidence) >= 3)
            {
                parsed.RemoveRange(3, parsed.Count - 3);
            }
            else if (parsed.Count > MaxRecommendations)
            {
                parsed.RemoveRange(MaxRecommendations, parsed.Count - MaxRecommendations);
            }

            return parsed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string UnwrapJson(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var start = text.StartsWith("```json", StringComparison.OrdinalIgnoreCase) ? 7 : 3;
            text = text[start..].TrimStart('\r', '\n', ' ');
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }
        }

        return text.Trim();
    }

    private static bool TryReadProjectId(JsonElement item, out int projectId)
    {
        projectId = 0;
        if (!item.TryGetProperty("projectId", out var idEl))
        {
            return false;
        }

        if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out projectId))
        {
            return projectId > 0;
        }

        return idEl.ValueKind == JsonValueKind.String
            && int.TryParse(idEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out projectId)
            && projectId > 0;
    }

    private static bool TryReadConfidence(JsonElement item, out double confidence)
    {
        confidence = 0;
        if (!item.TryGetProperty("confidence", out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out confidence))
        {
            return confidence is >= 0 and <= 1;
        }

        return el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out confidence)
            && confidence is >= 0 and <= 1;
    }
}

public sealed record ParsedEmailProjectRecommendation(int ProjectId, double Confidence, string? Reason);
