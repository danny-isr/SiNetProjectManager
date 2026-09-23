using SiNet.App.Wpf.Admin.WorkflowOps;
using SiNet.Application.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin.WorkflowOps;

public sealed class AdoptExistingWorkflowViewModelTests
{
    [Fact]
    public async Task Changing_report_choice_or_responsible_user_requires_a_new_preview()
    {
        var adoption = new FakeAdoptionService();
        var vm = new AdoptExistingWorkflowViewModel(adoption);
        vm.ProjectId = 5;

        await vm.LoadAsync().ConfigureAwait(true);
        await vm.PreviewAsync().ConfigureAwait(true);
        Assert.True(vm.CommitCommand.CanExecute(null));
        Assert.Equal(1, adoption.PreviewCalls);

        vm.Reports[0].Choice = "פעיל";
        Assert.False(vm.CommitCommand.CanExecute(null));
        Assert.Contains("אינה מעודכנת", vm.PreviewText, StringComparison.Ordinal);

        await vm.PreviewAsync().ConfigureAwait(true);
        Assert.True(vm.CommitCommand.CanExecute(null));

        vm.SelectedResponsible = vm.ResponsibleUsers.Single(u => u.UserId == 7);
        Assert.False(vm.CommitCommand.CanExecute(null));
        Assert.Contains("אינה מעודכנת", vm.PreviewText, StringComparison.Ordinal);
    }

    private sealed class FakeAdoptionService : IWorkflowAdoptionService
    {
        public int PreviewCalls { get; private set; }

        public ValueTask<WorkflowAdoptionOptions> GetOptionsAsync(int projectId, CancellationToken ct)
        {
            var stage = new WorkflowAdoptionStageOption(
                "REV.ProfessionalReview",
                "בדיקה מקצועית",
                50,
                [new WorkflowAdoptionUserOption(7, "Dana")]);
            var job = new WorkflowAdoptionJobTypeOption(3, "בדיקה", [stage]);
            var workflow = new WorkflowAdoptionWorkflowOption(9, "Review", "תהליך בדיקה", [job]);
            var report = new WorkflowAdoptionReportPreview(
                4, 1, null, false, false, "לא נבחר", 12, "בדיקת תנועה");
            return new(new WorkflowAdoptionOptions(projectId, "פרויקט", [workflow], [report], null));
        }

        public ValueTask<WorkflowAdoptionPreview> PreviewAsync(WorkflowAdoptionRequest request, CancellationToken ct)
        {
            PreviewCalls++;
            return new(new WorkflowAdoptionPreview(
                WorkflowAdoptionDisposition.ReadyToAdopt,
                true,
                "אפשר להטמיע",
                request.ProjectId,
                "פרויקט",
                5,
                "Active",
                request.JobTypeId,
                "בדיקה",
                request.WorkflowDefinitionId,
                "Review",
                "תהליך בדיקה",
                request.CurrentStageCode,
                "בדיקה מקצועית",
                50,
                "Stage",
                false,
                null,
                null,
                [],
                [new WorkflowAdoptionTaskPreview("PerformProfessionalReview", "ביצוע בדיקה מקצועית")],
                [],
                [],
                [],
                [],
                7));
        }

        public ValueTask<WorkflowAdoptionCommitResult> CommitAsync(WorkflowAdoptionRequest request, CancellationToken ct) =>
            new(new WorkflowAdoptionCommitResult(
                WorkflowAdoptionDisposition.Committed, "הוטמע", 1, 1, null, []));
    }
}
