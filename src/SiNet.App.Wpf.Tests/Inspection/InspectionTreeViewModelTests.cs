using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Inspection;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

/// <summary>
/// Unit tests for <see cref="InspectionTreeViewModel.SelectReportByIdAsync"/>, the task-mode
/// selection used by the workflow-first open path. These lock in the core navigation guarantee:
/// the EXACT report is selected, and there is NEVER a fallback to the first/last report when the
/// target is missing (the previous selection must not linger either).
/// <para>
/// The fake <see cref="IInspectionWorkspace"/> returns already-completed tasks so the view model's
/// internal fire-and-forget reload (triggered by the <c>SelectedSeries</c> setter) runs
/// synchronously and the post-condition assertions are deterministic.
/// </para>
/// </summary>
public sealed class InspectionTreeViewModelTests
{
    private static InspectionReportRow Report(int id, int number)
        => new(id, number, new DateTime(2025, 1, number, 0, 0, 0, DateTimeKind.Utc), $"Inspector {number}");

    [Fact]
    public async Task SelectReportByIdAsync_selects_the_exact_report()
    {
        // Two series; the target report lives in the SECOND series. Exact selection must find it
        // there (not stop at the first series, not pick the first report).
        var workspace = new FakeWorkspace
        {
            SeriesByProject =
            {
                [10] = new[] { new InspectionSeriesSummary(1, "S1"), new InspectionSeriesSummary(2, "S2") }
            },
            ReportsBySeries =
            {
                [(10, 1)] = new[] { Report(100, 1), Report(101, 2) },
                [(10, 2)] = new[] { Report(200, 1), Report(201, 2) }
            }
        };
        var sut = new InspectionTreeViewModel(workspace);

        var ok = await sut.SelectReportByIdAsync(projectId: 10, reportId: 201);

        Assert.True(ok);
        Assert.NotNull(sut.SelectedReport);
        Assert.Equal(201, sut.SelectedReport!.Value.ReportId);
        Assert.Equal(2, sut.SelectedSeries!.Value.SeriesId);
    }

    [Fact]
    public async Task SelectReportByIdAsync_returns_false_when_report_is_missing()
    {
        // Case 5: the report id is not present in any series of the project. The method must report
        // failure and select nothing — no arbitrary/first report.
        var workspace = new FakeWorkspace
        {
            SeriesByProject = { [10] = new[] { new InspectionSeriesSummary(1, "S1") } },
            ReportsBySeries = { [(10, 1)] = new[] { Report(100, 1), Report(101, 2) } }
        };
        var sut = new InspectionTreeViewModel(workspace);

        var ok = await sut.SelectReportByIdAsync(projectId: 10, reportId: 999);

        Assert.False(ok);
        Assert.Null(sut.SelectedReport);
    }

    [Fact]
    public async Task SelectReportByIdAsync_does_not_keep_previous_selection_after_failure()
    {
        // First select a valid report so SelectedReport is non-null, then attempt a missing one.
        // The failed attempt must clear the prior selection (no stale/leftover report).
        var workspace = new FakeWorkspace
        {
            SeriesByProject = { [10] = new[] { new InspectionSeriesSummary(1, "S1") } },
            ReportsBySeries = { [(10, 1)] = new[] { Report(100, 1), Report(101, 2) } }
        };
        var sut = new InspectionTreeViewModel(workspace);

        var first = await sut.SelectReportByIdAsync(projectId: 10, reportId: 101);
        Assert.True(first);
        Assert.Equal(101, sut.SelectedReport!.Value.ReportId);

        var second = await sut.SelectReportByIdAsync(projectId: 10, reportId: 999);

        Assert.False(second);
        Assert.Null(sut.SelectedReport);
    }

    [Fact]
    public async Task SelectReportByIdAsync_returns_false_when_project_has_no_series()
    {
        var workspace = new FakeWorkspace();
        var sut = new InspectionTreeViewModel(workspace);

        var ok = await sut.SelectReportByIdAsync(projectId: 10, reportId: 100);

        Assert.False(ok);
        Assert.Null(sut.SelectedReport);
    }

    private sealed class FakeWorkspace : IInspectionWorkspace
    {
        public Dictionary<int, IReadOnlyList<InspectionSeriesSummary>> SeriesByProject { get; } = new();

        public Dictionary<(int ProjectId, int SeriesId), IReadOnlyList<InspectionReportRow>> ReportsBySeries { get; } = new();

        public Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
            int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(SeriesByProject.TryGetValue(projectId, out var s)
                ? s
                : Array.Empty<InspectionSeriesSummary>());

        public Task<IReadOnlyList<InspectionReportRow>> GetReportsAsync(
            int projectId, int seriesId, CancellationToken cancellationToken = default)
            => Task.FromResult(ReportsBySeries.TryGetValue((projectId, seriesId), out var r)
                ? r
                : Array.Empty<InspectionReportRow>());

        public Task<IReadOnlyList<InspectionNoteRow>> GetNotesAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionNoteRow>>(Array.Empty<InspectionNoteRow>());

        public Task<InspectionReportDetail?> GetReportDetailAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<InspectionReportDetail?>(null);

        public Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionChapterNode>>([]);

        public Task<IReadOnlyList<InspectionDrawingRow>> GetDrawingsAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionDrawingRow>>([]);

        public Task<IReadOnlyList<InspectionReviewedFileRow>> GetReviewedFilesAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionReviewedFileRow>>([]);
    }
}
