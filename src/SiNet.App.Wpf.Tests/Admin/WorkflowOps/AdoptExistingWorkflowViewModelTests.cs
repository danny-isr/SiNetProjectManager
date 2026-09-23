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
