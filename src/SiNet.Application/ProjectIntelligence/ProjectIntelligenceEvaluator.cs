using System.Diagnostics;
using SiNet.Application.Projects;

namespace SiNet.Application.ProjectIntelligence;

public static class ProjectIntelligenceEvaluator
{
    public const int TargetEvalCount = 50;
    public const int MinimumEvalCount = 30;

    public static IReadOnlyList<ProjectIntelligenceEvalSample> BuildEvalSet(
        IReadOnlyList<ConfirmedProjectSubject> confirmed,
        IReadOnlyDictionary<int, ProjectSummaryDto> projects,
        int targetCount = TargetEvalCount)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(projects);

        var byClean = confirmed
            .Select(row => (row, Clean: ProjectIntelligenceText.Clean(row.Subject)))
            .Where(x => x.Clean.Length >= 4 && projects.ContainsKey(x.row.ProjectId))
            .GroupBy(x => x.Clean, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var buckets = new Dictionary<string, List<ProjectIntelligenceEvalSample>>(StringComparer.Ordinal);
        foreach (var group in byClean)
        {
            var projectIds = group.Select(x => x.row.ProjectId).Distinct().ToArray();
            var first = group.OrderByDescending(x => x.row.ReceivedUtc).First();
            if (!projects.TryGetValue(first.row.ProjectId, out var project))
            {
                continue;
            }

            var bucket = Classify(first.Clean, project, projectIds.Length > 1);
            var sample = new ProjectIntelligenceEvalSample(first.row.Subject, first.row.ProjectId, bucket, HeldOutFromObserved: true);
            if (!buckets.TryGetValue(bucket, out var list))
            {
                list = [];
                buckets[bucket] = list;
            }

            list.Add(sample);
        }

        var selected = new List<ProjectIntelligenceEvalSample>();
        var perBucket = Math.Max(4, targetCount / Math.Max(1, buckets.Count));
        foreach (var bucket in buckets.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
        {
            selected.AddRange(bucket.Value.Take(perBucket));
        }

        if (selected.Count < targetCount)
        {
            var seen = selected.Select(static s => s.Subject + "\u001f" + s.KnownProjectId).ToHashSet(StringComparer.Ordinal);
            foreach (var sample in buckets.Values.SelectMany(static x => x))
            {
                if (selected.Count >= targetCount)
                {
                    break;
                }

                if (seen.Add(sample.Subject + "\u001f" + sample.KnownProjectId))
                {
                    selected.Add(sample);
                }
            }
        }

        return selected.Take(Math.Max(targetCount, MinimumEvalCount)).ToArray();
    }

    public static ProjectIntelligenceStrategyMetrics Measure(
        string strategy,
        IReadOnlyList<ProjectIntelligenceEvalSample> samples,
        Func<ProjectIntelligenceEvalSample, IReadOnlyList<int>> rank)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(rank);

        var top1 = 0;
        var top3 = 0;
        var top5 = 0;
        var none = 0;
        var latencies = new List<double>(samples.Count);
        foreach (var sample in samples)
        {
            var sw = Stopwatch.StartNew();
            var ids = rank(sample);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
            if (ids.Count == 0)
            {
                none++;
                continue;
            }

            var index = ids.Take(5).ToList().IndexOf(sample.KnownProjectId);
            if (index == 0)
            {
                top1++;
                top3++;
                top5++;
            }
            else if (index is 1 or 2)
            {
                top3++;
                top5++;
            }
            else if (index is 3 or 4)
            {
                top5++;
            }
        }

        var count = Math.Max(1, samples.Count);
        return new ProjectIntelligenceStrategyMetrics(
            strategy,
            samples.Count,
            (double)top1 / count,
            (double)top3 / count,
            (double)top5 / count,
            (double)none / count,
            latencies.Count == 0 ? 0 : latencies.Average(),
            Percentile(latencies, 0.95));
    }

    public static string Recommend(IReadOnlyList<ProjectIntelligenceStrategyMetrics> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var facts = Find(strategies, "Facts");
        var historical = Find(strategies, "Facts+Historical");
        var ai = Find(strategies, "Facts+Historical+AI");
        var rerank = Find(strategies, "Rerank");
        var existing = Find(strategies, "Existing");

        if (rerank is not null && ai is not null && rerank.Top1 >= ai.Top1 + 0.05)
        {
            return "4. Local intelligence + nightly AI + live Top-10 rerank";
        }

        if (ai is not null && historical is not null && ai.Top1 >= historical.Top1 + 0.04)
        {
            return "3. Local intelligence + nightly AI enrichment";
        }

        if (historical is not null && facts is not null && historical.Top1 >= facts.Top1 + 0.03)
        {
            return "2. Local intelligence + historical Subjects";
        }

        if (facts is not null && existing is not null && facts.Top1 >= existing.Top1)
        {
            return "1. Local intelligence only";
        }

        return historical is not null
            ? "2. Local intelligence + historical Subjects"
            : "1. Local intelligence only";
    }

    public static string FormatReport(ProjectIntelligenceEvalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "PROJECT INTELLIGENCE PHASE 1",
            $"Confirmed subjects: {report.ConfirmedSubjectCount}",
            $"Eval samples: {report.EvalSampleCount}",
            $"Hierarchy: {report.HierarchyNote}",
            $"AI: {report.AiLevel} / {report.AiProvider} / {report.AiModel} avgEnrichMs={report.AverageEnrichmentMs:0}",
            "",
        };

        foreach (var row in report.Strategies)
        {
            lines.Add(
                $"{row.Strategy}: Top1={row.Top1:P0} Top3={row.Top3:P0} Top5={row.Top5:P0} none={row.NoCandidateRate:P0} avg={row.AverageLatencyMs:0.0}ms p95={row.P95LatencyMs:0.0}ms");
        }

        if (report.Rerank is { } rerank)
        {
            lines.Add(
                $"{rerank.Strategy}: Top1={rerank.Top1:P0} Top3={rerank.Top3:P0} Top5={rerank.Top5:P0} avg={rerank.AverageLatencyMs:0}ms");
        }

        lines.Add("Recommendation: " + report.Recommendation);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Classify(string cleaned, ProjectSummaryDto project, bool ambiguous)
    {
        if (ambiguous)
        {
            return "ambiguous";
        }

        var tokens = Email.EmailFilingSubjectQuery.MeaningfulTokens(cleaned);
        if (tokens.Count <= 1)
        {
            return "generic";
        }

        if (tokens.Any(t => string.Equals(t, project.ProjectNumber, StringComparison.OrdinalIgnoreCase)))
        {
            return "project-number";
        }

        if (!string.IsNullOrWhiteSpace(project.ProjectName)
            && cleaned.Contains(project.ProjectName, StringComparison.OrdinalIgnoreCase))
        {
            return "official-name";
        }

        if (tokens.Any(static t => t.Any(char.IsDigit))
            && !string.IsNullOrWhiteSpace(project.PlaceName)
            && cleaned.Contains(project.PlaceName, StringComparison.OrdinalIgnoreCase))
        {
            return "street-address";
        }

        if (!string.IsNullOrWhiteSpace(project.PlaceName)
            && cleaned.Contains(project.PlaceName, StringComparison.OrdinalIgnoreCase))
        {
            return "locality";
        }

        if (!string.IsNullOrWhiteSpace(project.CompanyName)
            && cleaned.Contains(project.CompanyName, StringComparison.OrdinalIgnoreCase))
        {
            return "client";
        }

        return "alias-informal";
    }

    private static ProjectIntelligenceStrategyMetrics? Find(
        IReadOnlyList<ProjectIntelligenceStrategyMetrics> strategies,
        string prefix)
        => strategies.FirstOrDefault(s => string.Equals(s.Strategy, prefix, StringComparison.OrdinalIgnoreCase));

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(static x => x).ToArray();
        var index = (int)Math.Ceiling(p * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }
}
