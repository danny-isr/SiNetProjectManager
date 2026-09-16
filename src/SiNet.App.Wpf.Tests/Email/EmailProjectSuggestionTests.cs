using System.IO;
using SiNet.Application.Ai;
using SiNet.Application.Email;
using SiNet.Application.ProjectIntelligence;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailProjectSuggestionTests
{
    [Fact]
    public void Exact_project_number_is_top1()
    {
        var source = Fixture();
        var result = new EmailProjectSuggestionService().Suggest("RE: 3070 weekly", source);

        Assert.Equal(3070, Assert.Single(result.Suggestions).Project.ProjectId);
        Assert.Same(source.Single(p => p.ProjectId == 3070), result.Suggestions[0].Project);
    }

    [Fact]
    public void Exact_distinctive_name_is_top1()
    {
        var result = new EmailProjectSuggestionService().Suggest("יבנה מזרח", Fixture());

        Assert.Equal(3070, Assert.Single(result.Suggestions).Project.ProjectId);
    }

    [Fact]
    public void Place_plus_project_phrase_ranks_the_named_project_first()
    {
        var result = new EmailProjectSuggestionService().Suggest(
            "הושלם הטיפול בפנייתך תיאום תכנון ביבנה מזרח",
            Fixture());

        Assert.True(result.Suggestions.Count >= 1);
        Assert.Equal(3070, result.Suggestions[0].Project.ProjectId);
        Assert.All(result.Suggestions, s => Assert.Contains(s.Project.ProjectId, Fixture().Select(p => p.ProjectId)));
    }

    [Fact]
    public void Two_similar_projects_in_same_place_keep_meaningful_order()
    {
        var east = new EmailProjectSuggestionService().Suggest("יבנה מזרח", Fixture());
        var west = new EmailProjectSuggestionService().Suggest("יבנה מערב", Fixture());

        Assert.Equal(3070, east.Suggestions[0].Project.ProjectId);
        Assert.DoesNotContain(east.Suggestions, s => s.Project.ProjectId == 3045);
        Assert.Equal(3045, west.Suggestions[0].Project.ProjectId);
        Assert.DoesNotContain(west.Suggestions, s => s.Project.ProjectId == 3070);
    }

    [Fact]
    public void Generic_quote_request_returns_no_suggestions()
    {
        var result = new EmailProjectSuggestionService().Suggest("בקשת הצעת מחיר", Fixture());
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void Generic_planning_token_returns_no_broad_false_positives()
    {
        var result = new EmailProjectSuggestionService().Suggest("תכנון", Fixture());
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void Suggested_project_ids_always_exist_in_the_source_collection()
    {
        var source = Fixture();
        var result = new EmailProjectSuggestionService().Suggest("קלאוזנר 7 רעננה | הגשה מלאה כולל נספח תנועה עדכני", source);

        Assert.Equal(101, Assert.Single(result.Suggestions).Project.ProjectId);
        Assert.Contains(result.Suggestions[0].Project, source);
        Assert.Equal("רעננה · עיריית רעננה", result.Suggestions[0].ExplanationHe);
    }

    [Fact]
    public void Duplicate_source_rows_never_appear_twice()
    {
        var source = Fixture().Concat(Fixture()).ToArray();
        var result = new EmailProjectSuggestionService().Suggest("3070", source);

        Assert.Equal(3070, Assert.Single(result.Suggestions).Project.ProjectId);
    }

    [Fact]
    public void Inactive_project_is_omitted_when_not_in_the_picker_source()
    {
        var closed = P(3070, "3070", "יבנה מזרח", "יבנה", "עיריית יבנה") with { IsActive = false };
        var activeOnly = Fixture().Where(p => p.ProjectId != 3070).ToArray();
        var service = new EmailProjectSuggestionService();

        var hidden = service.Suggest("יבנה מזרח", activeOnly);
        Assert.DoesNotContain(hidden.Suggestions, s => s.Project.ProjectId == 3070);

        var ifProvided = service.Suggest("יבנה מזרח", activeOnly.Append(closed).ToArray());
        Assert.Equal(closed, Assert.Single(ifProvided.Suggestions).Project);
    }

    [Fact]
    public void Place_digit_without_matching_project_fact_does_not_recommend_all_neighbors()
    {
        var result = new EmailProjectSuggestionService().Suggest("רעננה", Fixture());
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void Traffic_appendix_boilerplate_does_not_outrank_or_invent_matches()
    {
        var source = Fixture().Append(P(999, "999", "חאש - נספח תנועה", "אשקלון", "חברת חאש")).ToArray();
        var service = new EmailProjectSuggestionService();

        var klausner = service.Suggest("קלאוזנר 7 רעננה | הגשה מלאה כולל נספח תנועה עדכני", source);
        Assert.Equal(101, Assert.Single(klausner.Suggestions).Project.ProjectId);

        var appendix = service.Suggest("נספח תנועה עדכני", source);
        Assert.Empty(appendix.Suggestions);
    }

    [Fact]
    public void Suggestion_uses_cleaned_subject_not_later_search_text()
    {
        var service = new EmailProjectSuggestionService();
        var first = service.Suggest("השב: 3070 יבנה מזרח", Fixture());
        var typedLater = service.Suggest("תכנון", Fixture());

        Assert.Equal(3070, first.Suggestions[0].Project.ProjectId);
        Assert.Empty(typedLater.Suggestions);
    }

    [Fact]
    public void Retriever_meaningful_match_rejects_place_only_scores()
    {
        var facts = new ProjectIntelligenceFacts(202, "202", "הרצל 12", "202 הרצל 12", "רעננה", "חברת נתיבי איילון", null, null, true);
        var profile = new ProjectIntelligenceProfile(
            facts,
            new ProjectIntelligenceObserved([], []),
            new ProjectIntelligenceAiDerived([], [], [], [], [], null, null),
            "הרצל 12",
            new ProjectIntelligenceMetadata("x", DateTime.UtcNow, "t", null, null));
        var tokens = EmailFilingSubjectQuery.MeaningfulTokens("רעננה");
        var hit = ProjectIntelligenceRetriever.Score("רעננה", tokens, profile, ProjectIntelligenceLayers.Facts);

        Assert.False(ProjectIntelligenceRetriever.HasMeaningfulMatch(hit));
    }

    [Fact]
    public void Filing_picker_uses_local_suggestions_without_ai_or_gmail()
    {
        var host = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Email", "WpfEmailFilingProjectPickerHost.cs"));
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Email", "EmailFilingAiRecommendationsView.xaml"));

        Assert.Contains("IEmailProjectSuggestionService", host, StringComparison.Ordinal);
        Assert.Contains("ApplyLocal", host, StringComparison.Ordinal);
        Assert.Contains("selector.SearchText", host, StringComparison.Ordinal);
        Assert.Contains("SelectProjectCommand.Execute", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IAiCompletionService", host, StringComparison.Ordinal);
        Assert.DoesNotContain("RecommendAsync", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginLoading", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IGmail", host, StringComparison.Ordinal);
        Assert.DoesNotContain("FileToProject", host, StringComparison.Ordinal);
        Assert.Contains("הצעות", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("הצעות AI", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfidenceText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("מחפש פרויקטים מתאימים", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unused_ai_recommendation_port_still_calls_completion_in_isolation()
    {
        var completion = new CountingCompletion();
        var sut = new EmailProjectRecommendationService(
            completion,
            new StubProjects(P(1, "1", "A", "X", "Y")));

        await sut.RecommendAsync("A");
        Assert.Equal(1, completion.Calls);
    }

    private static IReadOnlyList<ProjectSummaryDto> Fixture() =>
    [
        P(3070, "3070", "יבנה מזרח", "יבנה", "עיריית יבנה"),
        P(3045, "3045", "יבנה מערב", "יבנה", "עיריית יבנה"),
        P(101, "101", "קלאוזנר 7", "רעננה", "עיריית רעננה"),
        P(202, "202", "הרצל 12", "רעננה", "חברת נתיבי איילון"),
        P(303, "303", "מגדל הים", "נתניה", "יזם אלפא"),
        P(404, "404", "גבעת שמואל צפון", "גבעת שמואל", "עיריית גבעת שמואל"),
        P(505, "505", "נס ציונה מדע", "נס ציונה", "עיריית נס ציונה"),
        P(606, "606", "אשדוד נמל", "אשדוד", "חברת נמל אשדוד"),
    ];

    private static ProjectSummaryDto P(int id, string number, string name, string place, string company)
        => new(id, number, name, place, company, null, null, null, true, null, null, number + " " + name);

    private sealed class StubProjects(params ProjectSummaryDto[] projects) : IProjectQueryService
    {
        public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
            ProjectSearchQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(projects);

        public Task<ProjectSummaryDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projects.FirstOrDefault(p => p.ProjectId == projectId));
    }

    private sealed class CountingCompletion : IAiCompletionService
    {
        public int Calls { get; private set; }

        public Task<AiCompletionResult> CompleteAsync(
            AiCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(AiCompletionResult.Ok(
                """{"recommendations":[]}""",
                AiProviderNames.Ollama,
                "test"));
        }

        public Task<bool> IsAvailableAsync(
            AiCompletionLevel level = AiCompletionLevel.Simple,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !File.Exists(Path.Combine(dir.FullName, "SiNetProjectManager_GitHub.sln"))
               && !File.Exists(Path.Combine(dir.FullName, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
