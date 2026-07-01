using System.Diagnostics;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// Responsiveness tests for <see cref="ProjectSelectorViewModel"/> covering the perf/stability slice's
/// guarantees (see <c>docs/PROJECTS.md</c>, <c>docs/AI_DEVELOPMENT_GUIDE.md</c>): the constructor stays
/// cheap, filter setters never block the caller on the underlying query, rapid changes debounce/cancel so
/// only the latest keystroke's result reaches the UI, stale (superseded) results are dropped even if they
/// finish last, and the default load is capped rather than unbounded.
/// </summary>
public sealed class ProjectSelectorViewModelResponsivenessTests
{
    private static ProjectSummaryDto Project(int id, string number, string name = "Project")
        => new(
            ProjectId: id,
            ProjectNumber: number,
            ProjectName: name,
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true);

    /// <summary>
    /// A configurable, recording <see cref="IProjectQueryService"/> fake: <paramref name="search"/> decides
    /// what each call returns (immediately or gated behind an externally-controlled
    /// <see cref="TaskCompletionSource{TResult}"/>), and every call is counted with its search text recorded
    /// so tests can assert exactly what reached the "database".
    /// </summary>
    private sealed class RecordingProjectQueryService(
        Func<ProjectSearchQuery, Task<IReadOnlyList<ProjectSummaryDto>>> search) : IProjectQueryService
    {
        public int CallCount { get; private set; }

        public List<string?> ReceivedSearchTexts { get; } = [];

        public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
            ProjectSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ReceivedSearchTexts.Add(query.SearchText);
            return search(query);
        }

        public Task<ProjectSummaryDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectSummaryDto?>(null);
    }

    [Fact]
    public void Constructor_does_not_query_the_service()
    {
        // Constructors must stay cheap (docs/AI_DEVELOPMENT_GUIDE.md): building the view model must not
        // itself trigger a DB/query call. Loading only happens via an explicit LoadAsync/RefreshCommand.
        var service = new RecordingProjectQueryService(
            _ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(Array.Empty<ProjectSummaryDto>()));

        _ = new ProjectSelectorViewModel(service, new InMemoryCurrentProjectContext(), TimeSpan.Zero);

        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public void SearchText_setter_returns_immediately_without_awaiting_the_query()
    {
        // The setter must be fire-and-forget: it schedules a debounced reload and returns immediately,
        // even when the underlying query never completes (simulating a slow/hung database call).
        var service = new RecordingProjectQueryService(
            _ => new TaskCompletionSource<IReadOnlyList<ProjectSummaryDto>>().Task);
        var sut = new ProjectSelectorViewModel(service, new InMemoryCurrentProjectContext(), TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        sut.SearchText = "abc";
        sw.Stop();

        Assert.True(
            sw.ElapsedMilliseconds < 200,
            $"SearchText setter took {sw.ElapsedMilliseconds} ms; it must return immediately, not block on the query.");
    }

    [Fact]
    public async Task Rapid_SearchText_changes_only_apply_the_last_query_regardless_of_completion_order()
    {
        // Three rapid keystrokes each schedule a reload (debounce = zero so there is no real delay to wait
        // out, meaning all three actually reach the service). Resolve the NEWEST first and the OLDER
        // (already-superseded) ones last, proving completion order never decides the winner: only the
        // final keystroke's result may ever reach the UI-bound Projects collection.
        var byText = new Dictionary<string, ProjectSummaryDto>
        {
            ["a"] = Project(1, "1001", "Result-A"),
            ["al"] = Project(2, "1002", "Result-AL"),
            ["alp"] = Project(3, "1003", "Result-ALP"),
        };
        var gates = new Dictionary<string, TaskCompletionSource<IReadOnlyList<ProjectSummaryDto>>>();

        var service = new RecordingProjectQueryService(query =>
        {
            var text = query.SearchText ?? string.Empty;
            var tcs = new TaskCompletionSource<IReadOnlyList<ProjectSummaryDto>>();
            gates[text] = tcs;
            return tcs.Task;
        });

        var sut = new ProjectSelectorViewModel(service, new InMemoryCurrentProjectContext(), TimeSpan.Zero);

        sut.SearchText = "a";
        sut.SearchText = "al";
        sut.SearchText = "alp";

        // All three keystrokes reached the service (debounce is zero in this test).
        Assert.Equal(3, service.CallCount);

        gates["alp"].TrySetResult([byText["alp"]]);
        await Task.Delay(50);
        gates["al"].TrySetResult([byText["al"]]);
        gates["a"].TrySetResult([byText["a"]]);
        await Task.Delay(50);

        Assert.Single(sut.Projects);
        Assert.Equal("Result-ALP", sut.Projects.Single().ProjectName);
    }

    [Fact]
    public async Task Concurrent_LoadAsync_calls_the_stale_call_never_overwrites_the_newer_one_even_if_it_finishes_last()
    {
        // Bypasses SearchText/cancellation entirely and calls LoadAsync() directly twice back-to-back (as
        // RefreshCommand would). The monotonic request id alone -- with no cancellation involved -- must
        // ensure the SLOWER, older call never overwrites the faster, newer one.
        var slowGate = new TaskCompletionSource<IReadOnlyList<ProjectSummaryDto>>();
        var callIndex = 0;
        IReadOnlyList<ProjectSummaryDto> slowResult = [Project(1, "1001", "Slow-Stale")];
        IReadOnlyList<ProjectSummaryDto> fastResult = [Project(2, "1002", "Fast-Fresh")];

        var service = new RecordingProjectQueryService(_ =>
        {
            callIndex++;
            return callIndex == 1 ? slowGate.Task : Task.FromResult(fastResult);
        });

        var sut = new ProjectSelectorViewModel(service, new InMemoryCurrentProjectContext(), TimeSpan.Zero);

        var slowLoad = sut.LoadAsync();
        var fastLoad = sut.LoadAsync();

        await fastLoad;
        Assert.Single(sut.Projects);
        Assert.Equal("Fast-Fresh", sut.Projects.Single().ProjectName);

        // Let the STALE (older) call finish LAST.
        slowGate.TrySetResult(slowResult);
        await slowLoad;

        // It must NOT have overwritten the fresher result.
        Assert.Single(sut.Projects);
        Assert.Equal("Fast-Fresh", sut.Projects.Single().ProjectName);
    }

    [Fact]
    public async Task Default_load_caps_results_instead_of_loading_everything_unbounded()
    {
        // A very large table must never flood the (ComboBox-bound) Projects collection: the selector caps
        // at DefaultMaxResults even though the fake "database" has more rows than that.
        IReadOnlyList<ProjectSummaryDto> many = Enumerable.Range(1, ProjectSelectorViewModel.DefaultMaxResults + 50)
            .Select(i => Project(i, (1000 + i).ToString()))
            .ToArray();

        var service = new RecordingProjectQueryService(
            query => Task.FromResult(ProjectSummaryQuery.Apply(many, query)));
        var sut = new ProjectSelectorViewModel(service, new InMemoryCurrentProjectContext(), TimeSpan.Zero);

        await sut.LoadAsync();

        Assert.Equal(ProjectSelectorViewModel.DefaultMaxResults, sut.Projects.Count);
    }

    [Fact]
    public async Task Debounce_coalesces_rapid_keystrokes_into_a_single_query()
    {
        // Unlike the other (TimeSpan.Zero) tests, this uses a small but REAL debounce window to prove
        // keystrokes fired within the window collapse into exactly one query against the service, instead
        // of one query per keystroke.
        IReadOnlyList<ProjectSummaryDto> results = [Project(1, "1001", "Alpha")];
        var service = new RecordingProjectQueryService(_ => Task.FromResult(results));
        var sut = new ProjectSelectorViewModel(
            service,
            new InMemoryCurrentProjectContext(),
            TimeSpan.FromMilliseconds(120));

        sut.SearchText = "a";
        sut.SearchText = "al";
        sut.SearchText = "alp";

        // Wait comfortably past the debounce window measured from the LAST keystroke.
        await Task.Delay(500);

        Assert.Equal(1, service.CallCount);
        Assert.Equal("alp", service.ReceivedSearchTexts.Single());
    }
}
