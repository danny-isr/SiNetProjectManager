using SiNet.Application.Projects;

namespace SiNet.Application.ProjectIntelligence;

public static class ProjectIntelligenceCatalogBuilder
{
    public static string CleanSubject(string? subject) => ProjectIntelligenceText.Clean(subject);

    public static ProjectIntelligenceFacts ToFacts(ProjectSummaryDto project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new ProjectIntelligenceFacts(
            project.ProjectId,
            project.ProjectNumber,
            project.ProjectName,
            project.ProjectLabelName,
            project.PlaceName,
            project.CompanyName,
            project.Status,
            project.JobType,
            project.IsActive);
    }

    public static ProjectIntelligenceProfile Build(
        ProjectSummaryDto project,
        IEnumerable<string?> confirmedSubjects,
        ProjectIntelligenceAiDerived? aiDerived = null,
        DateTime? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(confirmedSubjects);

        var facts = ToFacts(project);
        var observed = ProjectIntelligenceObservedExtractor.Extract(confirmedSubjects, facts);
        var ai = aiDerived ?? EmptyAi();
        var fingerprint = BuildFingerprint(facts, observed, ai, ProjectIntelligenceLayers.All);
        var generated = generatedAtUtc ?? DateTime.UtcNow;
        return new ProjectIntelligenceProfile(
            facts,
            observed,
            ai,
            fingerprint,
            new ProjectIntelligenceMetadata(
                ProjectIntelligenceText.SourceHash(facts, observed.CleanedSubjects),
                generated,
                ProjectIntelligenceText.GeneratorVersion,
                ai.Provider,
                ai.Model));
    }

    public static IReadOnlyList<ProjectIntelligenceProfile> BuildCatalog(
        IReadOnlyList<ProjectSummaryDto> projects,
        IReadOnlyDictionary<int, IReadOnlyList<string>> subjectsByProject,
        IReadOnlyDictionary<int, ProjectIntelligenceAiDerived>? aiByProject = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(subjectsByProject);

        var catalog = new List<ProjectIntelligenceProfile>(projects.Count);
        foreach (var project in projects)
        {
            subjectsByProject.TryGetValue(project.ProjectId, out var subjects);
            ProjectIntelligenceAiDerived? ai = null;
            aiByProject?.TryGetValue(project.ProjectId, out ai);
            catalog.Add(Build(project, subjects ?? [], ai));
        }

        return catalog;
    }

    public static ProjectIntelligenceProfile WithHeldOutSubject(
        ProjectIntelligenceProfile profile,
        string subject)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var cleaned = ProjectIntelligenceText.Clean(subject);
        var remaining = profile.Observed.CleanedSubjects
            .Where(s => !string.Equals(s, cleaned, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var observed = ProjectIntelligenceObservedExtractor.Extract(remaining, profile.Facts);
        var fingerprint = BuildFingerprint(profile.Facts, observed, profile.AiDerived, ProjectIntelligenceLayers.All);
        return profile with
        {
            Observed = observed,
            NormalizedSearchText = fingerprint,
            Metadata = profile.Metadata with
            {
                SourceHash = ProjectIntelligenceText.SourceHash(profile.Facts, observed.CleanedSubjects),
            },
        };
    }

    public static string BuildFingerprint(
        ProjectIntelligenceFacts facts,
        ProjectIntelligenceObserved observed,
        ProjectIntelligenceAiDerived ai,
        ProjectIntelligenceLayers layers)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(ai);

        var parts = new List<string>();
        if (layers.HasFlag(ProjectIntelligenceLayers.Facts))
        {
            Add(parts, facts.ProjectNumber);
            Add(parts, facts.CanonicalName);
            Add(parts, facts.ProjectLabelName);
            Add(parts, facts.Place);
            Add(parts, facts.Client);
        }

        if (layers.HasFlag(ProjectIntelligenceLayers.Observed))
        {
            foreach (var phrase in observed.DistinctivePhrases)
            {
                Add(parts, phrase);
            }
        }

        if (layers.HasFlag(ProjectIntelligenceLayers.AiDerived))
        {
            foreach (var term in ai.Aliases.Concat(ai.Abbreviations).Concat(ai.DistinctivePhrases)
                         .Concat(ai.PlaceVariants).Concat(ai.Keywords))
            {
                Add(parts, term);
            }
        }

        return string.Join('\n', parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static string HierarchyKey(ProjectIntelligenceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var place = string.IsNullOrWhiteSpace(facts.Place) ? "?" : facts.Place.Trim();
        var client = string.IsNullOrWhiteSpace(facts.Client) ? "?" : facts.Client.Trim();
        return place + "\u001f" + client;
    }

    public static string EvaluateHierarchy(IReadOnlyList<ProjectIntelligenceProfile> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var withPlace = catalog.Count(static p => !string.IsNullOrWhiteSpace(p.Facts.Place));
        var withClient = catalog.Count(static p => !string.IsNullOrWhiteSpace(p.Facts.Client));
        var groups = catalog
            .Where(static p => !string.IsNullOrWhiteSpace(p.Facts.Place) && !string.IsNullOrWhiteSpace(p.Facts.Client))
            .GroupBy(static p => HierarchyKey(p.Facts), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var multi = groups.Count(static g => g.Count() >= 2);
        var placeGroups = catalog
            .Where(static p => !string.IsNullOrWhiteSpace(p.Facts.Place))
            .GroupBy(static p => p.Facts.Place!, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var placeMultiClient = placeGroups.Count(static g =>
            g.Select(p => p.Facts.Client ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() >= 2);

        if (withPlace < catalog.Count * 0.5 || groups.Length < 10)
        {
            return $"Hierarchy not forced: Place coverage {withPlace}/{catalog.Count}, Client {withClient}/{catalog.Count}. Use Place+Client only as a retrieval signal.";
        }

        return $"Place→Client usable as a signal (not the only index): {placeGroups.Length} places, {placeMultiClient} places with 2+ clients, {multi} Place+Client groups with 2+ projects.";
    }

    private static ProjectIntelligenceAiDerived EmptyAi()
        => new([], [], [], [], [], null, null);

    private static void Add(List<string> parts, string? value)
    {
        var cleaned = ProjectIntelligenceText.Clean(value);
        if (cleaned.Length >= 2)
        {
            parts.Add(cleaned);
        }
    }
}
