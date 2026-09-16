namespace SiNet.Application.ProjectIntelligence;

/// <summary>SQL facts only. Never mix with AI aliases.</summary>
public sealed record ProjectIntelligenceFacts(
    int ProjectId,
    string ProjectNumber,
    string CanonicalName,
    string? ProjectLabelName,
    string? Place,
    string? Client,
    string? Status,
    string? JobType,
    bool IsActive);

/// <summary>Phrases that actually appeared on confirmed filed Subjects.</summary>
public sealed record ProjectIntelligenceObserved(
    IReadOnlyList<string> CleanedSubjects,
    IReadOnlyList<string> DistinctivePhrases);

/// <summary>Model-generated search terms. Never presented as DB facts.</summary>
public sealed record ProjectIntelligenceAiDerived(
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Abbreviations,
    IReadOnlyList<string> DistinctivePhrases,
    IReadOnlyList<string> PlaceVariants,
    IReadOnlyList<string> Keywords,
    string? Provider,
    string? Model);

public sealed record ProjectIntelligenceMetadata(
    string SourceHash,
    DateTime GeneratedAtUtc,
    string GeneratorVersion,
    string? AiProvider,
    string? AiModel);

/// <summary>Compact search profile. Layers stay separate.</summary>
public sealed record ProjectIntelligenceProfile(
    ProjectIntelligenceFacts Facts,
    ProjectIntelligenceObserved Observed,
    ProjectIntelligenceAiDerived AiDerived,
    string NormalizedSearchText,
    ProjectIntelligenceMetadata Metadata)
{
    public int ProjectId => Facts.ProjectId;
    public string ProjectNumber => Facts.ProjectNumber;
}

[Flags]
public enum ProjectIntelligenceLayers
{
    Facts = 1,
    Observed = 2,
    AiDerived = 4,
    FactsAndObserved = Facts | Observed,
    All = Facts | Observed | AiDerived,
}

public sealed record ProjectIntelligenceHit(
    int ProjectId,
    int Score,
    IReadOnlyList<string> Signals);

public sealed record ConfirmedProjectSubject(
    int ProjectId,
    string Subject,
    DateTime ReceivedUtc,
    string ThreadUniqueId);

public sealed record ProjectIntelligenceEvalSample(
    string Subject,
    int KnownProjectId,
    string Bucket,
    bool HeldOutFromObserved);

public sealed record ProjectIntelligenceStrategyMetrics(
    string Strategy,
    int SampleCount,
    double Top1,
    double Top3,
    double Top5,
    double NoCandidateRate,
    double AverageLatencyMs,
    double P95LatencyMs);

public sealed record ProjectIntelligenceEvalReport(
    int ConfirmedSubjectCount,
    int EvalSampleCount,
    IReadOnlyList<ProjectIntelligenceProfile> ExampleProfiles,
    IReadOnlyList<ProjectIntelligenceStrategyMetrics> Strategies,
    string? HierarchyNote,
    string? AiLevel,
    string? AiProvider,
    string? AiModel,
    double? AverageEnrichmentMs,
    ProjectIntelligenceStrategyMetrics? Rerank,
    string Recommendation);
