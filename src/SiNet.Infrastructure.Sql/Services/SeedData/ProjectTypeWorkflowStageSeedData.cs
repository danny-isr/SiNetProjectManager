using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Per-<c>ProjectType</c> activation profile for <c>PLN.*</c> workflow stages.
/// <para>
/// Each entry maps a <see cref="ProjectTypeMatch"/> (Hebrew title substring,
/// matched against <c>JobType.Title</c>) to the set of <see cref="PlanningStageCodes"/>
/// it activates. A stage not listed for a project type is treated as inactive
/// for that project type.
/// </para>
/// <para>
/// The <see cref="DefaultProfile"/> is used when no specific profile matches —
/// it includes the full happy path so that a new project type still has a
/// reasonable PlanningWorkflow out of the box.
/// </para>
/// </summary>
public static class ProjectTypeWorkflowStageSeedData
{
    /// <summary>
    /// The default activation profile applied to any project type that does
    /// not match a more specific <see cref="Profiles"/> entry.
    /// </summary>
    public static readonly StageActivation[] DefaultProfile = BuildFullProfile();

    /// <summary>
    /// Per-project-type overrides. The first matching profile wins;
    /// <see cref="DefaultProfile"/> is used otherwise.
    /// </summary>
    public static readonly ProjectTypeStageProfile[] Profiles = new[]
    {
        // Traffic-arrangement projects don't deliver detailed work plans the
        // same way design projects do — they still go through authority approval
        // but skip the WorkPlans stage.
        new ProjectTypeStageProfile(
            new ProjectTypeMatch("הסדר תנועה"),
            BuildProfileExcept(PlanningStageCodes.DesignWorkPlans)),

        // Street-development / road projects: full profile.
        new ProjectTypeStageProfile(
            new ProjectTypeMatch("פיתוח רחוב"),
            BuildFullProfile()),

        // Drainage projects: full profile.
        new ProjectTypeStageProfile(
            new ProjectTypeMatch("ניקוז"),
            BuildFullProfile()),
    };

    private static StageActivation[] BuildFullProfile()
    {
        var stages = new[]
        {
            PlanningStageCodes.Intake,
            PlanningStageCodes.QuoteProjectSetup,
            PlanningStageCodes.QuoteMaterialCheck,
            PlanningStageCodes.QuoteCalculation,
            PlanningStageCodes.QuotePreparation,
            PlanningStageCodes.QuoteInternalApproval,
            PlanningStageCodes.QuoteSentFollowUp,
            PlanningStageCodes.WorkOrder,
            PlanningStageCodes.ExecutionMaterialCheck,
            PlanningStageCodes.PlanningStart,
            PlanningStageCodes.DesignDraft,
            PlanningStageCodes.DesignPreliminary,
            PlanningStageCodes.DesignDetailed,
            PlanningStageCodes.ApprovalSubmission,
            PlanningStageCodes.ApprovalComments,
            PlanningStageCodes.ApprovalAuthorityApproved,
            PlanningStageCodes.DesignWorkPlans,
            PlanningStageCodes.BillingCheckMilestone,
            PlanningStageCodes.Close,
        };

        var sortOrder = 0;
        return stages.Select(code => new StageActivation(
            StageCode: code,
            IsRequired: true,
            CanRepeat: code is PlanningStageCodes.ApprovalSubmission
                              or PlanningStageCodes.ApprovalComments,
            SortOrder: sortOrder += 10
        )).ToArray();
    }

    private static StageActivation[] BuildProfileExcept(params string[] excludedCodes)
    {
        var excluded = new HashSet<string>(excludedCodes, StringComparer.OrdinalIgnoreCase);
        return BuildFullProfile()
            .Where(s => !excluded.Contains(s.StageCode))
            .ToArray();
    }

    /// <summary>One ProjectType → activated stages.</summary>
    public sealed record ProjectTypeStageProfile(
        ProjectTypeMatch Match,
        StageActivation[] Stages);

    /// <summary>How to find the target ProjectType (matched against <c>JobType.Title</c>).</summary>
    public sealed record ProjectTypeMatch(string TitleContains);

    /// <summary>Per-stage activation entry for a ProjectType.</summary>
    public sealed record StageActivation(
        string StageCode,
        bool IsRequired,
        bool CanRepeat,
        int SortOrder);
}
