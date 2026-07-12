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
        Assert.Equal(-WorkflowCanvasLabels.ReversePairGap, forward);
        Assert.Equal(WorkflowCanvasLabels.ReversePairGap, backward);
    }

    [Fact]
    public void Create_ReverseVerticalPair_SeparatesInWorldX()
    {
        const double nodeW = 160;
        const double nodeH = 72;
        var upper = new System.Windows.Point(100, 40);
        var lower = new System.Windows.Point(100, 220);
        var stroke = System.Windows.Media.Brushes.Gray;
        var selected = System.Windows.Media.Brushes.Orange;

        var down = MakeTransition(
            id: 1, from: 10, to: 20, trigger: "TaskStatusChanged", condition: "TaskResultEquals",
            resultCode: "MissingMaterialRequested", resultName: "נשלחה דרישה");
        var up = MakeTransition(
            id: 2, from: 20, to: 10, trigger: "TaskStatusChanged", condition: "TaskResultEquals",
            resultCode: "MissingMaterialReceived", resultName: "התקבלה השלמה");

        var downLateral = WorkflowCanvasLabels.ComputeLateral(0, 1, hasReversePair: true, fromLessThanTo: true);
        var upLateral = WorkflowCanvasLabels.ComputeLateral(0, 1, hasReversePair: true, fromLessThanTo: false);

        var downEdge = WorkflowCanvasEdgeVm.Create(down, upper, lower, nodeW, nodeH, downLateral, 0, stroke, selected);
        var upEdge = WorkflowCanvasEdgeVm.Create(up, lower, upper, nodeW, nodeH, upLateral, 0, stroke, selected);

        Assert.NotEqual(downEdge.X1, upEdge.X1);
        Assert.True(Math.Abs(downEdge.X1 - upEdge.X1) >= WorkflowCanvasLabels.ReversePairGap * 2 - 0.5);
        Assert.NotEqual(downEdge.LabelX, upEdge.LabelX);
    }

    [Fact]
    public void Create_VerticalEdge_TipOutsideTargetAabb_AndLabelAngleNearNinety()
    {
        const double nodeW = 140;
        const double nodeH = 56;
        var upper = new System.Windows.Point(100, 40);
        var lower = new System.Windows.Point(100, 200);
        var stroke = System.Windows.Media.Brushes.Gray;
        var selected = System.Windows.Media.Brushes.Orange;

        var down = MakeTransition(
            id: 1, from: 10, to: 20, trigger: "TaskStatusChanged", condition: "Always",
            resultCode: null, resultName: null);

        var edge = WorkflowCanvasEdgeVm.Create(down, upper, lower, nodeW, nodeH, 0, 0, stroke, selected);
        var tip = edge.ArrowPoints[0];

        Assert.True(tip.Y <= lower.Y - 1.5, $"Tip Y={tip.Y} should be above target top {lower.Y}");
        Assert.False(tip.X >= lower.X && tip.X <= lower.X + nodeW && tip.Y >= lower.Y && tip.Y <= lower.Y + nodeH);
        Assert.InRange(Math.Abs(edge.LabelAngle), 80, 100);
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
