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
        Assert.False(inspect.ShowTriggerGateTasks);
        Assert.Single(inspect.RequiredTasks);
        Assert.Contains("QuoteMaterialMissing", inspect.RequiredTasks[0].AllowedResultsEn, StringComparison.Ordinal);
        Assert.Contains("חסר חומר להצעה", inspect.RequiredTasks[0].AllowedResultsHe, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionInspect_AllRequiredTasksClosed_ListsRequiredTasksUnderTrigger()
    {
        var stage = MakeStage(tasks:
        [
            new WorkflowStageTaskGraphDto(
                Id: 1, StageId: 10, SortOrder: 1, IsRequired: true, Notes: null,
                TaskTypeName: "תיוק חומר ראשוני", TaskTypeCode: "FileInitialMaterials",
                AssigneeDisplay: "Office", HasInteraction: false, OpenMode: null, ComponentKey: null,
                AllowedTaskResults: Array.Empty<WorkflowLabeledCodeDto>()),
            new WorkflowStageTaskGraphDto(
                Id: 2, StageId: 10, SortOrder: 2, IsRequired: false, Notes: null,
                TaskTypeName: "אופציונלי", TaskTypeCode: "OptionalNote",
                AssigneeDisplay: "Office", HasInteraction: false, OpenMode: null, ComponentKey: null,
                AllowedTaskResults: Array.Empty<WorkflowLabeledCodeDto>()),
        ]);
        var transition = MakeTransition(
            id: 7, from: 10, to: 20,
            trigger: "AllRequiredTasksClosed", condition: "AllTasksComplete",
            resultCode: null, resultName: null,
            fromName: "קבלת חומר", toName: "תיוק חומר");

        var inspect = WorkflowCanvasTransitionInspectVm.From(transition, stage);

        Assert.True(inspect.ShowTriggerGateTasks);
        Assert.True(inspect.HasTriggerGateTasks);
        Assert.Single(inspect.TriggerGateTasks);
        Assert.Equal("FileInitialMaterials", inspect.TriggerGateTasks[0].TaskTypeCode);
        Assert.Contains("משימות שצריכות להיסגר", inspect.TriggerGateHeading, StringComparison.Ordinal);
        Assert.Contains("מיד מתחת", inspect.TriggerExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void StageInspect_Outgoing_IsBilingual()
    {
        var stage = MakeStage(tasks: Array.Empty<WorkflowStageTaskGraphDto>());
        var loop = MakeTransition(
            id: 1, from: 10, to: 10, trigger: "Manual", condition: "TaskResultEquals",
            resultCode: "QuoteMaterialMissing", resultName: "חסר חומר להצעה");

        var inspect = WorkflowCanvasStageInspectVm.From(
            stage,
            incoming: [],
            outgoing: [WorkflowCanvasPathSummaryVm.ForOutgoing(loop)]);

        Assert.Single(inspect.OutgoingTransitions);
        Assert.Contains("Manual", inspect.OutgoingTransitions[0].DisplayEn, StringComparison.Ordinal);
        Assert.Contains("ידני", inspect.OutgoingTransitions[0].DisplayHe, StringComparison.Ordinal);
        Assert.Contains("חסר חומר להצעה", inspect.OutgoingTransitions[0].DisplayHe, StringComparison.Ordinal);
    }

    [Fact]
    public void StageInspect_Incoming_OrderedAndExcludesSelfLoop()
    {
        var stage = MakeStage(id: 20, code: "MAT.Check", name: "בדיקת שלמות חומר", isInitial: false);
        var fromFile = MakeTransition(
            id: 2, from: 10, to: 20, fromName: "תיוק חומר", toName: "בדיקת שלמות חומר",
            trigger: "AllRequiredTasksClosed", condition: "AllTasksComplete",
            resultCode: null, resultName: null, priority: 1);
        var fromAwait = MakeTransition(
            id: 3, from: 30, to: 20, fromName: "ממתין להשלמה", toName: "בדיקת שלמות חומר",
            trigger: "TaskStatusChanged", condition: "TaskResultEquals",
            resultCode: "MissingMaterialReceived", resultName: "התקבלה השלמה", priority: 0);
        var selfLoop = MakeTransition(
            id: 4, from: 20, to: 20, fromName: "בדיקת שלמות חומר", toName: "בדיקת שלמות חומר",
            trigger: "Manual", condition: "Always",
            resultCode: null, resultName: null);

        // Mimic SelectStage: incoming excludes self-loop; self-loop only in outgoing.
        var incoming = new[] { fromAwait, fromFile }
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.Id)
            .Select(WorkflowCanvasPathSummaryVm.ForIncoming)
            .ToList();
        var outgoing = new[] { selfLoop }
            .Select(WorkflowCanvasPathSummaryVm.ForOutgoing)
            .ToList();

        var inspect = WorkflowCanvasStageInspectVm.From(stage, incoming, outgoing);

        Assert.Equal(2, inspect.IncomingTransitions.Count);
        Assert.Equal(3, inspect.IncomingTransitions[0].TransitionId);
        Assert.Equal(2, inspect.IncomingTransitions[1].TransitionId);
        Assert.Contains("ממתין להשלמה", inspect.IncomingTransitions[0].DisplayHe, StringComparison.Ordinal);
        Assert.True(inspect.IncomingTransitions[0].IsIncoming);
        Assert.Single(inspect.OutgoingTransitions);
        Assert.True(inspect.OutgoingTransitions[0].IsSelfLoop);
        Assert.False(inspect.HasInitialEntryHint);
    }

    [Fact]
    public void PathSummary_IncludesCreateStageTasksActionHe()
    {
        var transition = MakeTransition(
            id: 5, from: 10, to: 20, fromName: "קבלת חומר", toName: "תיוק חומר",
            trigger: "AllRequiredTasksClosed", condition: "AllTasksComplete",
            resultCode: null, resultName: null,
            actions:
            [
                new WorkflowTransitionActionGraphDto(
                    ActionType: "CreateStageTasks",
                    ActionTypeKnown: true,
                    ActionCode: null,
                    ConfigJson: null,
                    ConfigProjectStatusCode: null,
                    ConfigProjectStatusOk: true,
                    ConfigTaskResultCode: null,
                    ConfigTaskResultName: null,
                    ConfigTaskResultOk: true,
                    SortOrder: 1),
            ]);

        var summary = WorkflowCanvasPathSummaryVm.ForOutgoing(transition);

        Assert.True(summary.HasActions);
        Assert.Contains("CreateStageTasks", summary.ActionsSummaryEn, StringComparison.Ordinal);
        Assert.Contains("יצירת משימות שלב", summary.ActionsSummaryHe, StringComparison.Ordinal);
        Assert.Equal(5, summary.TransitionId);
    }

    [Fact]
    public void StageInspect_Initial_ShowsEntryHint()
    {
        var stage = MakeStage(id: 1, code: "MAT.Receive", name: "קבלת חומר", isInitial: true);
        var inspect = WorkflowCanvasStageInspectVm.From(stage, [], []);

        Assert.True(inspect.HasInitialEntryHint);
        Assert.Contains("StartWorkflow", inspect.InitialEntryHint, StringComparison.Ordinal);
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
        IReadOnlyList<WorkflowStageTaskGraphDto>? tasks = null,
        int id = 10,
        string code = "PRP.MaterialCheck",
        string name = "בדיקת חומר להצעת מחיר",
        bool isInitial = false) =>
        new(
            Id: id,
            Code: code,
            Name: name,
            Description: null,
            SortOrder: 20,
            IsInitial: isInitial,
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
                    StageId: id,
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
        int id,
        int from,
        int to,
        string trigger,
        string condition,
        string? resultCode,
        string? resultName,
        string fromName = "בדיקת חומר להצעת מחיר",
        string toName = "בדיקת חומר להצעת מחיר",
        int priority = 0,
        IReadOnlyList<WorkflowTransitionActionGraphDto>? actions = null) =>
        new(
            Id: id,
            Name: null,
            FromStageId: from,
            ToStageId: to,
            FromStageName: fromName,
            ToStageName: toName,
            TriggerType: trigger,
            TriggerTypeKnown: true,
            ConditionType: condition,
            ConditionTypeKnown: true,
            EvaluationMode: "Manual",
            EvaluationModeKnown: true,
            Priority: priority,
            ConditionJson: null,
            ConditionTaskResultCode: resultCode,
            ConditionTaskResultName: resultName,
            ConditionTaskResultOk: true,
            Actions: actions ?? Array.Empty<WorkflowTransitionActionGraphDto>());
}
