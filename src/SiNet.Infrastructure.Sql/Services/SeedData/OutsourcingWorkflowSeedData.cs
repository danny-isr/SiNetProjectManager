using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Simple outsourcing workflow (<see cref="WorkflowCodes.Outsourcing"/>) —
/// receive quote → approve → monitor payments → complete.
/// Not mapped to JobTypes in seed; attach via admin policy when needed.
/// </summary>
public static class OutsourcingWorkflowSeedData
{
    public const string Code = WorkflowCodes.Outsourcing;
    public const string Name = "תהליך מיקור חוץ";
    public const string Description =
        "OUT.* — קבלת הצעת מחיר מיקור חוץ, אישור, מעקב תשלומים ידני, סיום.";

    public static readonly PlanningWorkflowSeedData.StageDefinition[] Stages =
    [
        Stage(OutsourcingStageCodes.ReceiveOffer, "קבלת הצעת מחיר מיקור חוץ", SortOrder: 10, index: 0, IsInitial: true),
        Stage(OutsourcingStageCodes.ApproveOffer, "אישור הצעת מיקור חוץ", SortOrder: 20, index: 1),
        Stage(OutsourcingStageCodes.MonitorPayments, "מעקב תשלומים", SortOrder: 30, index: 2),
        Stage(OutsourcingStageCodes.Complete, "סיום", SortOrder: 40, index: 3, IsFinal: true),
    ];

    private static PlanningWorkflowSeedData.StageDefinition Stage(
        string code, string name, int SortOrder, int index, bool IsInitial = false, bool IsFinal = false)
    {
        var (x, y) = PlanningWorkflowSeedData.LinearCanvasLayout.At(index);
        return new PlanningWorkflowSeedData.StageDefinition(code, name, SortOrder, IsInitial, IsFinal)
        {
            NodeType = "Stage",
            CanvasX = x,
            CanvasY = y,
        };
    }

    public static readonly PlanningWorkflowSeedData.StageTaskDefinition[] StageTasks =
    [
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OutsourcingStageCodes.ReceiveOffer,
            TaskTypeCode: TaskTypeCodes.ReceiveOutsourceQuote,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OutsourcingStageCodes.ApproveOffer,
            TaskTypeCode: TaskTypeCodes.ApproveOutsourceQuote,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OutsourcingStageCodes.MonitorPayments,
            TaskTypeCode: TaskTypeCodes.MonitorOutsourcePayments,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
    ];

    public static readonly IReadOnlyDictionary<string, string> StageGroupAssignments =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OutsourcingStageCodes.ReceiveOffer] = UserGroupCodes.OfficeManagement,
            [OutsourcingStageCodes.ApproveOffer] = UserGroupCodes.OfficeManagement,
            [OutsourcingStageCodes.MonitorPayments] = UserGroupCodes.OfficeManagement,
        };

    public static readonly PlanningWorkflowSeedData.StageTransitionDefinition[] Transitions =
    [
        AutoLinear(OutsourcingStageCodes.ReceiveOffer, OutsourcingStageCodes.ApproveOffer),
        AutoLinear(OutsourcingStageCodes.ApproveOffer, OutsourcingStageCodes.MonitorPayments),
        AutoLinear(OutsourcingStageCodes.MonitorPayments, OutsourcingStageCodes.Complete),
    ];

    private static PlanningWorkflowSeedData.StageTransitionDefinition AutoLinear(string from, string to)
        => new(from, to, TaskResultCode: null, Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.AllRequiredTasksClosed,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.AllTasksComplete,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };
}
