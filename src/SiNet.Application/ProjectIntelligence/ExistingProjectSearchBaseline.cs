using SiNet.Application.Email;
using SiNet.Application.Projects;

namespace SiNet.Application.ProjectIntelligence;

/// <summary>Strategy A: current deterministic picker search (no intelligence catalog).</summary>
public static class ExistingProjectSearchBaseline
{
    public static IReadOnlyList<int> Rank(
        string? subject,
        IReadOnlyList<ProjectSummaryDto> projects,
        int topN = 5)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var searchText = EmailFilingSubjectQuery.BuildSearchText(
            subject,
            text => ProjectSummaryQuery.Apply(
                projects,
                new ProjectSearchQuery(SearchText: text, IncludeClosed: false)).Count);

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [];
        }

        return ProjectSummaryQuery.Apply(
                projects,
                new ProjectSearchQuery(SearchText: searchText, IncludeClosed: false, MaxResults: topN))
            .Select(static p => p.ProjectId)
            .ToArray();
    }
}
