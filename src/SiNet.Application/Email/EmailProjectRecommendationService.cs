using System.Diagnostics;
using System.Text;
using SiNet.Application.Ai;
using SiNet.Application.Projects;

namespace SiNet.Application.Email;

/// <summary>
/// Future Subject → AI recommendation port. The filing picker must not call this;
/// interactive suggestions use <see cref="IEmailProjectSuggestionService"/> (Facts-only).
/// </summary>
public sealed class EmailProjectRecommendationService(
    IAiCompletionService completion,
    IProjectQueryService projects) : IEmailProjectRecommendationService
{
    public const int FullListMaxProjects = 500;
    public const int FullListMaxChars = 80_000;
    public const int PrefilterCap = 400;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private static readonly string Instructions =
        """
        You match an email Subject to office projects.
        Return JSON only: {"recommendations":[{"projectId":123,"confidence":0.0,"reason":"..."}]}
        Use only projectId values from the supplied list. Never invent ids or names.
        At most 5 recommendations, prefer 3 strong ones. confidence is 0..1.
        If there is no meaningful match return {"recommendations":[]}.
        """;

    private readonly IAiCompletionService _completion = completion ?? throw new ArgumentNullException(nameof(completion));
    private readonly IProjectQueryService _projects = projects ?? throw new ArgumentNullException(nameof(projects));

    public async Task<EmailProjectRecommendationResult> RecommendAsync(
        string? subject,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var cleaned = EmailFilingSubjectQuery.CleanSubject(subject);
        var available = await _projects
            .SearchProjectsAsync(new ProjectSearchQuery(IncludeClosed: false), cancellationToken)
            .ConfigureAwait(false);

        var (candidates, prefiltered) = await ReduceCandidatesAsync(cleaned, available, cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return EmailProjectRecommendationResult.Empty(
                "לא נמצאה המלצת AI", available.Count, 0, prefiltered, 0, sw.Elapsed);
        }

        var catalog = BuildCatalog(candidates);
        var prompt = new StringBuilder(catalog.Length + cleaned.Length + 80)
            .AppendLine("EMAIL SUBJECT:")
            .AppendLine(cleaned)
            .AppendLine()
            .AppendLine("PROJECTS:")
            .Append(catalog)
            .ToString();

        var ai = await _completion.CompleteAsync(
                new AiCompletionRequest(
                    prompt,
                    AiCompletionLevel.Simple,
                    Instructions,
                    ExpectJson: true,
                    Timeout: RequestTimeout),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ai.Succeeded)
        {
            if (ai.Failure is AiCompletionFailureKind.Cancelled)
            {
                return EmailProjectRecommendationResult.Empty(
                    null, available.Count, candidates.Count, prefiltered, prompt.Length, sw.Elapsed);
            }

            return EmailProjectRecommendationResult.Empty(
                string.IsNullOrWhiteSpace(ai.UserMessageHe) ? "המלצת AI אינה זמינה כרגע" : ai.UserMessageHe,
                available.Count,
                candidates.Count,
                prefiltered,
                prompt.Length,
                sw.Elapsed);
        }

        var sentIds = candidates.Select(static p => p.ProjectId).ToHashSet();
        var parsed = EmailProjectRecommendationParser.Parse(ai.Text, sentIds);
        if (parsed.Count == 0)
        {
            return EmailProjectRecommendationResult.Empty(
                "לא נמצאה המלצת AI",
                available.Count,
                candidates.Count,
                prefiltered,
                prompt.Length,
                sw.Elapsed);
        }

        var byId = candidates.ToDictionary(static p => p.ProjectId);
        var mapped = new List<EmailProjectRecommendation>(parsed.Count);
        foreach (var item in parsed)
        {
            if (byId.TryGetValue(item.ProjectId, out var project))
            {
                mapped.Add(new EmailProjectRecommendation(project, item.Confidence, item.Reason));
            }
        }

        return new EmailProjectRecommendationResult(
            mapped,
            mapped.Count == 0 ? "לא נמצאה המלצת AI" : null,
            available.Count,
            candidates.Count,
            prefiltered,
            prompt.Length,
            sw.Elapsed);
    }

    internal async Task<(IReadOnlyList<ProjectSummaryDto> Candidates, bool Prefiltered)> ReduceCandidatesAsync(
        string cleanedSubject,
        IReadOnlyList<ProjectSummaryDto> available,
        CancellationToken cancellationToken)
    {
        var catalog = BuildCatalog(available);
        if (available.Count <= FullListMaxProjects && catalog.Length <= FullListMaxChars)
        {
            return (available, false);
        }

        var tokens = EmailFilingSubjectQuery.MeaningfulTokens(cleanedSubject);
        IReadOnlyList<ProjectSummaryDto> narrowed;
        if (tokens.Count > 0)
        {
            narrowed = available.Where(p => tokens.Any(token => MatchesToken(p, token))).Take(PrefilterCap).ToArray();
        }
        else
        {
            narrowed = await _projects
                .SearchProjectsAsync(
                    new ProjectSearchQuery(
                        SearchText: cleanedSubject,
                        IncludeClosed: false,
                        MaxResults: PrefilterCap),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (narrowed.Count == 0)
        {
            narrowed = available.Take(PrefilterCap).ToArray();
        }

        return (narrowed, true);
    }

    internal static (IReadOnlyList<ProjectSummaryDto> Candidates, bool Prefiltered) ReduceCandidates(
        string cleanedSubject,
        IReadOnlyList<ProjectSummaryDto> available)
    {
        var catalog = BuildCatalog(available);
        if (available.Count <= FullListMaxProjects && catalog.Length <= FullListMaxChars)
        {
            return (available, false);
        }

        var tokens = EmailFilingSubjectQuery.MeaningfulTokens(cleanedSubject);
        IEnumerable<ProjectSummaryDto> narrowed = available;
        if (tokens.Count > 0)
        {
            narrowed = available.Where(p => tokens.Any(token => MatchesToken(p, token)));
        }
        else if (!string.IsNullOrWhiteSpace(cleanedSubject))
        {
            narrowed = available.Where(p => ContainsIgnoreCase(p, cleanedSubject));
        }

        var list = narrowed.Take(PrefilterCap).ToArray();
        if (list.Length == 0)
        {
            list = available.Take(PrefilterCap).ToArray();
        }

        return (list, true);
    }

    internal static string BuildCatalog(IEnumerable<ProjectSummaryDto> projects)
    {
        var sb = new StringBuilder();
        foreach (var p in projects)
        {
            sb.Append(p.ProjectId).Append('|')
                .Append(p.ProjectNumber).Append('|')
                .Append(p.ProjectName).Append('|')
                .Append(p.PlaceName).Append('|')
                .Append(p.ProjectLabelName)
                .AppendLine();
        }

        return sb.ToString();
    }

    private static bool MatchesToken(ProjectSummaryDto project, string token) =>
        Contains(project.ProjectNumber, token)
        || Contains(project.ProjectName, token)
        || Contains(project.PlaceName, token)
        || Contains(project.ProjectLabelName, token);

    private static bool ContainsIgnoreCase(ProjectSummaryDto project, string text) =>
        MatchesToken(project, text);

    private static bool Contains(string? value, string token) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
