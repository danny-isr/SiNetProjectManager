using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using SiNet.App.Wpf.Surfaces.Inspection;
using Xunit;
using AppInspectionNoteRow = SiNet.Application.Abstractions.Inspection.InspectionNoteRow;
using AppInspectionReportRow = SiNet.Application.Abstractions.Inspection.InspectionReportRow;

namespace SiNet.App.Wpf.Tests.Surfaces.Inspection;

public sealed class InspectionWindowViewModelTaskModeTests
{
    [Fact]
    public async Task ApplyContextAsync_loads_exact_report_and_notes()
    {
        var workspace = new StubInspectionWorkspace(
            series: [new(1, "Series A")],
            reports: [new(42, 7, new DateTime(2026, 7, 1), "Inspector")],
            notes: [new(1, "1.1", "Note text", "Open")]);

        var sut = new InspectionWindowViewModel(workspace, new RecordingCompletion());
        var context = CreateContext(taskId: 10, projectId: 5, reportId: 42);

        var ok = await sut.ApplyContextAsync(context);

        Assert.True(ok);
        Assert.True(sut.IsTaskMode);
        Assert.True(sut.CanCompleteTask);
        Assert.NotNull(sut.SelectedReport);
        Assert.Single(sut.Notes);
        Assert.Contains("Opened inspection report #42", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_missing_report_blocks_without_fallback()
    {
        var workspace = new StubInspectionWorkspace(
            series: [new(1, "Series A")],
            reports: [new(99, 1, DateTime.UtcNow, "X")],
            notes: []);

        var sut = new InspectionWindowViewModel(workspace);
        var ok = await sut.ApplyContextAsync(CreateContext(taskId: 10, projectId: 5, reportId: 42));

        Assert.False(ok);
        Assert.False(sut.CanCompleteTask);
        Assert.Null(sut.SelectedReport);
        Assert.Contains("No fallback", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_PerformProfessionalReview_without_report_enters_creation_mode()
    {
        var workspace = new StubInspectionWorkspace(
            series: [new(1, "Series A")],
            reports: [],
            notes: []);

        var sut = new InspectionWindowViewModel(workspace);
        var context = CreateContext(taskId: 10, projectId: 5, reportId: 0) with
        {
            PrimaryWorkTargetEntityId = null,
            TaskTypeCode = "PerformProfessionalReview",
        };

        var ok = await sut.ApplyContextAsync(context);

        Assert.True(ok);
        Assert.True(sut.IsTaskMode);
        Assert.False(sut.CanCompleteTask);
        Assert.Contains("צור או בחר דוח", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_FixReport_without_report_is_blocked()
    {
        var sut = new InspectionWindowViewModel(new StubInspectionWorkspace([], [], []));
        var context = CreateContext(taskId: 10, projectId: 5, reportId: 0) with
        {
            PrimaryWorkTargetEntityId = null,
            TaskTypeCode = "FixReportPerManager",
        };

        var ok = await sut.ApplyContextAsync(context);

        Assert.False(ok);
        Assert.Contains("אינה מקושרת לדוח בדיקה קיים", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_wrong_component_key_is_rejected()
    {
        var sut = new InspectionWindowViewModel(new StubInspectionWorkspace([], [], []));
        var context = CreateContext(taskId: 10, projectId: 5, reportId: 42) with
        {
            ComponentKey = WorkSurfaceComponentKeys.EmailFiling,
        };

        var ok = await sut.ApplyContextAsync(context);

        Assert.False(ok);
        Assert.Contains("not the Inspection surface", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_blocks_when_user_unknown()
    {
        var workspace = new StubInspectionWorkspace(
            series: [new(1, "S")],
            reports: [new(42, 1, DateTime.UtcNow, "I")],
            notes: []);
        var completion = new RecordingCompletion();
        var sut = new InspectionWindowViewModel(workspace, completion);
        var context = CreateContext(taskId: 10, projectId: 5, reportId: 42) with { ActingUserId = null };

        Assert.True(await sut.ApplyContextAsync(context));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.Contains("acting user is unknown", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_blocks_when_event_code_missing()
    {
        var workspace = new StubInspectionWorkspace(
            series: [new(1, "S")],
            reports: [new(42, 1, DateTime.UtcNow, "I")],
            notes: []);
        var completion = new RecordingCompletion();
        var sut = new InspectionWindowViewModel(workspace, completion);
        var context = CreateContext(taskId: 10, projectId: 5, reportId: 42) with
        {
            CompletionEventCode = null,
            TaskTypeCode = "UnknownType",
            AllowedResultCodes = [],
        };

        Assert.True(await sut.ApplyContextAsync(context));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.Contains("no completion event", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_calls_completion_service_when_resolved()
    {
        var workspace = new StubInspectionWorkspace(
            series: [new(1, "S")],
            reports: [new(42, 1, DateTime.UtcNow, "I")],
            notes: []);
        var completion = new RecordingCompletion();
        var sut = new InspectionWindowViewModel(workspace, completion);
        var context = CreateContext(taskId: 10, projectId: 5, reportId: 42);

        Assert.True(await sut.ApplyContextAsync(context));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal(10, completion.LastCommand?.TaskId);
        Assert.Equal("ReviewProfessionalReviewCompleted", completion.LastCommand?.CompletionEventCode);
        Assert.Equal(7, completion.LastCommand?.UserId);
        Assert.Contains("Completed task #10", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_multiple_allowed_results_exposes_picker_and_blocks_until_selected()
    {
        var workspace = new StubInspectionWorkspace(
            series: [new(1, "S")],
            reports: [new(4, 1, DateTime.UtcNow, "I")],
            notes: []);
        var completion = new RecordingCompletion();
        var sut = new InspectionWindowViewModel(workspace, completion);
        var context = CreateContext(taskId: 299, projectId: 136, reportId: 4) with
        {
            ComponentKey = WorkSurfaceComponentKeys.ManagerReviewApproval,
            TaskTypeCode = "ApproveReviewReport",
            AllowedResultCodes = ["ManagerApproved", "ManagerRequestedChanges"],
            CompletionEventCode = null,
        };

        Assert.True(await sut.ApplyContextAsync(context));
        Assert.True(sut.HasMultipleAllowedResultCodes);
        Assert.Null(sut.SelectedResultCode);

        Assert.False(await sut.CompleteFromTaskAsync());
        Assert.Equal(0, completion.CallCount);
        Assert.Contains("allows multiple results", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);

        sut.SelectedResultCode = "ManagerApproved";
        Assert.True(await sut.CompleteFromTaskAsync(
            completionEventCode: "Review.ManagerApproved"));
        Assert.Equal(1, completion.CallCount);
        Assert.Equal("ManagerApproved", completion.LastCommand?.TaskResultCode);
        Assert.Equal("Review.ManagerApproved", completion.LastCommand?.CompletionEventCode);
    }

    private static WorkSurfaceContext CreateContext(int taskId, int projectId, int reportId) =>
        new(
            TaskId: taskId,
            ProjectId: projectId,
            WorkflowInstanceId: 1,
            ComponentKey: WorkSurfaceComponentKeys.InspectionReport,
            PrimaryWorkTargetEntityId: reportId,
            AllowedResultCodes: ["ProfessionalReviewCompleted"],
            CompletionEventCode: "ReviewProfessionalReviewCompleted",
            ActingUserId: 7,
            TaskTypeCode: "PerformProfessionalReview");

    private sealed class StubInspectionWorkspace(
        IReadOnlyList<InspectionSeriesSummary> series,
        IReadOnlyList<AppInspectionReportRow> reports,
        IReadOnlyList<AppInspectionNoteRow> notes) : IInspectionWorkspace
    {
        public Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(series);

        public Task<IReadOnlyList<AppInspectionReportRow>> GetReportsAsync(
            int projectId, int seriesId, CancellationToken cancellationToken = default)
            => Task.FromResult(reports);

        public Task<IReadOnlyList<AppInspectionNoteRow>> GetNotesAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult(notes);

        public Task<InspectionReportDetail?> GetReportDetailAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<InspectionReportDetail?>(null);

        public Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionChapterNode>>([]);

        public Task<IReadOnlyList<InspectionGeneralFieldRow>> GetGeneralFieldsAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionGeneralFieldRow>>([]);

        public Task<IReadOnlyList<InspectionDrawingRow>> GetDrawingsAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionDrawingRow>>([]);

        public Task<IReadOnlyList<InspectionReviewedFileRow>> GetReviewedFilesAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionReviewedFileRow>>([]);
    }

    private sealed class RecordingCompletion : ITaskCompletionService
    {
        public int CallCount { get; private set; }
        public CompleteTaskCommand? LastCommand { get; private set; }

        public ValueTask<TaskCompletionResultDto> CompleteAsync(CompleteTaskCommand command, CancellationToken ct)
        {
            CallCount++;
            LastCommand = command;
            return ValueTask.FromResult(new TaskCompletionResultDto(
                Success: true,
                TaskClosed: true,
                WorkflowAdvanced: true));
        }
    }
}
