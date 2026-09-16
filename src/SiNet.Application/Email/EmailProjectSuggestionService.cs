using System.Diagnostics;
using System.Text;
using SiNet.Application.ProjectIntelligence;
using SiNet.Application.Projects;

namespace SiNet.Application.Email;

public sealed record EmailProjectSuggestion(
    ProjectSummaryDto Project,
    string? ExplanationHe);

public sealed record EmailProjectSuggestionResult(
    IReadOnlyList<EmailProjectSuggestion> Suggestions,
    TimeSpan Elapsed,
    int CatalogCount,
    TimeSpan CatalogBuildElapsed)
{
    public static EmailProjectSuggestionResult Empty(TimeSpan elapsed, int catalogCount, TimeSpan catalogBuild) =>
        new([], elapsed, catalogCount, catalogBuild);
}

public interface IEmailProjectSuggestionService
{
    EmailProjectSuggestionResult Suggest(string? subject, IReadOnlyList<ProjectSummaryDto> available);
}

/// <summary>
/// Facts-only local ranking for the filing picker. Never calls AI or Gmail.
/// </summary>
public sealed class EmailProjectSuggestionService : IEmailProjectSuggestionService
{
    public const int PreferredCount = 3;
    public const int MaxCount = 5;

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> EmptyObserved =
        new Dictionary<int, IReadOnlyList<string>>();

    private readonly ProjectIntelligenceRetriever _retriever = new();
    private readonly object _gate = new();
    private IReadOnlyList<ProjectIntelligenceProfile>? _catalog;
    private string? _stamp;

    public EmailProjectSuggestionResult Suggest(string? subject, IReadOnlyList<ProjectSummaryDto> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var sw = Stopwatch.StartNew();

        var source = available
            .DistinctBy(static p => p.ProjectId)
            .ToArray();
        if (source.Length == 0)
        {
            return EmailProjectSuggestionResult.Empty(sw.Elapsed, 0, TimeSpan.Zero);
        }

        var catalog = CatalogFor(source, out var catalogBuild);
        var hits = _retriever.Retrieve(subject, catalog, ProjectIntelligenceLayers.Facts, MaxCount + 2);
        var byId = source.ToDictionary(static p => p.ProjectId);
        var ranked = new List<(EmailProjectSuggestion Suggestion, int Score)>(hits.Count);
        var seen = new HashSet<int>();
        foreach (var hit in hits)
        {
            if (!ProjectIntelligenceRetriever.HasMeaningfulMatch(hit))
            {
                continue;
            }

            if (!byId.TryGetValue(hit.ProjectId, out var project) || !seen.Add(hit.ProjectId))
            {
                continue;
            }

            ranked.Add((new EmailProjectSuggestion(project, Explain(project)), hit.Score));
        }

        if (ranked.Count == 0)
        {
            return EmailProjectSuggestionResult.Empty(sw.Elapsed, catalog.Count, catalogBuild);
        }

        var take = Math.Min(PreferredCount, ranked.Count);
        if (ranked.Count > PreferredCount)
        {
            var thirdScore = ranked[PreferredCount - 1].Score;
            for (var i = PreferredCount; i < Math.Min(MaxCount, ranked.Count); i++)
            {
                if (ranked[i].Score == thirdScore)
                {
                    take = i + 1;
                }
                else
                {
                    break;
                }
            }
        }

        return new EmailProjectSuggestionResult(
            ranked.Take(take).Select(static r => r.Suggestion).ToArray(),
            sw.Elapsed,
            catalog.Count,
            catalogBuild);
    }

    private IReadOnlyList<ProjectIntelligenceProfile> CatalogFor(
        IReadOnlyList<ProjectSummaryDto> projects,
        out TimeSpan buildElapsed)
    {
        var stamp = BuildStamp(projects);
        var sw = Stopwatch.StartNew();
        lock (_gate)
        {
            if (_catalog is not null && string.Equals(_stamp, stamp, StringComparison.Ordinal))
            {
                buildElapsed = sw.Elapsed;
                return _catalog;
            }

            _catalog = ProjectIntelligenceCatalogBuilder.BuildCatalog(projects, EmptyObserved);
            _stamp = stamp;
            buildElapsed = sw.Elapsed;
            return _catalog;
        }
    }

    private static string BuildStamp(IReadOnlyList<ProjectSummaryDto> projects)
    {
        var sb = new StringBuilder(projects.Count * 24);
        sb.Append(projects.Count).Append(';');
        foreach (var p in projects.OrderBy(static x => x.ProjectId))
        {
            sb.Append(p.ProjectId).Append('|')
                .Append(p.ProjectNumber).Append('|')
                .Append(p.ProjectName).Append('|')
                .Append(p.PlaceName).Append('|')
                .Append(p.CompanyName).Append('|')
                .Append(p.ProjectLabelName).Append('|')
                .Append(p.IsActive).Append(';');
        }

        return sb.ToString();
    }

    private static string? Explain(ProjectSummaryDto project)
    {
        if (!string.IsNullOrWhiteSpace(project.PlaceName)
            && !string.IsNullOrWhiteSpace(project.CompanyName))
        {
            return project.PlaceName + " · " + project.CompanyName;
        }

        if (!string.IsNullOrWhiteSpace(project.PlaceName))
        {
            return project.PlaceName;
        }

        return string.IsNullOrWhiteSpace(project.CompanyName) ? null : project.CompanyName;
    }
}
