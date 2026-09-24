using System.IO;
using System.Threading;
using System.Windows;
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

    [Fact]
    public async Task A_preview_that_finishes_after_the_selection_changed_does_not_enable_commit()
    {
        var gate = new TaskCompletionSource<WorkflowAdoptionPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adoption = new GatedAdoptionService(gate);
        var vm = new AdoptExistingWorkflowViewModel(adoption);
        vm.ProjectId = 5;
        await vm.LoadAsync().ConfigureAwait(true);

        var preview = vm.PreviewAsync();
        vm.Reports[0].Choice = "פעיל";
        gate.SetResult(new WorkflowAdoptionPreview(
            WorkflowAdoptionDisposition.ReadyToAdopt,
            true,
            "אפשר להטמיע",
            5,
            "פרויקט",
            5,
            "Active",
            3,
            "בדיקה",
            9,
            "Review",
            "תהליך בדיקה",
            "REV.ProfessionalReview",
            "בדיקה מקצועית",
            50,
            "Stage",
            false,
            null,
            null,
            [],
            [],
            [],
            [],
            [],
            [],
            7));
        await preview.ConfigureAwait(true);

        Assert.False(vm.CommitCommand.CanExecute(null));
    }

    [Fact]
    public void Report_choice_automation_id_uses_report_id_not_report_number()
    {
        var first = new AdoptionReportRowVm(new WorkflowAdoptionReportPreview(
            12, 1, null, false, false, "לא נבחר", 2, "E2E Adoption Test"));
        var sameNumberOtherSeries = new AdoptionReportRowVm(new WorkflowAdoptionReportPreview(
            99, 1, null, false, false, "לא נבחר", 8, "Other Series"));

        Assert.Equal("Adoption.Report.12", first.ChoiceAutomationId);
        Assert.Equal("Adoption.Report.99", sameNumberOtherSeries.ChoiceAutomationId);
        Assert.NotEqual(first.ChoiceAutomationId, sameNumberOtherSeries.ChoiceAutomationId);
    }

    [Fact]
    public void Preview_textbox_binding_is_one_way()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/WorkflowOps/AdoptExistingWorkflowView.xaml");
        Assert.Contains("Text=\"{Binding PreviewText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Adopt_dialog_view_shows_without_readonly_preview_binding_exception()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new AdoptExistingWorkflowView
                {
                    DataContext = new AdoptExistingWorkflowViewModel(new FakeAdoptionService()),
                };
                var window = new Window
                {
                    Content = view,
                    Width = 640,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
                window.UpdateLayout();
                window.Close();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (caught is not null)
            throw new Xunit.Sdk.XunitException(caught.ToString());
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Repo file not found: {relativePath}");
    }

    private sealed class GatedAdoptionService(TaskCompletionSource<WorkflowAdoptionPreview> gate) : IWorkflowAdoptionService
    {
        public ValueTask<WorkflowAdoptionOptions> GetOptionsAsync(int projectId, CancellationToken ct)
        {
            var stage = new WorkflowAdoptionStageOption(
                "REV.ProfessionalReview", "בדיקה מקצועית", 50, [new WorkflowAdoptionUserOption(7, "Dana")]);
            var job = new WorkflowAdoptionJobTypeOption(3, "בדיקה", [stage]);
            var workflow = new WorkflowAdoptionWorkflowOption(9, "Review", "תהליך בדיקה", [job]);
            var report = new WorkflowAdoptionReportPreview(4, 1, null, false, false, "לא נבחר", 12, "בדיקת תנועה");
            return new(new WorkflowAdoptionOptions(projectId, "פרויקט", [workflow], [report], null));
        }

        public async ValueTask<WorkflowAdoptionPreview> PreviewAsync(WorkflowAdoptionRequest request, CancellationToken ct) =>
            await gate.Task.ConfigureAwait(false);

        public ValueTask<WorkflowAdoptionCommitResult> CommitAsync(WorkflowAdoptionRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("Commit must stay disabled.");
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
