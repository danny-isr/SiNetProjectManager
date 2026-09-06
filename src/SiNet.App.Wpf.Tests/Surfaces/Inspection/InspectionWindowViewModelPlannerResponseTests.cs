using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Projects;
using SiNet.App.Wpf.Surfaces.Inspection;
using Xunit;
using AppInspectionNoteRow = SiNet.Application.Abstractions.Inspection.InspectionNoteRow;
using AppInspectionReportRow = SiNet.Application.Abstractions.Inspection.InspectionReportRow;

namespace SiNet.App.Wpf.Tests.Surfaces.Inspection;

public sealed class InspectionWindowViewModelPlannerResponseTests
{
    [Fact]
    public async Task MarkResponseReceived_pulls_and_updates_status_when_spreadsheet_url_present()
    {
        var planner = new FakePlannerResponses();
        var workspace = new StubWorkspace(
            series: [new InspectionSeriesSummary(1, "סדרה")],
            reports: [new AppInspectionReportRow(9, 2, DateTime.Today, "E2E")],
            notes: [],
            detail: new InspectionReportDetail(
                ReportId: 9,
                ProjectId: 136,
                SeriesId: 1,
                ReportNumber: 2,
                InspectionDate: DateTime.Today,
                InspectorName: "E2E",
                ReviewedVersion: null,
                IsLockedAfterSend: false,
                SentAt: null,
                SentSpreadsheetUrl: "https://docs.google.com/spreadsheets/d/abc123/edit",
                SourceFileUrn: null,
                SourceFileVersion: null));

        var project = new RecordingProjectContext(new ProjectSummaryDto(
            136, "136", "SI", null, null, null, null, null, true));

        var sut = new InspectionWindowViewModel(
            workspace,
            currentProject: project,
            plannerResponses: planner);

        await sut.InitializeBrowseAsync().ConfigureAwait(true);
        Assert.True(sut.MarkResponseReceivedCommand.CanExecute(null));

        sut.MarkResponseReceivedCommand.Execute(null);
        await WaitUntilAsync(() => planner.Calls >= 1).ConfigureAwait(true);

        Assert.Equal(1, planner.Calls);
        Assert.False(planner.LastIsRepull);
        Assert.Equal(9, planner.LastReportId);
        Assert.Contains("נשמרו", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepullPlannerResponses_sets_isRepull_true()
    {
        var planner = new FakePlannerResponses();
        var workspace = new StubWorkspace(
            series: [new InspectionSeriesSummary(1, "סדרה")],
            reports: [new AppInspectionReportRow(9, 2, DateTime.Today, "E2E")],
            notes: [],
            detail: new InspectionReportDetail(
                9, 136, 1, 2, DateTime.Today, "E2E", null, false, null,
                "https://docs.google.com/spreadsheets/d/abc123/edit", null, null));
        var project = new RecordingProjectContext(new ProjectSummaryDto(
            136, "136", "SI", null, null, null, null, null, true));

        var sut = new InspectionWindowViewModel(
            workspace,
            currentProject: project,
            plannerResponses: planner);
        await sut.InitializeBrowseAsync().ConfigureAwait(true);

        sut.RepullPlannerResponsesCommand.Execute(null);
        await WaitUntilAsync(() => planner.Calls >= 1).ConfigureAwait(true);

        Assert.True(planner.LastIsRepull);
    }

    [Fact]
    public void MarkResponseReceived_disabled_when_no_exported_spreadsheet()
    {
        var sut = new InspectionWindowViewModel(
            workspace: new StubWorkspace([], [], [], detail: null),
            plannerResponses: new FakePlannerResponses());

        Assert.False(sut.MarkResponseReceivedCommand.CanExecute(null));
        Assert.False(sut.RepullPlannerResponsesCommand.CanExecute(null));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20).ConfigureAwait(true);
        }
    }

    private sealed class FakePlannerResponses : IInspectionPlannerResponseService
    {
        public int Calls { get; private set; }
        public int LastReportId { get; private set; }
        public bool LastIsRepull { get; private set; }

        public Task<InspectionPlannerResponsePullResult> PullAndPersistAsync(
            int reportId, bool isRepull, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastReportId = reportId;
            LastIsRepull = isRepull;
            return Task.FromResult(InspectionPlannerResponsePullResult.Ok(matched: 2, saved: 1));
        }
    }

    private sealed class RecordingProjectContext(ProjectSummaryDto? current) : ICurrentProjectContext
    {
        public ProjectSummaryDto? CurrentProject { get; private set; } = current;
        public event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged;

        public Task SetCurrentProjectAsync(ProjectSummaryDto? project, CancellationToken cancellationToken = default)
        {
            CurrentProject = project;
            CurrentProjectChanged?.Invoke(this, new ProjectChangedEventArgs(project));
            return Task.CompletedTask;
        }
    }

    private sealed class StubWorkspace : IInspectionWorkspace
    {
        private readonly IReadOnlyList<InspectionSeriesSummary> _series;
        private readonly IReadOnlyList<AppInspectionReportRow> _reports;
        private readonly IReadOnlyList<AppInspectionNoteRow> _notes;
        private readonly InspectionReportDetail? _detail;

        public StubWorkspace(
            IReadOnlyList<InspectionSeriesSummary> series,
            IReadOnlyList<AppInspectionReportRow> reports,
            IReadOnlyList<AppInspectionNoteRow> notes,
            InspectionReportDetail? detail)
        {
            _series = series;
            _reports = reports;
            _notes = notes;
            _detail = detail;
        }

        public Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
            int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_series);

        public Task<IReadOnlyList<AppInspectionReportRow>> GetReportsAsync(
            int projectId, int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_reports);

        public Task<IReadOnlyList<AppInspectionNoteRow>> GetNotesAsync(
            int reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_notes);

        public Task<InspectionReportDetail?> GetReportDetailAsync(
            int reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_detail);

        public Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
            int reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InspectionChapterNode>>([]);

        public Task<IReadOnlyList<InspectionGeneralFieldRow>> GetGeneralFieldsAsync(
            int reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InspectionGeneralFieldRow>>([]);

        public Task<IReadOnlyList<InspectionDrawingRow>> GetDrawingsAsync(
            int reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InspectionDrawingRow>>([]);

        public Task<IReadOnlyList<InspectionReviewedFileRow>> GetReviewedFilesAsync(
            int reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InspectionReviewedFileRow>>([]);
    }
}
