using System.IO;
using SiNet.Application.Ai;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailProjectRecommendationTests
{
    [Fact]
    public void Parser_drops_invented_and_duplicate_ids()
    {
        var parsed = EmailProjectRecommendationParser.Parse(
            """{"recommendations":[{"projectId":10,"confidence":0.9},{"projectId":99,"confidence":0.95},{"projectId":10,"confidence":0.8}]}""",
            new HashSet<int> { 10, 11 });

        var item = Assert.Single(parsed);
        Assert.Equal(10, item.ProjectId);
        Assert.Equal(0.9, item.Confidence);
    }

    [Fact]
    public void Parser_filters_low_confidence_and_caps_at_five()
    {
        var json =
            """{"recommendations":[""" +
            """{"projectId":1,"confidence":0.2},""" +
            """{"projectId":2,"confidence":0.4},""" +
            """{"projectId":3,"confidence":0.41},""" +
            """{"projectId":4,"confidence":0.42},""" +
            """{"projectId":5,"confidence":0.43},""" +
            """{"projectId":6,"confidence":0.44},""" +
            """{"projectId":7,"confidence":0.45}]}""";

        var parsed = EmailProjectRecommendationParser.Parse(json, new HashSet<int> { 1, 2, 3, 4, 5, 6, 7 });

        Assert.DoesNotContain(parsed, x => x.ProjectId == 1);
        Assert.Equal(5, parsed.Count);
        Assert.Equal(7, parsed[0].ProjectId);
    }

    [Fact]
    public void Parser_prefers_three_strong_matches()
    {
        var json =
            """{"recommendations":[""" +
            """{"projectId":1,"confidence":0.9},""" +
            """{"projectId":2,"confidence":0.8},""" +
            """{"projectId":3,"confidence":0.7},""" +
            """{"projectId":4,"confidence":0.6}]}""";

        var parsed = EmailProjectRecommendationParser.Parse(json, new HashSet<int> { 1, 2, 3, 4 });

        Assert.Equal(3, parsed.Count);
        Assert.Equal([1, 2, 3], parsed.Select(x => x.ProjectId).ToArray());
    }

    [Fact]
    public void Parser_unwraps_markdown_and_string_ids()
    {
        var parsed = EmailProjectRecommendationParser.Parse(
            """```json{"recommendations":[{"projectId":"12","confidence":"0.7","reason":"tower"}]}```""",
            new HashSet<int> { 12 });

        var item = Assert.Single(parsed);
        Assert.Equal(12, item.ProjectId);
        Assert.Equal(0.7, item.Confidence);
        Assert.Equal("tower", item.Reason);
    }

    [Fact]
    public void Parser_malformed_json_is_empty()
    {
        Assert.Empty(EmailProjectRecommendationParser.Parse("{nope", new HashSet<int> { 1 }));
        Assert.Empty(EmailProjectRecommendationParser.Parse(null, new HashSet<int> { 1 }));
    }

    [Fact]
    public void CleanSubject_strips_reply_prefixes_including_hebrew()
    {
        Assert.Equal("Tower weekly", EmailFilingSubjectQuery.CleanSubject("השב: RE: Tower weekly"));
    }

    [Fact]
    public async Task Recommend_uses_Simple_and_returns_only_sent_real_projects()
    {
        var projects = new StubProjects(Project(10, "1010", "Tower A", "Tel Aviv"));
        var completion = new StubCompletion(
            """{"recommendations":[{"projectId":10,"confidence":0.8,"reason":"subject"},{"projectId":77,"confidence":0.99}]}""");
        var sut = new EmailProjectRecommendationService(completion, projects);

        var result = await sut.RecommendAsync("RE: Tower A");

        Assert.Equal(AiCompletionLevel.Simple, completion.LastRequest!.Level);
        Assert.True(completion.LastRequest.ExpectJson);
        var row = Assert.Single(result.Recommendations);
        Assert.Equal(10, row.Project.ProjectId);
        Assert.Equal("Tower A", row.Project.ProjectName);
        Assert.False(result.UsedLocalPrefilter);
        Assert.Contains("EMAIL SUBJECT:", completion.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("10|1010|Tower A|", completion.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("77", completion.LastRequest.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recommend_ai_failure_returns_empty_without_throwing()
    {
        var sut = new EmailProjectRecommendationService(
            new StubCompletion(AiCompletionResult.Fail(AiCompletionFailureKind.Unavailable, "down")),
            new StubProjects(Project(1, "1", "A")));

        var result = await sut.RecommendAsync("A");

        Assert.Empty(result.Recommendations);
        Assert.Equal("down", result.StatusMessageHe);
    }

    [Fact]
    public async Task Recommend_cancelled_is_empty_without_operator_error()
    {
        var sut = new EmailProjectRecommendationService(
            new StubCompletion(AiCompletionResult.Fail(AiCompletionFailureKind.Cancelled, "cancelled")),
            new StubProjects(Project(1, "1", "A")));

        var result = await sut.RecommendAsync("A");

        Assert.Empty(result.Recommendations);
        Assert.Null(result.StatusMessageHe);
    }

    [Fact]
    public async Task Recommend_prefilters_when_catalog_is_too_large()
    {
        var many = Enumerable.Range(1, EmailProjectRecommendationService.FullListMaxProjects + 1)
            .Select(i => Project(i, i.ToString(), i == 9 ? "UniqueTower" : "Other", i == 9 ? "Haifa" : "X"))
            .ToArray();
        var projects = new StubProjects(many);
        var completion = new StubCompletion("""{"recommendations":[{"projectId":9,"confidence":0.8}]}""");
        var sut = new EmailProjectRecommendationService(completion, projects);

        var result = await sut.RecommendAsync("UniqueTower weekly");

        Assert.True(result.UsedLocalPrefilter);
        Assert.True(result.SentCandidateCount <= EmailProjectRecommendationService.PrefilterCap);
        Assert.Contains("9|9|UniqueTower|", completion.LastRequest!.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("10|10|Other|", completion.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.Equal(9, Assert.Single(result.Recommendations).Project.ProjectId);
    }

    [Fact]
    public async Task Recommend_without_tokens_uses_search_query_when_prefiltering()
    {
        var many = Enumerable.Range(1, EmailProjectRecommendationService.FullListMaxProjects + 1)
            .Select(i => Project(i, i.ToString(), "P"))
            .ToArray();
        var projects = new StubProjects(many)
        {
            SearchOverride = query =>
            {
                Assert.Equal("hello", query.SearchText);
                Assert.Equal(EmailProjectRecommendationService.PrefilterCap, query.MaxResults);
                return [many[0]];
            },
        };
        var completion = new StubCompletion("""{"recommendations":[{"projectId":1,"confidence":0.9}]}""");
        var sut = new EmailProjectRecommendationService(completion, projects);

        var result = await sut.RecommendAsync("hello");

        Assert.True(result.UsedLocalPrefilter);
        Assert.Equal(1, result.SentCandidateCount);
        Assert.Equal(1, Assert.Single(result.Recommendations).Project.ProjectId);
    }

    [Fact]
    public void Session_drops_late_results()
    {
        var session = new EmailProjectPickerAiSession();
        var first = session.Begin();
        var second = session.Begin();
        Assert.False(session.IsCurrent(first));
        Assert.True(session.IsCurrent(second));
        session.Close();
        Assert.False(session.IsCurrent(second));
    }

    [Fact]
    public void Filing_picker_keeps_search_contract_and_does_not_auto_select()
    {
        var host = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Email", "WpfEmailFilingProjectPickerHost.cs"));
        Assert.Contains("PickProjectAsync(", host, StringComparison.Ordinal);
        Assert.Contains("string? initialSearchText = null", host, StringComparison.Ordinal);
        Assert.Contains("selector.SearchText", host, StringComparison.Ordinal);
        Assert.Contains("IEmailProjectSuggestionService", host, StringComparison.Ordinal);
        Assert.Contains("SelectProjectCommand.Execute", host, StringComparison.Ordinal);
        Assert.Contains("ApplySubjectPrefill", host, StringComparison.Ordinal);
        Assert.Contains("ProjectSelectorView", host, StringComparison.Ordinal);
        Assert.Contains("IncludeClosed: false", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmailProjectRecommendationService", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IAiCompletionService", host, StringComparison.Ordinal);
        Assert.DoesNotContain("RecommendAsync", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginLoading", host, StringComparison.Ordinal);
        Assert.DoesNotContain("FileToProject", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IGmail", host, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectProject(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogResult = true", host.Split("okButton.Click")[0], StringComparison.Ordinal);
    }

    private static ProjectSummaryDto Project(int id, string number, string name, string? place = null) =>
        new(id, number, name, place, null, null, null, null, true, ProjectLabelName: $"{number} {name}");

    private sealed class StubProjects(params ProjectSummaryDto[] projects) : IProjectQueryService
    {
        public Func<ProjectSearchQuery, IReadOnlyList<ProjectSummaryDto>>? SearchOverride { get; init; }

        public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
            ProjectSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            if (SearchOverride is not null && !string.IsNullOrWhiteSpace(query.SearchText))
            {
                return Task.FromResult(SearchOverride(query));
            }

            IEnumerable<ProjectSummaryDto> result = projects;
            if (!query.IncludeClosed)
            {
                result = result.Where(static p => p.IsActive);
            }

            if (query.MaxResults is { } max)
            {
                result = result.Take(max);
            }

            return Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(result.ToArray());
        }

        public Task<ProjectSummaryDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projects.FirstOrDefault(p => p.ProjectId == projectId));
    }

    private sealed class StubCompletion : IAiCompletionService
    {
        private readonly AiCompletionResult _result;

        public StubCompletion(string json)
            : this(AiCompletionResult.Ok(json, AiProviderNames.Ollama, "test-model"))
        {
        }

        public StubCompletion(AiCompletionResult result) => _result = result;

        public AiCompletionRequest? LastRequest { get; private set; }

        public Task<AiCompletionResult> CompleteAsync(
            AiCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
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
        return dir!.FullName.EndsWith("SiNetProjectManager_GitHub", StringComparison.OrdinalIgnoreCase)
            ? dir.FullName
            : dir.FullName;
    }
}
