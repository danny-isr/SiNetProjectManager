using SiNet.Application.Projects;

namespace SiNet.Application.Email;

public sealed record EmailProjectRecommendationCandidate(
    int ProjectId,
    string ProjectNumber,
    string ProjectName,
    string? PlaceName,
    string? ProjectLabelName);

public sealed record EmailProjectRecommendation(
    ProjectSummaryDto Project,
    double Confidence,
    string? Reason);

public sealed record EmailProjectRecommendationResult(
    IReadOnlyList<EmailProjectRecommendation> Recommendations,
    string? StatusMessageHe,
    int AvailableProjectCount,
    int SentCandidateCount,
    bool UsedLocalPrefilter,
    int PromptChars,
    TimeSpan Elapsed)
{
    public static EmailProjectRecommendationResult Empty(
        string? statusMessageHe,
        int available,
        int sent,
        bool prefiltered,
        int promptChars,
        TimeSpan elapsed) =>
        new([], statusMessageHe, available, sent, prefiltered, promptChars, elapsed);
}

public interface IEmailProjectRecommendationService
{
    Task<EmailProjectRecommendationResult> RecommendAsync(
        string? subject,
        CancellationToken cancellationToken = default);
}

/// <summary>Generation guard so a late response cannot update a newer picker.</summary>
public sealed class EmailProjectPickerAiSession
{
    private int _generation;

    public int Begin() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(int generation) => Volatile.Read(ref _generation) == generation;

    public void Close() => Interlocked.Increment(ref _generation);
}
