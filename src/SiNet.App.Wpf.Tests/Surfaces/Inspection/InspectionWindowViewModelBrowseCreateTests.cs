using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Projects;
using SiNet.App.Wpf.Surfaces.Inspection;
using Xunit;
using AppInspectionNoteRow = SiNet.Application.Abstractions.Inspection.InspectionNoteRow;
using AppInspectionReportRow = SiNet.Application.Abstractions.Inspection.InspectionReportRow;

namespace SiNet.App.Wpf.Tests.Surfaces.Inspection;

public sealed class InspectionWindowViewModelBrowseCreateTests
{
    [Fact]
    public async Task InitializeBrowseAsync_loads_project_reports()
    {
        var workspace = new StubWorkspace(
            series: [new(1, "Series A")],
            reports: [new(42, 7, new DateTime(2026, 7, 1), "Inspector")],
            notes: [new(1, "1.1", "Note text", "Open")]);
        var project = new RecordingProjectContext(new ProjectSummaryDto(
            ProjectId: 5,
            ProjectNumber: "100",
            ProjectName: "Test Project",
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true));

        var sut = new InspectionWindowViewModel(workspace, currentProject: project);
        await sut.InitializeBrowseAsync();

        Assert.Contains("100", sut.ActiveProjectDisplay, StringComparison.Ordinal);
        Assert.Single(sut.Reports);
        Assert.Equal(42, sut.SelectedReport?.ReportId);
        Assert.Single(sut.Notes);
        Assert.Contains("נפתח דוח #42", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedReport_change_loads_report_content()
    {
        var workspace = new StubWorkspace(
            series: [new(1, "S")],
            reports:
            [
                new(10, 1, DateTime.UtcNow, "A"),
                new(20, 2, DateTime.UtcNow, "B"),
            ],
            notes: [],
            notesByReport: new Dictionary<int, IReadOnlyList<AppInspectionNoteRow>>
            {
                [10] = [new(1, "1.1", "First", "Open")],
                [20] = [new(2, "2.1", "Second", "Open")],
            });
        var project = new RecordingProjectContext(new ProjectSummaryDto(
            5, "1", "P", null, null, null, null, null, true));

        var sut = new InspectionWindowViewModel(workspace, currentProject: project);
        await sut.InitializeBrowseAsync();

        sut.SelectedReport = sut.Reports.First(r => r.ReportId == 20);
        await WaitUntilAsync(() => sut.Notes.Any(n => n.NoteText.Contains("Second", StringComparison.Ordinal)));

        Assert.Contains(sut.Notes, n => n.NoteText.Contains("Second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateReportCommand_calls_command_service_and_reloads()
    {
        var workspace = new StubWorkspace(
            series: [new(1, "S")],
            reports: [new(99, 1, DateTime.UtcNow, "X")],
            notes: []);
        var project = new RecordingProjectContext(new ProjectSummaryDto(
            5, "1", "P", null, null, null, null, null, true));
        var catalog = new StubCatalog(
        [
            new InspectionTemplateCatalogItem("T1", "sheet-1", "https://docs.google.com/spreadsheets/d/sheet-1"),
        ]);
        var commands = new RecordingReportCommands();

        var sut = new InspectionWindowViewModel(
            workspace,
            currentProject: project,
            templateCatalog: catalog,
            reportCommands: commands);

        await sut.InitializeBrowseAsync();
        Assert.NotNull(sut.SelectedTemplate);

        // Simulate create returning a new report id that appears on next browse load
        commands.NextReportId = 99;
        sut.CreateReportCommand.Execute(null);
        await WaitUntilAsync(() => commands.CreateCallCount >= 1);

        Assert.Equal(1, commands.CreateCallCount);
        Assert.Equal(5, commands.LastProjectId);
        Assert.Equal("https://docs.google.com/spreadsheets/d/sheet-1", commands.LastTemplateUrl);
        Assert.Equal(99, sut.SelectedReport?.ReportId);
        Assert.Contains("דוח #99", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnlockReportCommand_calls_command_service()
    {
        var workspace = new StubWorkspace(
            series: [new(1, "S")],
            reports: [new(42, 1, DateTime.UtcNow, "I")],
            notes: []);
        var project = new RecordingProjectContext(new ProjectSummaryDto(
            5, "1", "P", null, null, null, null, null, true));
        var commands = new RecordingReportCommands();

        var sut = new InspectionWindowViewModel(
            workspace,
            currentProject: project,
            reportCommands: commands);
        await sut.InitializeBrowseAsync();

        sut.UnlockReportCommand.Execute(null);
        await WaitUntilAsync(() => commands.UnlockCallCount >= 1);

        Assert.Equal(1, commands.UnlockCallCount);
        Assert.Equal(42, commands.LastUnlockReportId);
        Assert.Contains("נעילה", sut.StatusMessage, StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }

    /*
     * Manual checklist (V2 host):
     * 1. Open New Shell / V2 menu → דוחות ביקורת with a current project selected.
     * 2. Click רענון תבניות → see Drive templates from settings folder.
     * 3. Select template → + דוח חדש → tree fills.
     * 4. Select a note → edit + שמור הערה.
     * 5. From a task: open existing report → השלם משימה when CanCompleteTask.
     * 6. Optional: 📤 export / 🔓 unlock.
     */

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

    private sealed class StubCatalog(IReadOnlyList<InspectionTemplateCatalogItem> items) : IInspectionTemplateCatalog
    {
        public Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListTemplatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(items);
    }

    private sealed class RecordingReportCommands : IInspectionReportCommandService
    {
        public int CreateCallCount { get; private set; }
        public int UnlockCallCount { get; private set; }
        public int? LastProjectId { get; private set; }
        public string? LastTemplateUrl { get; private set; }
        public int? LastUnlockReportId { get; private set; }
        public int NextReportId { get; set; } = 1;

        public Task<InspectionReportCommandResult> CreateReportAsync(
            int projectId,
            string templateUrl,
            int? seriesId = null,
            string? inspectorName = null,
            int? inspectorId = null,
            string? spreadsheetId = null,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            LastProjectId = projectId;
            LastTemplateUrl = templateUrl;
            return Task.FromResult(InspectionReportCommandResult.Ok(NextReportId));
        }

        public Task<InspectionReportCommandResult> UnlockReportAsync(
            int reportId, CancellationToken cancellationToken = default)
        {
            UnlockCallCount++;
            LastUnlockReportId = reportId;
            return Task.FromResult(InspectionReportCommandResult.Ok(reportId));
        }

        public Task<InspectionReportCommandResult> DeleteReportAsync(
            int reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult(InspectionReportCommandResult.Ok(reportId));

        public Task<InspectionReportCommandResult> SetReviewedVersionAsync(
            int reportId, string? reviewedVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(InspectionReportCommandResult.Ok(reportId));

        public Task<InspectionReportCommandResult> ReplaceReviewedFilesAsync(
            int reportId,
            IReadOnlyList<InspectionReviewedFileRow> files,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InspectionReportCommandResult.Ok(reportId));
    }

    private sealed class StubWorkspace : IInspectionWorkspace
    {
        private readonly IReadOnlyList<InspectionSeriesSummary> _series;
        private readonly IReadOnlyList<AppInspectionReportRow> _reports;
        private readonly IReadOnlyList<AppInspectionNoteRow> _notes;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<AppInspectionNoteRow>>? _notesByReport;

        public StubWorkspace(
            IReadOnlyList<InspectionSeriesSummary> series,
            IReadOnlyList<AppInspectionReportRow> reports,
            IReadOnlyList<AppInspectionNoteRow> notes,
            IReadOnlyDictionary<int, IReadOnlyList<AppInspectionNoteRow>>? notesByReport = null)
        {
            _series = series;
            _reports = reports;
            _notes = notes;
            _notesByReport = notesByReport;
        }

        public Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(_series);

        public Task<IReadOnlyList<AppInspectionReportRow>> GetReportsAsync(
            int projectId, int seriesId, CancellationToken cancellationToken = default)
            => Task.FromResult(_reports);

        public Task<IReadOnlyList<AppInspectionNoteRow>> GetNotesAsync(
            int reportId, CancellationToken cancellationToken = default)
        {
            if (_notesByReport is not null && _notesByReport.TryGetValue(reportId, out var specific))
                return Task.FromResult(specific);
            return Task.FromResult(_notes);
        }

        public Task<InspectionReportDetail?> GetReportDetailAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<InspectionReportDetail?>(new InspectionReportDetail(
                reportId, 5, 1, 1, DateTime.UtcNow, "I", null, false, null, null,
                "https://docs.google.com/spreadsheets/d/abc", null));

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
