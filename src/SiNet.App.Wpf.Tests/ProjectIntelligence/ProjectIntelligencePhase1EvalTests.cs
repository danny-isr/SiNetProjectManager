using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Ai;
using SiNet.Application.ProjectIntelligence;
using SiNet.Application.Projects;
using SiNet.Application.Settings;
using SiNet.App.Wpf.Tests.Live;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectIntelligence;

/// <summary>
/// Phase 1 read-only benchmark. Skips when SQL is unavailable so CI stays green.
/// Does not file, migrate, schedule, or publish.
/// </summary>
public sealed class ProjectIntelligencePhase1EvalTests
{
    private const int EnrichProjectCap = 4;
    private const int RerankSampleCap = 5;

    [Fact]
    public async Task WhenSqlAvailableThenBenchmarkConfirmedSubjects()
    {
        var connection = LiveEnvironment.TryResolveSqlConnectionString();
        if (string.IsNullOrWhiteSpace(connection))
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddSiNetSecrets();
        services.AddSiNetSql(connection);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetProjectQuerySql();
        services.AddSiNetEmailReadSql();
        services.AddSiNetSystemSettingsSql();
        services.AddSiNetAi();
        await using var sp = services.BuildServiceProvider();

        var settings = await sp.GetRequiredService<ISystemSettingsQueryService>()
            .GetSystemSettingsAsync()
            .ConfigureAwait(true);
        if (!int.TryParse(settings.EmailOffice.OfficeManagementProjectId, out var officeId) || officeId <= 0)
        {
            officeId = int.Parse(SystemSettingsDefaults.OfficeManagementProjectId);
        }

        var projects = await sp.GetRequiredService<IProjectQueryService>()
            .SearchProjectsAsync(new ProjectSearchQuery(IncludeClosed: true))
            .ConfigureAwait(true);
        var confirmed = await sp.GetRequiredService<IConfirmedProjectSubjectSource>()
            .LoadAsync(officeId)
            .ConfigureAwait(true);

        Assert.True(projects.Count > 0, "Expected project facts from SQL.");

        var dbFactory = sp.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(true);
        var inboxTotal = await db.EmailInboxMessages.CountAsync().ConfigureAwait(true);
        var inboxNonOffice = await db.EmailInboxMessages.CountAsync(i =>
                i.ProjectId != officeId && i.Subject != null && i.Subject != "")
            .ConfigureAwait(true);
        var mappingAssigned = await db.ThreadStatusMappings.CountAsync(t =>
                t.Status == ThreadMappingStatus.Assigned && t.ProjectId != officeId)
            .ConfigureAwait(true);
        var preferredIds = new[] { 3070, 3045, 3147, 3213 };
        var factExamples = preferredIds
            .Select(id => projects.FirstOrDefault(p => p.ProjectId == id))
            .Where(p => p is not null)
            .Cast<ProjectSummaryDto>()
            .Concat(projects.Where(p =>
                p.IsActive
                && !string.IsNullOrWhiteSpace(p.PlaceName)
                && !string.IsNullOrWhiteSpace(p.CompanyName)
                && p.ProjectName.Contains("SMOKE", StringComparison.OrdinalIgnoreCase) == false
                && p.ProjectName.Contains("CERT", StringComparison.OrdinalIgnoreCase) == false
                && p.PlaceName!.Equals("SI", StringComparison.OrdinalIgnoreCase) == false))
            .DistinctBy(p => p.ProjectId)
            .Take(3)
            .Select(p => ProjectIntelligenceCatalogBuilder.Build(p, []))
            .ToArray();

        if (confirmed.Count < ProjectIntelligenceEvaluator.MinimumEvalCount)
        {
            var sb = new StringBuilder()
                .AppendLine("PROJECT INTELLIGENCE PHASE 1")
                .AppendLine($"projects={projects.Count} officeId={officeId}")
                .AppendLine($"inboxTotal={inboxTotal} inboxNonOfficeWithSubject={inboxNonOffice} mappingAssignedNonOffice={mappingAssigned}")
                .AppendLine($"confirmedJoin={confirmed.Count}")
                .AppendLine("Historical subjects STOPPED: clean File-cache join is below 30. No Gmail crawl added.")
                .AppendLine()
                .AppendLine("CONFIRMED ROWS");
            foreach (var row in confirmed)
            {
                sb.Append(row.ProjectId).Append(" | ").AppendLine(row.Subject);
            }

            sb.AppendLine().AppendLine("EXAMPLE FACT PROFILES");
            foreach (var profile in factExamples)
            {
                sb.Append("ProjectId=").Append(profile.ProjectId)
                    .Append(" Number=").Append(profile.ProjectNumber)
                    .Append(" Name=").Append(profile.Facts.CanonicalName)
                    .Append(" Place=").Append(profile.Facts.Place)
                    .Append(" Client=").Append(profile.Facts.Client)
                    .AppendLine();
            }

            sb.AppendLine().AppendLine(ProjectIntelligenceCatalogBuilder.EvaluateHierarchy(
                ProjectIntelligenceCatalogBuilder.BuildCatalog(projects, new Dictionary<int, IReadOnlyList<string>>())));

            var catalog = ProjectIntelligenceCatalogBuilder.BuildCatalog(projects, new Dictionary<int, IReadOnlyList<string>>());
            var tinyRetriever = new ProjectIntelligenceRetriever();
            var tiny = confirmed
                .Select(row => new ProjectIntelligenceEvalSample(row.Subject, row.ProjectId, "confirmed-tiny", true))
                .ToArray();
            if (tiny.Length > 0)
            {
                var existingTiny = ProjectIntelligenceEvaluator.Measure(
                    "Existing",
                    tiny,
                    sample => ExistingProjectSearchBaseline.Rank(sample.Subject, projects));
                var factsTiny = ProjectIntelligenceEvaluator.Measure(
                    "Facts",
                    tiny,
                    sample => tinyRetriever.Retrieve(sample.Subject, catalog, ProjectIntelligenceLayers.Facts, 5)
                        .Select(h => h.ProjectId)
                        .ToArray());
                sb.AppendLine()
                    .AppendLine("TINY CONFIRMED A/B (n=2, generic quote subjects — not a 30+ eval)")
                    .AppendLine(ProjectIntelligenceEvaluator.FormatReport(new ProjectIntelligenceEvalReport(
                        confirmed.Count,
                        tiny.Length,
                        factExamples,
                        [existingTiny, factsTiny],
                        null,
                        nameof(AiCompletionLevel.DeepAnalysis),
                        settings.Ai.DeepAnalysis.Provider,
                        settings.Ai.DeepAnalysis.Model,
                        null,
                        null,
                        "1. Local intelligence only")));
            }

            WriteReport(sb.ToString());
            return;
        }

        var byId = projects.ToDictionary(static p => p.ProjectId);
        var samples = ProjectIntelligenceEvaluator.BuildEvalSet(confirmed, byId, 50);
        Assert.True(samples.Count >= ProjectIntelligenceEvaluator.MinimumEvalCount, $"Eval set too small: {samples.Count}");

        var subjectsByProject = confirmed
            .GroupBy(static x => x.ProjectId)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<string>)g
                    .Select(x => x.Subject)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(ProjectIntelligenceObservedExtractor.MaxSubjectsPerProject)
                    .ToArray());

        var catalogFactsObserved = ProjectIntelligenceCatalogBuilder.BuildCatalog(projects, subjectsByProject);
        var retriever = new ProjectIntelligenceRetriever();

        var existing = ProjectIntelligenceEvaluator.Measure(
            "Existing",
            samples,
            sample => ExistingProjectSearchBaseline.Rank(sample.Subject, projects));
        var facts = ProjectIntelligenceEvaluator.Measure(
            "Facts",
            samples,
            sample => RankHeldOut(retriever, catalogFactsObserved, sample, ProjectIntelligenceLayers.Facts));
        var historical = ProjectIntelligenceEvaluator.Measure(
            "Facts+Historical",
            samples,
            sample => RankHeldOut(retriever, catalogFactsObserved, sample, ProjectIntelligenceLayers.FactsAndObserved));

        var strategies = new List<ProjectIntelligenceStrategyMetrics> { existing, facts, historical };
        string? provider = settings.Ai.DeepAnalysis.Provider;
        string? model = settings.Ai.DeepAnalysis.Model;
        double? enrichMs = null;
        ProjectIntelligenceStrategyMetrics? aiMetrics = null;
        ProjectIntelligenceStrategyMetrics? rerankMetrics = null;

        var completion = sp.GetRequiredService<IAiCompletionService>();
        var aiAvailable = await completion.IsAvailableAsync(AiCompletionLevel.DeepAnalysis).ConfigureAwait(true);
        IReadOnlyList<ProjectIntelligenceProfile> catalogAll = catalogFactsObserved;
        if (aiAvailable)
        {
            var enricher = new ProjectIntelligenceEnrichmentService(completion);
            var foreignNumbers = projects.Select(static p => p.ProjectNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var foreignIds = projects.Select(static p => p.ProjectId).ToHashSet();
            var evalProjectIds = samples.Select(static s => s.KnownProjectId).Distinct().Take(EnrichProjectCap).ToArray();
            var aiByProject = new Dictionary<int, ProjectIntelligenceAiDerived>();
            var enrichTimes = new List<double>();
            foreach (var projectId in evalProjectIds)
            {
                var profile = catalogFactsObserved.First(p => p.ProjectId == projectId);
                var heldSubjects = profile.Observed.CleanedSubjects
                    .Where(s => samples.All(sample =>
                        sample.KnownProjectId != projectId
                        || !string.Equals(ProjectIntelligenceCatalogBuilder.CleanSubject(sample.Subject), s, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                var sw = Stopwatch.StartNew();
                var derived = await enricher.EnrichAsync(
                        profile.Facts,
                        heldSubjects,
                        foreignNumbers,
                        foreignIds)
                    .ConfigureAwait(true);
                sw.Stop();
                enrichTimes.Add(sw.Elapsed.TotalMilliseconds);
                provider = derived.Provider ?? provider;
                model = derived.Model ?? model;
                aiByProject[projectId] = derived;
                if (sw.Elapsed.TotalSeconds > 45)
                {
                    break;
                }
            }

            enrichMs = enrichTimes.Count == 0 ? null : enrichTimes.Average();
            catalogAll = ProjectIntelligenceCatalogBuilder.BuildCatalog(projects, subjectsByProject, aiByProject);
            aiMetrics = ProjectIntelligenceEvaluator.Measure(
                "Facts+Historical+AI",
                samples,
                sample => RankHeldOut(retriever, catalogAll, sample, ProjectIntelligenceLayers.All));
            strategies.Add(aiMetrics);

            if (string.Equals(Environment.GetEnvironmentVariable("SINET_PI_RERANK"), "1", StringComparison.Ordinal))
            {
                var reranker = new ProjectIntelligenceReranker(completion);
                var profileMap = catalogAll.ToDictionary(static p => p.ProjectId);
                var rerankSamples = samples.Take(RerankSampleCap).ToArray();
                rerankMetrics = ProjectIntelligenceEvaluator.Measure(
                    "Rerank",
                    rerankSamples,
                    sample =>
                    {
                        var local = RankHeldOutHits(retriever, catalogAll, sample, ProjectIntelligenceLayers.All);
                        return reranker.RerankAsync(
                                sample.Subject,
                                local,
                                profileMap,
                                AiCompletionLevel.DeepAnalysis)
                            .GetAwaiter()
                            .GetResult()
                            .Take(5)
                            .ToArray();
                    });
            }
        }

        var examples = catalogAll
            .OrderByDescending(p => p.Observed.CleanedSubjects.Count)
            .Take(3)
            .ToArray();
        var report = new ProjectIntelligenceEvalReport(
            confirmed.Count,
            samples.Count,
            examples,
            strategies,
            ProjectIntelligenceCatalogBuilder.EvaluateHierarchy(catalogFactsObserved),
            AiCompletionLevel.DeepAnalysis.ToString(),
            provider,
            model,
            enrichMs,
            rerankMetrics,
            ProjectIntelligenceEvaluator.Recommend(strategies.Concat(rerankMetrics is null ? [] : [rerankMetrics]).ToArray()));

        var text = FormatFullReport(report, examples, samples);
        WriteReport(text);
        Assert.True(samples.Count >= 30);
        Assert.True(historical.SampleCount == samples.Count);
    }

    private static IReadOnlyList<int> RankHeldOut(
        ProjectIntelligenceRetriever retriever,
        IReadOnlyList<ProjectIntelligenceProfile> catalog,
        ProjectIntelligenceEvalSample sample,
        ProjectIntelligenceLayers layers)
        => RankHeldOutHits(retriever, catalog, sample, layers).Select(static h => h.ProjectId).ToArray();

    private static IReadOnlyList<ProjectIntelligenceHit> RankHeldOutHits(
        ProjectIntelligenceRetriever retriever,
        IReadOnlyList<ProjectIntelligenceProfile> catalog,
        ProjectIntelligenceEvalSample sample,
        ProjectIntelligenceLayers layers)
    {
        var working = catalog
            .Select(p => p.ProjectId == sample.KnownProjectId
                ? ProjectIntelligenceCatalogBuilder.WithHeldOutSubject(p, sample.Subject)
                : p)
            .ToArray();
        return retriever.Retrieve(sample.Subject, working, layers, 5);
    }

    private static string FormatFullReport(
        ProjectIntelligenceEvalReport report,
        IReadOnlyList<ProjectIntelligenceProfile> examples,
        IReadOnlyList<ProjectIntelligenceEvalSample> samples)
    {
        var sb = new StringBuilder()
            .AppendLine(ProjectIntelligenceEvaluator.FormatReport(report))
            .AppendLine()
            .AppendLine("EVAL BUCKETS")
            .AppendLine(string.Join(", ", samples.GroupBy(s => s.Bucket).Select(g => $"{g.Key}={g.Count()}")))
            .AppendLine()
            .AppendLine("EXAMPLE PROFILES");
        foreach (var profile in examples)
        {
            sb.Append("ProjectId=").Append(profile.ProjectId)
                .Append(" Number=").Append(profile.ProjectNumber)
                .Append(" Name=").Append(profile.Facts.CanonicalName)
                .Append(" Place=").Append(profile.Facts.Place)
                .Append(" Client=").Append(profile.Facts.Client)
                .AppendLine();
            sb.Append("  Observed: ").AppendLine(string.Join(" | ", profile.Observed.DistinctivePhrases.Take(8)));
            sb.Append("  AiDerived: ").AppendLine(string.Join(" | ",
                profile.AiDerived.Aliases.Concat(profile.AiDerived.DistinctivePhrases).Take(8)));
            sb.Append("  Hash=").Append(profile.Metadata.SourceHash[..Math.Min(12, profile.Metadata.SourceHash.Length)])
                .AppendLine();
        }

        return sb.ToString();
    }

    private static void WriteReport(string text)
    {
        var dir = @"d:\repos2026\SiNetProjectManager_GitHub\artifacts\project-intelligence";
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "phase1-eval.txt"), text);
    }
}
