using SiNet.App.Wpf.Runtime;
using SiNet.Application.Notifications;
using SiNet.Application.Runtime;
using SiNet.Application.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Runtime;

public sealed class WorkflowAssigneeRuntimeStatusTests
{
    [Fact]
    public async Task RefreshAsync_with_issues_marks_workflow_assignees_Degraded_and_notifies()
    {
        var registry = new StartupTaskRegistry();
        var readiness = new StubReadiness(
        [
            new WorkflowAssigneeReadinessIssueDto(
                "PRP",
                "Calculation",
                "אומדן",
                "Planners",
                WorkflowAssigneeIssueKind.NoActiveMembers,
                "חסרים חברים"),
        ]);
        var notifications = new CapturingNotifications();

        using var service = new RuntimeSubsystemStatusService(
            registry,
            assigneeReadiness: readiness,
            notifications: notifications);

        await service.RefreshAsync();

        var row = Assert.Single(service.Current, s => s.Key == RuntimeSubsystemStatusService.WorkflowAssigneesKey);
        Assert.Equal(SubsystemRuntimeState.Degraded, row.State);
        Assert.Contains("1 שלבים", row.SummaryHe, StringComparison.Ordinal);
        Assert.Single(notifications.Delivered);
        Assert.Equal(
            RuntimeSubsystemStatusService.WorkflowAssigneeConfigMissingTemplate,
            notifications.Delivered[0].Template);
    }

    [Fact]
    public async Task RefreshAsync_with_no_issues_marks_Idle()
    {
        var registry = new StartupTaskRegistry();
        var readiness = new StubReadiness([]);

        using var service = new RuntimeSubsystemStatusService(
            registry,
            assigneeReadiness: readiness);

        await service.RefreshAsync();

        var row = Assert.Single(service.Current, s => s.Key == RuntimeSubsystemStatusService.WorkflowAssigneesKey);
        Assert.Equal(SubsystemRuntimeState.Idle, row.State);
        Assert.Equal("הקצאות workflow תקינות", row.SummaryHe);
    }

    private sealed class StubReadiness(IReadOnlyList<WorkflowAssigneeReadinessIssueDto> issues)
        : IWorkflowAssigneeReadinessQueryService
    {
        public Task<IReadOnlyList<WorkflowAssigneeReadinessIssueDto>> GetIssuesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(issues);
    }

    private sealed class CapturingNotifications : INotificationDeliveryService
    {
        public List<NotificationDeliveryRequest> Delivered { get; } = [];

        public ValueTask<NotificationDeliveryResult> DeliverAsync(
            NotificationDeliveryRequest request,
            CancellationToken cancellationToken = default)
        {
            Delivered.Add(request);
            return ValueTask.FromResult(NotificationDeliveryResult.Delivered("test"));
        }
    }
}
