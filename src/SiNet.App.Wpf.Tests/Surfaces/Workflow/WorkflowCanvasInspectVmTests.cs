using SiNet.App.Wpf.Surfaces.Workflow;
using SiNet.Application.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Workflow;

public sealed class WorkflowCanvasInspectVmTests
{
    [Fact]
    public void TransitionInspect_SelfLoop_ExplainsStayInStage_AndListsSourceTasks()
    {
        var stage = new WorkflowStageGraphDto(
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
            StageTasks:
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
                    AllowedTaskResultCodes: ["QuoteMaterialComplete", "QuoteMaterialMissing"]),
            ]);

        var transition = new WorkflowTransitionGraphDto(
            Id: 99,
            Name: null,
            FromStageId: 10,
            ToStageId: 10,
            FromStageName: stage.Name,
            ToStageName: stage.Name,
            TriggerType: "Manual",
            TriggerTypeKnown: true,
            ConditionType: "TaskResultEquals",
            ConditionTypeKnown: true,
            EvaluationMode: "Manual",
            EvaluationModeKnown: true,
            Priority: 0,
            ConditionJson: null,
            ConditionTaskResultCode: "QuoteMaterialMissing",
            ConditionTaskResultOk: true,
            Actions: Array.Empty<WorkflowTransitionActionGraphDto>());

        var inspect = WorkflowCanvasTransitionInspectVm.From(transition, stage);

        Assert.True(inspect.IsSelfLoop);
        Assert.Contains("לולאה עצמית", inspect.Title, StringComparison.Ordinal);
        Assert.Equal("Manual", inspect.TriggerType);
        Assert.Contains("ידנית", inspect.TriggerExplanation, StringComparison.Ordinal);
        Assert.Equal("QuoteMaterialMissing", inspect.TaskResult);
        Assert.Single(inspect.RequiredTasks);
        Assert.Equal("CheckQuoteMaterialCompleteness", inspect.RequiredTasks[0].TaskTypeCode);
        Assert.Contains("QuoteMaterialMissing", inspect.RequiredTasks[0].AllowedResults, StringComparison.Ordinal);
    }

    [Fact]
    public void StageInspect_ListsOutgoingSelfLoop_WithLoopTag()
    {
        var stage = new WorkflowStageGraphDto(
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
            AssignedGroupName: null,
            AssignedGroupCode: null,
            SubWorkflowName: null,
            SubWorkflowCode: null,
            CanvasX: 0,
            CanvasY: 0,
            StageTasks: Array.Empty<WorkflowStageTaskGraphDto>());

        var loop = new WorkflowTransitionGraphDto(
            Id: 1,
            Name: null,
            FromStageId: 10,
            ToStageId: 10,
            FromStageName: stage.Name,
            ToStageName: stage.Name,
            TriggerType: "Manual",
            TriggerTypeKnown: true,
            ConditionType: "TaskResultEquals",
            ConditionTypeKnown: true,
            EvaluationMode: "Manual",
            EvaluationModeKnown: true,
            Priority: 0,
            ConditionJson: null,
            ConditionTaskResultCode: "QuoteMaterialMissing",
            ConditionTaskResultOk: true,
            Actions: Array.Empty<WorkflowTransitionActionGraphDto>());

        var outgoing = new[] { WorkflowCanvasOutgoingTransitionVm.From(loop) };
        var inspect = WorkflowCanvasStageInspectVm.From(stage, outgoing);

        Assert.Single(inspect.OutgoingTransitions);
        Assert.True(inspect.OutgoingTransitions[0].IsSelfLoop);
        Assert.Equal("לולאה", inspect.OutgoingTransitions[0].LoopTag);
        Assert.StartsWith("↻", inspect.OutgoingTransitions[0].Display, StringComparison.Ordinal);
    }
}
