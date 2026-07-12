using SiNet.App.Wpf.Surfaces.Workflow;
using SiNet.Application.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Workflow;

public sealed class WorkflowCanvasInspectVmTests
{
    [Fact]
    public void TransitionInspect_SelfLoop_ShowsBilingualTriggerAndResult()
    {
        var stage = MakeStage();
        var transition = MakeTransition(
            id: 99,
            from: 10,
            to: 10,
            trigger: "Manual",
            condition: "TaskResultEquals",
            resultCode: "QuoteMaterialMissing",
            resultName: "חסר חומר להצעה");

        var inspect = WorkflowCanvasTransitionInspectVm.From(transition, stage);

        Assert.True(inspect.IsSelfLoop);
        Assert.Equal("Manual", inspect.TriggerType);
        Assert.Equal("ידני", inspect.TriggerHe);
        Assert.Equal("QuoteMaterialMissing", inspect.TaskResultCode);
        Assert.Equal("חסר חומר להצעה", inspect.TaskResultName);
        Assert.True(inspect.HasNoActions);
        Assert.Single(inspect.RequiredTasks);
        Assert.Contains("QuoteMaterialMissing", inspect.RequiredTasks[0].AllowedResultsEn, StringComparison.Ordinal);
        Assert.Contains("חסר חומר להצעה", inspect.RequiredTasks[0].AllowedResultsHe, StringComparison.Ordinal);
    }

    [Fact]
    public void StageInspect_Outgoing_IsBilingual()
    {
        var stage = MakeStage(tasks: Array.Empty<WorkflowStageTaskGraphDto>());
        var loop = MakeTransition(
            id: 1, from: 10, to: 10, trigger: "Manual", condition: "TaskResultEquals",
            resultCode: "QuoteMaterialMissing", resultName: "חסר חומר להצעה");

        var inspect = WorkflowCanvasStageInspectVm.From(stage, [WorkflowCanvasOutgoingTransitionVm.From(loop)]);

        Assert.Single(inspect.OutgoingTransitions);
        Assert.Contains("Manual", inspect.OutgoingTransitions[0].DisplayEn, StringComparison.Ordinal);
        Assert.Contains("ידני", inspect.OutgoingTransitions[0].DisplayHe, StringComparison.Ordinal);
        Assert.Contains("חסר חומר להצעה", inspect.OutgoingTransitions[0].DisplayHe, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeLateral_FansParallelEdges_AndBiasesReversePairs()
    {
        var a = WorkflowCanvasLabels.ComputeLateral(0, 2, hasReversePair: false, fromLessThanTo: true);
        var b = WorkflowCanvasLabels.ComputeLateral(1, 2, hasReversePair: false, fromLessThanTo: true);
        Assert.NotEqual(a, b);
        Assert.Equal(-11, a);
        Assert.Equal(11, b);

        var forward = WorkflowCanvasLabels.ComputeLateral(0, 1, hasReversePair: true, fromLessThanTo: true);
        var backward = WorkflowCanvasLabels.ComputeLateral(0, 1, hasReversePair: true, fromLessThanTo: false);
        Assert.Equal(-16, forward);
        Assert.Equal(16, backward);
    }

    [Fact]
    public void TriggerHe_TaskStatusChanged_IsHebrew()
    {
        Assert.Equal("שינוי סטטוס/תוצאת משימה", WorkflowCanvasLabels.TriggerHe("TaskStatusChanged"));
        Assert.True(WorkflowCanvasLabels.IsEmphasizedTrigger("TaskStatusChanged"));
    }

    private static WorkflowStageGraphDto MakeStage(
        IReadOnlyList<WorkflowStageTaskGraphDto>? tasks = null) =>
        new(
            Id: 10,
            Code: "PRP.MaterialCheck",
            Name: "בדיקת חומר להצעת מחיר",
            Description: null,
            SortOrder: 20,
            IsInitial: false,
            IsFinal: false,
            NodeType: "Stage",
            NodeTypeKnown: true,
            IsSystem: true,
            AssignedGroupName: "Office",
            AssignedGroupCode: "OfficeManagement",
            SubWorkflowName: null,
            SubWorkflowCode: null,
            CanvasX: 100,
            CanvasY: 120,
            StageTasks: tasks ??
            [
                new WorkflowStageTaskGraphDto(
                    Id: 1,
                    StageId: 10,
                    SortOrder: 1,
                    IsRequired: true,
                    Notes: null,
                    TaskTypeName: "בדיקת שלמות חומר",
                    TaskTypeCode: "CheckQuoteMaterialCompleteness",
                    AssigneeDisplay: "Office",
                    HasInteraction: false,
                    OpenMode: null,
                    ComponentKey: null,
                    AllowedTaskResults:
                    [
                        new WorkflowLabeledCodeDto("QuoteMaterialComplete", "חומר להצעה הושלם"),
                        new WorkflowLabeledCodeDto("QuoteMaterialMissing", "חסר חומר להצעה"),
                    ]),
            ]);

    private static WorkflowTransitionGraphDto MakeTransition(
        int id, int from, int to, string trigger, string condition, string? resultCode, string? resultName) =>
        new(
            Id: id,
            Name: null,
            FromStageId: from,
            ToStageId: to,
            FromStageName: "בדיקת חומר להצעת מחיר",
            ToStageName: "בדיקת חומר להצעת מחיר",
            TriggerType: trigger,
            TriggerTypeKnown: true,
            ConditionType: condition,
            ConditionTypeKnown: true,
            EvaluationMode: "Manual",
            EvaluationModeKnown: true,
            Priority: 0,
            ConditionJson: null,
            ConditionTaskResultCode: resultCode,
            ConditionTaskResultName: resultName,
            ConditionTaskResultOk: true,
            Actions: Array.Empty<WorkflowTransitionActionGraphDto>());
}
