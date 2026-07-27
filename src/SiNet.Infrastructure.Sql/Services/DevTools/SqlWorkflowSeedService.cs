using Microsoft.EntityFrameworkCore;
using SiNet.Application.Diagnostics;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNet.Infrastructure.Sql.Services.Tasks;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Seeds the canonical <c>PlanningWorkflow</c> (PLN.*) definition together with
/// the default <see cref="UserGroup"/>s and the per-<c>ProjectType</c> mappings
/// (workflow assignment, stage activation, discipline activation).
/// <para>
/// All operations are idempotent — safe to call repeatedly.
/// </para>
/// </summary>
public class SqlWorkflowSeedService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlWorkflowSeedService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Seeds the canonical workflow surface:
    /// user groups, the <c>PlanningWorkflow</c> definition (PLN.*), and the
    /// per-<c>ProjectType</c> workflow / stage / discipline mappings.
    /// </summary>
    public async ValueTask SeedAllAsync(CancellationToken ct)
    {
        // 1. User groups (needed for stage assignments).
        await SeedUserGroupsAsync(ct);

        // 2. MaterialIntake (MAT.*) — reusable subworkflow. MUST be seeded
        //    BEFORE any parent workflow whose SubWorkflow stage links to it
        //    (PlanningWorkflow.PLN.Execution.MaterialCheck and
        //    Review.REV.MaterialIntake). See Docs/WorkflowDecisions.md
        //    (2026-05-23).
        await SeedMaterialIntakeWorkflowAsync(ct);

        // 3. PlanningWorkflow — the canonical PLN.* planning workflow definition.
        //    Separates ProjectStatus / WorkflowStage / TaskStatus / TaskResult.
        await SeedPlanningWorkflowAsync(ct);

        // 4. Review (REV.*) — תהליך בדיקת תוכנית. Separate from PlanningWorkflow.
        await SeedReviewWorkflowAsync(ct);

        // 5. Proposal (PRP.*) — independent price-quote workflow. Seeded BEFORE
        //    ProjectType mappings so consumers that resolve by code (e.g. the
        //    CreatePriceQuote email action) find an active definition. Proposal
        //    is started email-driven and is NOT auto-mapped to any ProjectType.
        await SeedProposalWorkflowAsync(ct);

        // 5a. Upgrade legacy PRP.MaterialCheck transitions that were seeded as
        //     Manual + QuoteMaterialComplete/Missing (never match auto-advance;
        //     UI emits MaterialComplete/MaterialMissing). Must run after Proposal
        //     seed so the canonical TaskStatusChanged rules exist or get fixed.
        await ReconcileProposalMaterialCheckTransitionsAsync(ct);

        // 5b. Diagnostic dump for the Proposal workflow shape that the runtime
        //     actually sees AFTER seeding. This is read-only and runs against the
        //     real application DB (not just clean test DBs), so it surfaces the
        //     gap between in-memory test seeds and an existing production DB that
        //     may be missing PRP.FileMaterial / its stage-task / its group / its
        //     ProjectSetup → FileMaterial transition. Logged via DevToolsLog so the
        //     output appears in the regular application log.
        await LogProposalRuntimeShapeAsync(ct);

        // 5c. Generic health dump for EVERY active WorkflowDefinition. Same idea
        //     as the Proposal-specific dump but workflow-agnostic — surfaces
        //     stages without task templates, task types without registry
        //     mappings, groups without resolvable assignees, and transitions
        //     pointing at missing/inactive results across the whole system.
        await LogAllWorkflowsRuntimeShapeAsync(ct);

        // 6. Opinion (OPN.*) — independent opinion workflow. Seeded BEFORE
        //    ProjectType mappings so the CreateOpinionProject email action
        //    finds an active definition. Opinion is started email-driven and
        //    is NOT auto-mapped to any ProjectType.
        await SeedOpinionWorkflowAsync(ct);

        // 7. ProjectType ↔ PlanningWorkflow mapping (default workflow per JobType).
        await SeedProjectTypeWorkflowMappingsAsync(ct);

        // 8. Per-ProjectType activation of PLN.* stages and disciplines.
        await SeedProjectTypeWorkflowStagesAsync(ct);
        await SeedProjectTypeDisciplinesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  User Groups Seed
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seeds the three default user groups: ניהול משרד, הנהלה בכירה, מתכננים.
    /// Idempotent — skips groups that already exist (by Code).
    /// </summary>
    private async ValueTask SeedUserGroupsAsync(CancellationToken ct)
    {
        var groups = new (string Code, string Name, string Description)[]
        {
            (UserGroupCodes.OfficeManagement, "ניהול משרד",   "אחראי על ניהול שוטף, פתיחת פרויקטים, תיוק, שליחת הצעות"),
            (UserGroupCodes.SeniorManagement, "הנהלה בכירה",  "אחראי על בדיקת שלמות חומר, אישור הצעות, החלטות עסקיות"),
            (UserGroupCodes.Planners,         "מתכננים",      "אחראי על תכנון, הכנת הצעות מחיר, בדיקה מקצועית"),

            // Review workflow groups (REV.*).
            (ReviewUserGroupCodes.ReviewIntake,    "קליטת בדיקות",      "אחראי על קליטת בקשות בדיקת תוכנית"),
            (ReviewUserGroupCodes.ProjectOpeners,  "פותחי פרויקטים",   "אחראי על פתיחת פרויקטי בדיקה ותיוק ראשוני"),
            (ReviewUserGroupCodes.Reviewers,       "בודקי תוכניות",    "אחראי על ביצוע הבדיקה המקצועית"),
            (ReviewUserGroupCodes.ReviewManagers,  "מנהלי בדיקה",      "אחראי על אישור דוחות בדיקה"),
            (ReviewUserGroupCodes.PoliceLiaison,   "קשר משטרה",        "אחראי על הגשות וקבלת אישורי משטרה"),
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existingCodes = await db.UserGroups
            .AsNoTracking()
            .Select(g => g.Code)
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var toAdd = new List<UserGroup>();

        foreach (var (code, name, desc) in groups)
        {
            if (existingSet.Contains(code)) continue;
            toAdd.Add(new UserGroup { Code = code, Name = name, Description = desc, IsActive = true });
        }

        if (toAdd.Count > 0)
        {
            db.UserGroups.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] Seeded {toAdd.Count} user groups.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ProjectType ↔ WorkflowDefinition Seed Mappings
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seeds initial ProjectType → WorkflowDefinition mappings.
    /// Looks up JobTypes by title (Hebrew) and assigns <see cref="WorkflowCodes.PlanningWorkflow"/>
    /// as the default workflow.
    /// Idempotent: skips pairs that already exist.
    /// </summary>
    private async ValueTask SeedProjectTypeWorkflowMappingsAsync(CancellationToken ct)
    {
        // Map: ProjectType title contains → PlanningWorkflow as the default.
        var mappingRules = new (string TitleContains, string WorkflowCode, bool IsDefault, int SortOrder)[]
        {
            ("תכנון",      WorkflowCodes.PlanningWorkflow, true, 1),
            ("כבישים",     WorkflowCodes.PlanningWorkflow, true, 1),
            ("ניקוז",      WorkflowCodes.PlanningWorkflow, true, 1),
            ("אדריכלות",   WorkflowCodes.PlanningWorkflow, true, 1),
            ("תיאום",      WorkflowCodes.PlanningWorkflow, true, 1),
            ("בדיקת",      WorkflowCodes.PlanningWorkflow, true, 1),
            ("בדיקה",      WorkflowCodes.PlanningWorkflow, true, 1),
            ("חוות דעת",   WorkflowCodes.PlanningWorkflow, true, 1),
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Load all workflow definitions by code (for ID lookup)
        var definitions = await db.WorkflowDefinitions
            .AsNoTracking()
            .ToDictionaryAsync(d => d.Code, d => d.Id, ct);

        // Load all job types
        var jobTypes = await db.JobTypes
            .AsNoTracking()
            .Where(j => j.Title != null)
            .ToListAsync(ct);

        // Load existing mappings to skip duplicates
        var existingPairs = await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .Select(m => new { m.ProjectTypeId, m.WorkflowDefinitionId })
            .ToListAsync(ct);

        var existingSet = new HashSet<(int, int)>(
            existingPairs.Select(p => (p.ProjectTypeId, p.WorkflowDefinitionId)));

        var toAdd = new List<ProjectTypeWorkflowDefinition>();

        foreach (var rule in mappingRules)
        {
            if (!definitions.TryGetValue(rule.WorkflowCode, out var defId))
                continue;

            // Find matching job types by title substring
            var matchingTypes = jobTypes
                .Where(j => j.Title!.Contains(rule.TitleContains, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var jt in matchingTypes)
            {
                if (existingSet.Contains((jt.Id, defId)))
                    continue;

                toAdd.Add(new ProjectTypeWorkflowDefinition
                {
                    ProjectTypeId = jt.Id,
                    WorkflowDefinitionId = defId,
                    IsDefault = rule.IsDefault,
                    IsEnabled = true,
                    SortOrder = rule.SortOrder,
                });

                existingSet.Add((jt.Id, defId));
            }
        }

        if (toAdd.Count > 0)
        {
            db.ProjectTypeWorkflowDefinitions.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] Seeded {toAdd.Count} ProjectType↔WorkflowDefinition mappings.");
        }

        // Reconcile default flags: for any ProjectType that has a PlanningWorkflow mapping,
        // it must be the IsDefault one; previous defaults are demoted to non-default.
        await ReconcilePlanningWorkflowAsDefaultAsync(db, ct);
    }

    /// <summary>
    /// Ensures that <see cref="WorkflowCodes.PlanningWorkflow"/> is the IsDefault mapping
    /// for every ProjectType that has it, demoting any sibling defaults.
    /// Idempotent.
    /// </summary>
    private static async ValueTask ReconcilePlanningWorkflowAsDefaultAsync(
        SiNetSQLDbContext db, CancellationToken ct)
    {
        var planningDefId = await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.PlanningWorkflow)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);

        if (planningDefId is null) return;

        var projectTypeIdsWithPlanning = await db.ProjectTypeWorkflowDefinitions
            .Where(m => m.WorkflowDefinitionId == planningDefId.Value)
            .Select(m => m.ProjectTypeId)
            .ToListAsync(ct);

        if (projectTypeIdsWithPlanning.Count == 0) return;

        var siblings = await db.ProjectTypeWorkflowDefinitions
            .Where(m => projectTypeIdsWithPlanning.Contains(m.ProjectTypeId))
            .ToListAsync(ct);

        var changed = 0;
        foreach (var m in siblings)
        {
            var shouldBeDefault = m.WorkflowDefinitionId == planningDefId.Value;
            if (m.IsDefault != shouldBeDefault)
            {
                m.IsDefault = shouldBeDefault;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] Reconciled {changed} ProjectType↔WorkflowDefinition default flags to PlanningWorkflow.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PlanningWorkflow (PLN.*)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seeds the clean PlanningWorkflow definition, its <c>PLN.*</c> stages and
    /// transitions (with <see cref="WorkflowTransitionAction"/> rows for status/result
    /// updates). Idempotent — safe to call repeatedly.
    /// </summary>
    private async ValueTask SeedPlanningWorkflowAsync(CancellationToken ct)
    {
        await SeedWorkflowDefinitionAsync(
            PlanningWorkflowSeedData.Code,
            PlanningWorkflowSeedData.Name,
            PlanningWorkflowSeedData.Description,
            PlanningWorkflowSeedData.Stages,
            PlanningWorkflowSeedData.Transitions,
            stageGroupAssignments: PlanningWorkflowSeedData.StageGroupAssignments,
            subWorkflowStageCode: PlanningStageCodes.ExecutionMaterialCheck,
            subWorkflowDefinitionCode: WorkflowCodes.MaterialIntake,
            ct,
            stageTasks: PlanningWorkflowSeedData.StageTasks);
    }

    /// <summary>Seeds the reusable MaterialIntake (MAT.*) subworkflow.</summary>
    private async ValueTask SeedMaterialIntakeWorkflowAsync(CancellationToken ct)
    {
        await SeedWorkflowDefinitionAsync(
            MaterialIntakeWorkflowSeedData.Code,
            MaterialIntakeWorkflowSeedData.Name,
            MaterialIntakeWorkflowSeedData.Description,
            MaterialIntakeWorkflowSeedData.Stages,
            MaterialIntakeWorkflowSeedData.Transitions,
            stageGroupAssignments: MaterialIntakeWorkflowSeedData.StageGroupAssignments,
            subWorkflowStageCode: null,
            subWorkflowDefinitionCode: null,
            ct,
            stageTasks: MaterialIntakeWorkflowSeedData.StageTasks);
    }

    /// <summary>
    /// Seeds the standalone Proposal (PRP.*) workflow definition. Independent of
    /// any project — started email-driven via <c>SuggestedActionType.CreatePriceQuote</c>.
    /// Reuses existing Quote* <see cref="TaskResultCodes"/> and the standard
    /// <c>SetProjectStatus</c> action; introduces no schema changes.
    /// </summary>
    private async ValueTask SeedProposalWorkflowAsync(CancellationToken ct)
    {
        await SeedWorkflowDefinitionAsync(
            ProposalWorkflowSeedData.Code,
            ProposalWorkflowSeedData.Name,
            ProposalWorkflowSeedData.Description,
            ProposalWorkflowSeedData.Stages,
            ProposalWorkflowSeedData.Transitions,
            stageGroupAssignments: ProposalWorkflowSeedData.StageGroupAssignments,
            subWorkflowStageCode: null,
            subWorkflowDefinitionCode: null,
            ct,
            stageTasks: ProposalWorkflowSeedData.StageTasks);
    }

    /// <summary>
    /// Fixes office DBs seeded before MaterialCheck shared the generic
    /// <c>MaterialComplete</c>/<c>MaterialMissing</c> codes with MAT.Check.
    /// Those DBs keep <c>Manual</c> + <c>QuoteMaterial*</c> rules whose unique
    /// key differs from the current seed, so <see cref="SeedWorkflowDefinitionAsync"/>
    /// neither updates nor removes them — and auto-advance never matches Manual.
    /// </summary>
    private async ValueTask ReconcileProposalMaterialCheckTransitionsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var defId = await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.Proposal)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (defId is null) return;

        var materialCheckId = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == defId.Value
                     && s.Code == ProposalStageCodes.MaterialCheck)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (materialCheckId is null) return;

        var rules = await db.WorkflowTransitionRules
            .Where(r => r.WorkflowDefinitionId == defId.Value
                     && r.FromStageId == materialCheckId.Value)
            .ToListAsync(ct);

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TaskResultCodes.QuoteMaterialComplete] = TaskResultCodes.MaterialComplete,
            [TaskResultCodes.QuoteMaterialMissing] = TaskResultCodes.MaterialMissing,
        };

        int updated = 0;
        int removed = 0;

        foreach (var rule in rules.ToList())
        {
            var json = rule.ConditionJson ?? string.Empty;
            string? mappedFrom = null;
            string? desiredCode = null;

            foreach (var (legacy, modern) in aliases)
            {
                if (json.Contains(legacy, StringComparison.Ordinal))
                {
                    mappedFrom = legacy;
                    desiredCode = modern;
                    break;
                }
            }

            if (desiredCode is null
                && rule.ConditionType == WorkflowTransitionConditionType.TaskResultEquals
                && (json.Contains(TaskResultCodes.MaterialComplete, StringComparison.Ordinal)
                    || json.Contains(TaskResultCodes.MaterialMissing, StringComparison.Ordinal)))
            {
                desiredCode = json.Contains(TaskResultCodes.MaterialMissing, StringComparison.Ordinal)
                    ? TaskResultCodes.MaterialMissing
                    : TaskResultCodes.MaterialComplete;
            }

            var needsTriggerUpgrade =
                rule.TriggerType == WorkflowTransitionTriggerType.Manual
                && rule.ConditionType == WorkflowTransitionConditionType.TaskResultEquals
                && desiredCode is not null;

            var needsCodeUpgrade = mappedFrom is not null;

            if (!needsTriggerUpgrade && !needsCodeUpgrade)
                continue;

            var desiredJson = $"{{\"TaskResultCode\":\"{desiredCode}\"}}";
            var desiredHash = WorkflowTransitionRule.ComputeConditionHash(desiredJson);
            const WorkflowTransitionTriggerType desiredTrigger = WorkflowTransitionTriggerType.TaskStatusChanged;
            const WorkflowEvaluationMode desiredMode = WorkflowEvaluationMode.Auto;

            var correctAlreadyExists = rules.Any(r =>
                r.Id != rule.Id
                && r.FromStageId == rule.FromStageId
                && r.ToStageId == rule.ToStageId
                && r.TriggerType == desiredTrigger
                && r.ConditionType == WorkflowTransitionConditionType.TaskResultEquals
                && string.Equals(r.ConditionHash, desiredHash, StringComparison.Ordinal));

            if (correctAlreadyExists)
            {
                db.WorkflowTransitionRules.Remove(rule);
                rules.Remove(rule);
                removed++;
                // #region agent log
                DevToolsLog.Info(
                    $"[WorkflowSeed] Proposal MaterialCheck: removed legacy rule Id={rule.Id} " +
                    $"(trigger={rule.TriggerType} json={rule.ConditionJson}) — canonical TaskStatusChanged rule already present.");
                // #endregion
                continue;
            }

            rule.ConditionJson = desiredJson;
            rule.ConditionHash = desiredHash;
            rule.TriggerType = desiredTrigger;
            rule.EvaluationMode = desiredMode;
            if (mappedFrom is not null && !string.IsNullOrEmpty(rule.Name))
                rule.Name = rule.Name.Replace(mappedFrom, desiredCode!, StringComparison.Ordinal);
            updated++;
            // #region agent log
            DevToolsLog.Info(
                $"[WorkflowSeed] Proposal MaterialCheck: upgraded rule Id={rule.Id} " +
                $"→ Trigger={desiredTrigger} Eval={desiredMode} json={desiredJson}");
            // #endregion
        }

        if (updated == 0 && removed == 0)
            return;

        await db.SaveChangesAsync(ct);
        DevToolsLog.Info(
            $"[WorkflowSeed] Proposal: reconciled MaterialCheck transitions (updated={updated}, removedLegacy={removed}).");
        // #region agent log
        WorkflowDebugTrace.Step(
            "WorkflowSeed.ReconcileMaterialCheck",
            $"updated={updated} removedLegacy={removed} hypothesis=H1-H2");
        // #endregion
    }

    /// <summary>
    /// Diagnostic dump of the Proposal workflow shape as seen by the runtime DB
    /// AFTER <see cref="SeedAllAsync"/> has finished its Proposal seeding. This
    /// is the answer to the question "why do clean-DB tests pass while the real
    /// application reports 'תהליך הופעל, אך לא נוצרה משימה'?" — it logs whether
    /// the actual application DB contains PRP.FileMaterial, its stage-task
    /// template for <see cref="TaskTypeCodes.FileQuoteMaterial"/>, its
    /// <see cref="UserGroupCodes.OfficeManagement"/> assignment, and the
    /// auto-transition from PRP.ProjectSetup. Read-only; no schema/data changes.
    /// </summary>
    public async ValueTask LogProposalRuntimeShapeAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var definition = await db.WorkflowDefinitions
            .AsNoTracking()
            .Include(d => d.Stages).ThenInclude(s => s.AssignedGroup)
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.Proposal, ct);

        if (definition is null)
        {
            DevToolsLog.Warn("[ProposalDiag] WorkflowDefinition 'Proposal' NOT FOUND in runtime DB after seed.");
            return;
        }

        DevToolsLog.Info($"[ProposalDiag] WorkflowDefinition: Id={definition.Id} Code={definition.Code} IsActive={definition.IsActive} Stages={definition.Stages.Count}");

        foreach (var s in definition.Stages.OrderBy(s => s.SortOrder))
        {
            DevToolsLog.Info(
                $"[ProposalDiag]   Stage: Id={s.Id} Code={s.Code} Name='{s.Name}' " +
                $"AssignedGroupId={s.AssignedGroupId?.ToString() ?? "null"} " +
                $"Group='{s.AssignedGroup?.Code ?? "(none)"}' " +
                $"IsInitial={s.IsInitial} IsFinal={s.IsFinal}");
        }

        var fileMaterial = definition.Stages.FirstOrDefault(s => s.Code == ProposalStageCodes.FileMaterial);
        if (fileMaterial is null)
        {
            DevToolsLog.Warn($"[ProposalDiag] CRITICAL: stage '{ProposalStageCodes.FileMaterial}' is MISSING in runtime DB. " +
                            "The seed reconciler should have added it — investigate stage reconciliation in SeedWorkflowDefinitionAsync.");
            return;
        }

        // Stage-task templates on PRP.FileMaterial.
        var templates = await db.WorkflowStageTasks
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Where(t => t.StageDefinitionId == fileMaterial.Id)
            .ToListAsync(ct);

        DevToolsLog.Info($"[ProposalDiag]   PRP.FileMaterial StageTaskTemplates: {templates.Count}");
        foreach (var t in templates)
        {
            DevToolsLog.Info(
                $"[ProposalDiag]     Template: Id={t.Id} TaskTypeId={t.TaskTypeId} " +
                $"TaskType.Code={t.TaskType?.Code ?? "(null)"} IsActive={t.IsActive} " +
                $"DefaultAssigneeId={t.DefaultAssigneeId?.ToString() ?? "null"}");
        }

        if (!templates.Any(t => t.IsActive && t.TaskType?.Code == TaskTypeCodes.FileQuoteMaterial))
        {
            DevToolsLog.Warn(
                $"[ProposalDiag] CRITICAL: no ACTIVE WorkflowStageTask for '{TaskTypeCodes.FileQuoteMaterial}' on PRP.FileMaterial. " +
                "This is the exact condition that causes 'תהליך הופעל, אך לא נוצרה משימה' at runtime " +
                "(orchestrator will fall back to group-based creation and produce a task without TaskTypeId, " +
                "or skip creation entirely if the group cannot resolve an assignee).");
        }

        // Group + active members on PRP.FileMaterial.
        if (fileMaterial.AssignedGroupId is null)
        {
            DevToolsLog.Warn("[ProposalDiag] CRITICAL: PRP.FileMaterial has NO AssignedGroupId in runtime DB.");
        }
        else
        {
            var group = await db.UserGroups
                .AsNoTracking()
                .Include(g => g.Memberships).ThenInclude(m => m.Siuser)
                .FirstOrDefaultAsync(g => g.Id == fileMaterial.AssignedGroupId.Value, ct);

            if (group is null)
            {
                DevToolsLog.Warn($"[ProposalDiag] CRITICAL: AssignedGroupId={fileMaterial.AssignedGroupId} on PRP.FileMaterial points to a missing UserGroup.");
            }
            else
            {
                var activeMembers = group.Memberships.Count(m => m.Siuser is { IsActive: true });
                DevToolsLog.Info(
                    $"[ProposalDiag]   PRP.FileMaterial Group: Id={group.Id} Code={group.Code} Name='{group.Name}' " +
                    $"DefaultAssigneeId={group.DefaultAssigneeId?.ToString() ?? "null"} ActiveMembers={activeMembers}");

                if (activeMembers == 0)
                {
                    DevToolsLog.Warn($"[ProposalDiag] CRITICAL: group '{group.Code}' has 0 active members. " +
                                    "Task creation on PRP.FileMaterial will throw because no assignee can be resolved.");
                }
                else if (activeMembers > 1 && group.DefaultAssigneeId is null)
                {
                    DevToolsLog.Warn($"[ProposalDiag] WARNING: group '{group.Code}' has {activeMembers} active members and no DefaultAssigneeId. " +
                                    "Task creation on PRP.FileMaterial will throw at runtime.");
                }
            }
        }

        // Transitions out of PRP.ProjectSetup.
        var projectSetup = definition.Stages.FirstOrDefault(s => s.Code == ProposalStageCodes.ProjectSetup);
        if (projectSetup is not null)
        {
            var transitions = await db.WorkflowTransitionRules
                .AsNoTracking()
                .Where(r => r.WorkflowDefinitionId == definition.Id && r.FromStageId == projectSetup.Id)
                .ToListAsync(ct);

            var stageById = definition.Stages.ToDictionary(s => s.Id, s => s.Code);
            DevToolsLog.Info($"[ProposalDiag]   Transitions from PRP.ProjectSetup: {transitions.Count}");
            foreach (var r in transitions)
            {
                stageById.TryGetValue(r.ToStageId, out var toCode);
                DevToolsLog.Info(
                    $"[ProposalDiag]     Transition: Id={r.Id} To='{toCode ?? r.ToStageId.ToString()}' " +
                    $"Trigger={r.TriggerType} Condition={r.ConditionType} Mode={r.EvaluationMode} " +
                    $"ConditionJson={r.ConditionJson ?? "(null)"}");
            }

            var hasProjectOpenedToFileMaterial = transitions.Any(r =>
                stageById.TryGetValue(r.ToStageId, out var to)
                && to == ProposalStageCodes.FileMaterial
                && r.ConditionJson is not null
                && r.ConditionJson.Contains(TaskResultCodes.ProjectOpened, StringComparison.Ordinal));

            if (!hasProjectOpenedToFileMaterial)
            {
                DevToolsLog.Warn(
                    "[ProposalDiag] CRITICAL: no transition PRP.ProjectSetup → PRP.FileMaterial on " +
                    $"TaskResult='{TaskResultCodes.ProjectOpened}' in runtime DB. Auto-advance after " +
                    "OpenQuoteProject will not fire.");
            }
        }

        // TaskType row for FileQuoteMaterial.
        var fileTaskType = await db.TaskTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == TaskTypeCodes.FileQuoteMaterial, ct);
        if (fileTaskType is null)
        {
            DevToolsLog.Warn($"[ProposalDiag] CRITICAL: TaskType '{TaskTypeCodes.FileQuoteMaterial}' MISSING — created tasks cannot carry a TaskTypeId.");
        }
        else
        {
            DevToolsLog.Info($"[ProposalDiag]   TaskType: Id={fileTaskType.Id} Code={fileTaskType.Code} Name='{fileTaskType.Name}' IsActive={fileTaskType.IsActive}");
        }
    }

    /// <summary>
    /// Workflow-agnostic runtime shape diagnostic. Iterates EVERY active
    /// <see cref="WorkflowDefinition"/> in the runtime DB and logs a
    /// <c>[WorkflowDiag]</c> report covering stages, group assignments,
    /// stage-task templates, task types, registry mappings, and transitions.
    /// <para>
    /// Designed to be read alongside <see cref="LogProposalRuntimeShapeAsync"/>:
    /// this surfaces problems across <em>all</em> active workflows (Planning,
    /// MaterialIntake, Review, Proposal, Opinion, …) so we stop chasing one
    /// workflow at a time through the UI.
    /// </para>
    /// </summary>
    public async ValueTask LogAllWorkflowsRuntimeShapeAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var definitions = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .Include(d => d.Stages).ThenInclude(s => s.AssignedGroup)
            .OrderBy(d => d.Code)
            .ToListAsync(ct);

        DevToolsLog.Info($"[WorkflowDiag] === Health report: {definitions.Count} active WorkflowDefinition(s) ===");

        if (definitions.Count == 0)
        {
            DevToolsLog.Warn("[WorkflowDiag] CRITICAL: no active WorkflowDefinitions in runtime DB.");
            return;
        }

        // Pre-load lookups once.
        var allTaskTypes = await db.TaskTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var allGroups = await db.UserGroups.AsNoTracking()
            .Include(g => g.Memberships).ThenInclude(m => m.Siuser)
            .ToDictionaryAsync(g => g.Id, ct);

        var allTaskResults = new HashSet<string>(
            await db.TaskResultDefinitions.AsNoTracking().Select(r => r.Code).ToListAsync(ct),
            StringComparer.Ordinal);

        foreach (var def in definitions)
        {
            DevToolsLog.Info(
                $"[WorkflowDiag] Workflow: Code={def.Code} Name='{def.Name}' Id={def.Id} " +
                $"Stages={def.Stages.Count}");

            var hasInitial = def.Stages.Any(s => s.IsInitial);
            var hasFinal = def.Stages.Any(s => s.IsFinal);
            if (!hasInitial) DevToolsLog.Warn($"[WorkflowDiag] {def.Code}: no initial stage.");
            if (!hasFinal) DevToolsLog.Warn($"[WorkflowDiag] {def.Code}: no final stage.");

            var stageById = def.Stages.ToDictionary(s => s.Id);

            var templates = await db.WorkflowStageTasks
                .AsNoTracking()
                .Where(t => t.StageDefinition.WorkflowDefinitionId == def.Id)
                .ToListAsync(ct);
            var templatesByStage = templates.GroupBy(t => t.StageDefinitionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var transitions = await db.WorkflowTransitionRules
                .AsNoTracking()
                .Where(r => r.WorkflowDefinitionId == def.Id)
                .ToListAsync(ct);
            var transitionsFrom = transitions.GroupBy(r => r.FromStageId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var s in def.Stages.OrderBy(s => s.SortOrder))
            {
                var groupCode = s.AssignedGroup?.Code ?? "(none)";
                DevToolsLog.Info(
                    $"[WorkflowDiag]   Stage: {s.Code} Name='{s.Name}' " +
                    $"IsInitial={s.IsInitial} IsFinal={s.IsFinal} " +
                    $"Group='{groupCode}'");

                if (s.IsFinal) continue;

                // --- Stage task templates ---
                if (!templatesByStage.TryGetValue(s.Id, out var stageTemplates) || stageTemplates.Count == 0)
                {
                    DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: WARNING no WorkflowStageTask templates — 0 tasks will be created here.");
                }
                else
                {
                    foreach (var t in stageTemplates.Where(t => t.IsActive))
                    {
                        allTaskTypes.TryGetValue(t.TaskTypeId, out var tt);
                        var ttCode = tt?.Code ?? "(missing)";
                        var ttActive = tt?.IsActive ?? false;
                        DevToolsLog.Info(
                            $"[WorkflowDiag]     Template: TaskType={ttCode} Active={ttActive} " +
                            $"DefaultAssigneeId={t.DefaultAssigneeId?.ToString() ?? "null"}");

                        if (tt is null)
                            DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: TaskType id={t.TaskTypeId} MISSING in TaskTypes.");
                        else if (!tt.IsActive)
                            DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: TaskType '{tt.Code}' is INACTIVE.");

                        // Registry mapping check.
                        if (tt is not null && ReviewTaskInteractionRegistry.TryGet(tt.Code) is null)
                            DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: TaskType '{tt.Code}' has NO ReviewTaskInteractionRegistry mapping (UI cannot open the task).");
                    }
                }

                // --- Assignee resolvability ---
                if (s.AssignedGroupId is null)
                {
                    DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: no AssignedGroupId.");
                }
                else if (!allGroups.TryGetValue(s.AssignedGroupId.Value, out var g) || g is null)
                {
                    DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: AssignedGroupId={s.AssignedGroupId} points to a missing UserGroup.");
                }
                else
                {
                    var activeMembers = g.Memberships.Count(m => m.Siuser is { IsActive: true });
                    if (activeMembers == 0)
                        DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: group '{g.Code}' has 0 active members — task creation will throw.");
                    else if (activeMembers > 1 && g.DefaultAssigneeId is null)
                        DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: group '{g.Code}' has {activeMembers} members and no DefaultAssigneeId.");
                }

                // --- Outgoing transitions ---
                if (!transitionsFrom.TryGetValue(s.Id, out var outs) || outs.Count == 0)
                {
                    DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: non-final stage with NO outgoing transitions.");
                }
                else
                {
                    foreach (var r in outs)
                    {
                        stageById.TryGetValue(r.ToStageId, out var toStage);
                        var toCode = toStage?.Code ?? $"(missing stage id={r.ToStageId})";
                        DevToolsLog.Info(
                            $"[WorkflowDiag]     Transition → {toCode} Trigger={r.TriggerType} " +
                            $"Cond={r.ConditionType} Mode={r.EvaluationMode}");

                        if (toStage is null)
                            DevToolsLog.Warn($"[WorkflowDiag]   {def.Code}.{s.Code}: transition points at MISSING ToStageId={r.ToStageId}.");

                        // Heuristic: when ConditionJson references a TaskResult code,
                        // make sure that code actually exists in TaskResults.
                        if (!string.IsNullOrEmpty(r.ConditionJson))
                        {
                            foreach (var code in allTaskResults)
                            {
                                if (r.ConditionJson.Contains(code, StringComparison.Ordinal))
                                {
                                    // present — fine
                                }
                            }
                            // Detect TaskResultEquals condition with a code not in TaskResults.
                            // (Best-effort string scan — no schema knowledge required.)
                            if (r.ConditionJson.Contains("TaskResult", StringComparison.OrdinalIgnoreCase)
                                && !allTaskResults.Any(c => r.ConditionJson.Contains(c, StringComparison.Ordinal)))
                            {
                                DevToolsLog.Warn(
                                    $"[WorkflowDiag]   {def.Code}.{s.Code}: transition ConditionJson references a TaskResult " +
                                    $"that is not present in TaskResults table. ConditionJson={r.ConditionJson}");
                            }
                        }
                    }
                }
            }
        }

        DevToolsLog.Info("[WorkflowDiag] === End health report ===");
    }

    /// <summary>
    /// Seeds the standalone Opinion workflow (OPN.*). Project-independent —
    /// started email-driven via <c>SuggestedActionType.CreateOpinionProject</c>.
    /// Reuses existing material <see cref="TaskResultCodes"/> and the new
    /// <c>Opinion*</c> results; introduces no schema changes.
    /// </summary>
    private async ValueTask SeedOpinionWorkflowAsync(CancellationToken ct)
    {
        await SeedWorkflowDefinitionAsync(
            OpinionWorkflowSeedData.Code,
            OpinionWorkflowSeedData.Name,
            OpinionWorkflowSeedData.Description,
            OpinionWorkflowSeedData.Stages,
            OpinionWorkflowSeedData.Transitions,
            stageGroupAssignments: OpinionWorkflowSeedData.StageGroupAssignments,
            subWorkflowStageCode: null,
            subWorkflowDefinitionCode: null,
            ct,
            stageTasks: OpinionWorkflowSeedData.StageTasks);
    }

    /// <summary>Seeds the Review (REV.*) workflow including subworkflow link to MaterialIntake.</summary>
    private async ValueTask SeedReviewWorkflowAsync(CancellationToken ct)
    {
        await SeedWorkflowDefinitionAsync(
            ReviewWorkflowSeedData.Code,
            ReviewWorkflowSeedData.Name,
            ReviewWorkflowSeedData.Description,
            ReviewWorkflowSeedData.Stages,
            ReviewWorkflowSeedData.Transitions,
            stageGroupAssignments: ReviewWorkflowSeedData.StageGroupAssignments,
            subWorkflowStageCode: ReviewWorkflowSeedData.SubWorkflowStageCode,
            subWorkflowDefinitionCode: WorkflowCodes.MaterialIntake,
            stageTasks: ReviewWorkflowSeedData.StageTasks,
            ct: ct);

        await RemoveLegacyReviewCloseLoopAsync(ct);
    }

    /// <summary>
    /// Narrowly removes the legacy unconditional <c>REV.Close → REV.Close</c> self-loop
    /// (Manual + Always, with the historical
    /// <c>RecordTaskResult(ReviewProjectClosed)</c> / <c>CloseProject</c> action pair)
    /// from databases seeded before the close decision was moved behind the
    /// generic <c>ProjectCloseApproved</c> task result.
    /// <para>
    /// The match is intentionally restrictive: it only deletes a rule that has
    /// <see cref="WorkflowTransitionTriggerType.Manual"/> trigger,
    /// <see cref="WorkflowTransitionConditionType.Always"/> condition, no
    /// <see cref="WorkflowTransitionRule.ConditionJson"/>, and whose actions are
    /// strictly the legacy pair. The new <c>ProjectCloseApproved</c> rule and the
    /// no-action rejected/needs-more-info rules use <c>TaskResultEquals</c> and
    /// non-null <c>ConditionJson</c>, so they are never matched.
    /// </para>
    /// </summary>
    private async ValueTask RemoveLegacyReviewCloseLoopAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var reviewDefId = await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.Review)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (reviewDefId is null) return;

        var closeStageId = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == reviewDefId.Value
                     && s.Code == ReviewStageCodes.Close)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (closeStageId is null) return;

        var candidates = await db.WorkflowTransitionRules
            .Include(r => r.Actions)
            .Where(r => r.WorkflowDefinitionId == reviewDefId.Value
                     && r.FromStageId == closeStageId.Value
                     && r.ToStageId == closeStageId.Value
                     && r.TriggerType == WorkflowTransitionTriggerType.Manual
                     && r.ConditionType == WorkflowTransitionConditionType.Always
                     && r.ConditionJson == null)
            .ToListAsync(ct);

        var legacy = candidates
            .Where(IsLegacyReviewCloseLoop)
            .ToList();

        if (legacy.Count == 0) return;

        db.WorkflowTransitionRules.RemoveRange(legacy);
        await db.SaveChangesAsync(ct);
        DevToolsLog.Info($"[WorkflowSeed] {WorkflowCodes.Review}: removed {legacy.Count} legacy unconditional REV.Close self-loop(s).");
    }

    private static bool IsLegacyReviewCloseLoop(WorkflowTransitionRule rule)
    {
        if (rule.Actions.Count != 2) return false;

        var hasLegacyRecord = rule.Actions.Any(a =>
            a.ActionType == WorkflowTransitionActionType.RecordTaskResult
            && a.ConfigJson != null
            && a.ConfigJson.Contains(TaskResultCodes.ReviewProjectClosed, StringComparison.Ordinal));

        var hasCloseProject = rule.Actions.Any(a =>
            a.ActionType == WorkflowTransitionActionType.CloseProject);

        return hasLegacyRecord && hasCloseProject;
    }

    /// <summary>
    /// Generic, idempotent seeder for a <see cref="WorkflowDefinition"/> + its
    /// <see cref="WorkflowStageDefinition"/>s and <see cref="WorkflowTransitionRule"/>s.
    /// Optionally wires per-stage <see cref="UserGroup"/> assignment and a
    /// SubWorkflow stage link (NodeType=SubWorkflow + SubWorkflowDefinitionId).
    /// </summary>
    private async ValueTask SeedWorkflowDefinitionAsync(
        string code,
        string name,
        string description,
        PlanningWorkflowSeedData.StageDefinition[] stageDefs,
        PlanningWorkflowSeedData.StageTransitionDefinition[] transitionDefs,
        IReadOnlyDictionary<string, string>? stageGroupAssignments,
        string? subWorkflowStageCode,
        string? subWorkflowDefinitionCode,
        CancellationToken ct,
        PlanningWorkflowSeedData.StageTaskDefinition[]? stageTasks = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // ── 1. Definition ────────────────────────────────────────────────
        var definition = await db.WorkflowDefinitions
            .Include(d => d.Stages)
            .FirstOrDefaultAsync(d => d.Code == code, ct);

        if (definition is null)
        {
            definition = new WorkflowDefinition
            {
                Code = code,
                Name = name,
                Description = description,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] Created workflow definition: {code}.");
        }

        // ── 2. Stages (add missing / reconcile basic fields) ─────────────
        var stagesByCode = definition.Stages.ToDictionary(s => s.Code, StringComparer.Ordinal);
        bool stagesChanged = false;

        foreach (var stageDef in stageDefs)
        {
            if (stagesByCode.TryGetValue(stageDef.Code, out var existing))
            {
                if (existing.Name != stageDef.Name) { existing.Name = stageDef.Name; stagesChanged = true; }
                if (existing.SortOrder != stageDef.SortOrder) { existing.SortOrder = stageDef.SortOrder; stagesChanged = true; }
                if (existing.IsInitial != stageDef.IsInitial) { existing.IsInitial = stageDef.IsInitial; stagesChanged = true; }
                if (existing.IsFinal != stageDef.IsFinal) { existing.IsFinal = stageDef.IsFinal; stagesChanged = true; }

                // NodeType: apply when seed specifies; never downgrade SubWorkflow → Stage/etc.
                if (stageDef.NodeType is { Length: > 0 } desiredNodeType
                    && !string.Equals(existing.NodeType, desiredNodeType, StringComparison.Ordinal)
                    && (!string.Equals(existing.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(desiredNodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase)))
                {
                    existing.NodeType = desiredNodeType;
                    stagesChanged = true;
                }

                // Canvas: seed only fills empty layout so manual/designer positions survive re-seed.
                var existingBlank =
                    Math.Abs(existing.CanvasX) < 0.1 && Math.Abs(existing.CanvasY) < 0.1;
                if (existingBlank && stageDef.CanvasX is double cx && existing.CanvasX != cx)
                {
                    existing.CanvasX = cx;
                    stagesChanged = true;
                }

                if (existingBlank && stageDef.CanvasY is double cy && existing.CanvasY != cy)
                {
                    existing.CanvasY = cy;
                    stagesChanged = true;
                }
            }
            else
            {
                var stage = new WorkflowStageDefinition
                {
                    WorkflowDefinitionId = definition.Id,
                    Code = stageDef.Code,
                    Name = stageDef.Name,
                    SortOrder = stageDef.SortOrder,
                    IsInitial = stageDef.IsInitial,
                    IsFinal = stageDef.IsFinal,
                    NodeType = stageDef.NodeType ?? "Stage",
                    CanvasX = stageDef.CanvasX ?? 0,
                    CanvasY = stageDef.CanvasY ?? 0,
                };
                db.WorkflowStageDefinitions.Add(stage);
                stagesByCode[stageDef.Code] = stage;
                stagesChanged = true;
            }
        }

        if (stagesChanged)
        {
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] {code} stages reconciled.");
        }

        // ── 2b. Stage → UserGroup assignment ─────────────────────────────
        if (stageGroupAssignments is { Count: > 0 })
        {
            var groupCodeToId = await db.UserGroups
                .AsNoTracking()
                .ToDictionaryAsync(g => g.Code, g => g.Id, StringComparer.OrdinalIgnoreCase, ct);

            int groupChanges = 0;
            foreach (var stage in stagesByCode.Values)
            {
                if (!stageGroupAssignments.TryGetValue(stage.Code, out var groupCode))
                    continue;
                if (!groupCodeToId.TryGetValue(groupCode, out var groupId))
                {
                    DevToolsLog.Warn($"[WorkflowSeed] {code}: group not found '{groupCode}' for stage {stage.Code}.");
                    continue;
                }
                if (stage.AssignedGroupId != groupId)
                {
                    stage.AssignedGroupId = groupId;
                    groupChanges++;
                }
            }

            if (groupChanges > 0)
            {
                await db.SaveChangesAsync(ct);
                DevToolsLog.Info($"[WorkflowSeed] {code}: assigned groups to {groupChanges} stages.");
            }
        }

        // ── 2c. Sub-workflow link ────────────────────────────────────────
        if (subWorkflowStageCode is not null && subWorkflowDefinitionCode is not null
            && stagesByCode.TryGetValue(subWorkflowStageCode, out var subStage))
        {
            var subDefId = await db.WorkflowDefinitions
                .Where(d => d.Code == subWorkflowDefinitionCode)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(ct);

            if (subDefId is null)
            {
                DevToolsLog.Warn($"[WorkflowSeed] {code}: sub-workflow '{subWorkflowDefinitionCode}' not found; skipping link.");
            }
            else
            {
                bool changed = false;
                if (subStage.NodeType != "SubWorkflow") { subStage.NodeType = "SubWorkflow"; changed = true; }
                if (subStage.SubWorkflowDefinitionId != subDefId.Value) { subStage.SubWorkflowDefinitionId = subDefId.Value; changed = true; }
                if (changed)
                {
                    await db.SaveChangesAsync(ct);
                    DevToolsLog.Info($"[WorkflowSeed] {code}: linked stage {subStage.Code} → sub-workflow {subWorkflowDefinitionCode}.");
                }
            }
        }

        // ── 2d. WorkflowStageTask templates ──────────────────────────────
        if (stageTasks is { Length: > 0 })
        {
            await SeedStageTaskTemplatesAsync(db, code, stagesByCode, stageTasks, ct);
        }

        // ── 3. Transitions ───────────────────────────────────────────────
        var stagesById = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .ToDictionaryAsync(s => s.Code, StringComparer.Ordinal, ct);

        var existingRules = await db.WorkflowTransitionRules
            .Include(r => r.Actions)
            .Where(r => r.WorkflowDefinitionId == definition.Id)
            .ToListAsync(ct);

        // The DB unique index is now
        // (WorkflowDefinitionId, FromStageId, ToStageId, TriggerType, ConditionType, ConditionHash)
        // — index name: IX_WorkflowTransitionRule_Unique. This allows multiple
        // legitimate transitions between the same (From, To) pair when they differ
        // by trigger / condition type / condition payload (e.g. REV.Close→REV.Close
        // for ProjectCloseApproved vs ProjectCloseRejected vs ProjectCloseNeedsMoreInfo,
        // or REV.PoliceApproved→REV.Close with Manual vs ActionCompleted triggers).
        var rulesByKey = existingRules
            .GroupBy(r => (r.FromStageId, r.ToStageId, r.TriggerType, r.ConditionType, r.ConditionHash))
            .ToDictionary(g => g.Key, g => g.First());

        int rulesAdded = 0;
        int rulesUpdated = 0;
        var seededKeys = new HashSet<(int, int, WorkflowTransitionTriggerType, WorkflowTransitionConditionType, string)>();

        foreach (var t in transitionDefs)
        {
            if (!stagesById.TryGetValue(t.FromStageCode, out var from) ||
                !stagesById.TryGetValue(t.ToStageCode, out var to))
            {
                DevToolsLog.Warn($"[WorkflowSeed] {code} transition skipped — stage not found: {t.FromStageCode} → {t.ToStageCode}");
                continue;
            }

            var conditionJson = t.ConditionJson
                ?? (t.TaskResultCode is null
                    ? null
                    : $"{{\"TaskResultCode\":\"{t.TaskResultCode}\"}}");

            var triggerType = t.TriggerType ?? WorkflowTransitionTriggerType.Manual;
            var conditionType = t.ConditionType ?? (t.TaskResultCode is null
                ? WorkflowTransitionConditionType.Always
                : WorkflowTransitionConditionType.TaskResultEquals);
            var evaluationMode = t.EvaluationMode ?? WorkflowEvaluationMode.Manual;
            var priority = t.Priority ?? 0;
            var ruleName = t.Name ?? (t.TaskResultCode is null
                ? $"{from.Name} → {to.Name}"
                : $"{from.Name} → {to.Name} ({t.TaskResultCode})");

            var conditionHash = WorkflowTransitionRule.ComputeConditionHash(conditionJson);
            var key = (from.Id, to.Id, triggerType, conditionType, conditionHash);

            // Guard: a true duplicate in seed data (same full key). Distinct entries
            // for the same (From, To) with different trigger/condition/hash are now
            // legitimate and must NOT be skipped.
            if (!seededKeys.Add(key))
            {
                DevToolsLog.Warn(
                    $"[WorkflowSeed] {code}: duplicate seed transition for " +
                    $"({from.Code} → {to.Code}, trigger={triggerType}, condition={conditionType}, hash={conditionHash[..8]}); skipping.");
                continue;
            }

            if (rulesByKey.TryGetValue(key, out var existing))
            {
                // Update existing rule in place so seed stays idempotent.
                bool changed = false;
                if (!string.Equals(existing.ConditionJson ?? string.Empty, conditionJson ?? string.Empty, StringComparison.Ordinal))
                { existing.ConditionJson = conditionJson; changed = true; }
                if (!string.Equals(existing.ConditionHash, conditionHash, StringComparison.Ordinal))
                { existing.ConditionHash = conditionHash; changed = true; }
                if (existing.EvaluationMode != evaluationMode) { existing.EvaluationMode = evaluationMode; changed = true; }
                if (existing.Priority != priority) { existing.Priority = priority; changed = true; }
                if (!string.Equals(existing.Name, ruleName, StringComparison.Ordinal)) { existing.Name = ruleName; changed = true; }

                // Replace actions if the set differs (by ActionType + ActionCode + ConfigJson + SortOrder).
                var desired = t.Actions
                    .Select((a, idx) =>
                    {
                        var type = MapSeedAction(a.ActionType);
                        return (Type: type,
                                Code: WorkflowTransitionActionCodeMapper.MapFromWorkflowTransitionActionType(type),
                                Config: BuildActionConfigJson(a),
                                Order: idx);
                    })
                    .ToList();

                var current = existing.Actions
                    .OrderBy(a => a.SortOrder)
                    .Select(a => (Type: a.ActionType, Code: a.ActionCode ?? string.Empty, Config: a.ConfigJson, Order: a.SortOrder))
                    .ToList();

                // Normalize desired's Code for comparison (non-null string).
                var desiredForCompare = desired
                    .Select(d => (d.Type, Code: d.Code ?? string.Empty, d.Config, d.Order))
                    .ToList();

                bool actionsDiffer = current.Count != desiredForCompare.Count
                    || !current.SequenceEqual(desiredForCompare);

                if (actionsDiffer)
                {
                    db.WorkflowTransitionActions.RemoveRange(existing.Actions);
                    existing.Actions.Clear();
                    int actionOrder = 0;
                    foreach (var a in t.Actions)
                    {
                        var type = MapSeedAction(a.ActionType);
                        existing.Actions.Add(new WorkflowTransitionAction
                        {
                            ActionType = type,
                            ActionCode = WorkflowTransitionActionCodeMapper.MapFromWorkflowTransitionActionType(type),
                            ConfigJson = BuildActionConfigJson(a),
                            SortOrder = actionOrder++,
                        });
                    }
                    changed = true;
                }

                if (changed) rulesUpdated++;
                continue;
            }

            var rule = new WorkflowTransitionRule
            {
                WorkflowDefinitionId = definition.Id,
                FromStageId = from.Id,
                ToStageId = to.Id,
                Name = ruleName,
                TriggerType = triggerType,
                ConditionType = conditionType,
                ConditionJson = conditionJson,
                ConditionHash = conditionHash,
                EvaluationMode = evaluationMode,
                Priority = priority,
            };

            int newActionOrder = 0;
            foreach (var a in t.Actions)
            {
                var type = MapSeedAction(a.ActionType);
                rule.Actions.Add(new WorkflowTransitionAction
                {
                    ActionType = type,
                    ActionCode = WorkflowTransitionActionCodeMapper.MapFromWorkflowTransitionActionType(type),
                    ConfigJson = BuildActionConfigJson(a),
                    SortOrder = newActionOrder++,
                });
            }

            db.WorkflowTransitionRules.Add(rule);
            rulesByKey[key] = rule;
            rulesAdded++;
        }

        if (rulesAdded > 0 || rulesUpdated > 0)
        {
            await db.SaveChangesAsync(ct);
            if (rulesAdded > 0)
                DevToolsLog.Info($"[WorkflowSeed] {code}: added {rulesAdded} transition rules.");
            if (rulesUpdated > 0)
                DevToolsLog.Info($"[WorkflowSeed] {code}: updated {rulesUpdated} existing transition rules.");
        }
    }

    /// <summary>
    /// Adds missing <see cref="WorkflowStageTask"/> templates declared by a workflow seed.
    /// Idempotent: a template is identified by (StageDefinitionId, TaskTypeId).
    /// Templates without a known TaskType or UserGroup are skipped with a warning so
    /// existing fallback behaviour for stages without templates is preserved.
    /// </summary>
    private static async ValueTask SeedStageTaskTemplatesAsync(
        SiNetSQLDbContext db,
        string workflowCode,
        IReadOnlyDictionary<string, WorkflowStageDefinition> stagesByCode,
        PlanningWorkflowSeedData.StageTaskDefinition[] stageTasks,
        CancellationToken ct)
    {
        var taskTypeMap = await db.TaskTypes
            .AsNoTracking()
            .Where(t => t.Code != null)
            .ToDictionaryAsync(t => t.Code!, t => t.Id, StringComparer.OrdinalIgnoreCase, ct);

        var groupMap = await db.UserGroups
            .AsNoTracking()
            .ToDictionaryAsync(g => g.Code, g => g.Id, StringComparer.OrdinalIgnoreCase, ct);

        var stageIds = stagesByCode.Values.Select(s => s.Id).ToList();
        var existing = await db.WorkflowStageTasks
            .AsNoTracking()
            .Where(t => stageIds.Contains(t.StageDefinitionId))
            .Select(t => new { t.StageDefinitionId, t.TaskTypeId })
            .ToListAsync(ct);
        var existingSet = new HashSet<(int, int)>(existing.Select(e => (e.StageDefinitionId, e.TaskTypeId)));

        var toAdd = new List<WorkflowStageTask>();

        foreach (var def in stageTasks)
        {
            if (!stagesByCode.TryGetValue(def.StageCode, out var stage))
            {
                DevToolsLog.Warn($"[WorkflowSeed] {workflowCode}: stage-task skipped — stage '{def.StageCode}' not found.");
                continue;
            }
            if (!taskTypeMap.TryGetValue(def.TaskTypeCode, out var taskTypeId))
            {
                DevToolsLog.Warn($"[WorkflowSeed] {workflowCode}: stage-task skipped — TaskType '{def.TaskTypeCode}' not found.");
                continue;
            }
            if (!groupMap.TryGetValue(def.AssignedGroupCode, out var groupId))
            {
                DevToolsLog.Warn($"[WorkflowSeed] {workflowCode}: stage-task skipped — UserGroup '{def.AssignedGroupCode}' not found.");
                continue;
            }
            if (!existingSet.Add((stage.Id, taskTypeId)))
                continue;

            // The stage's AssignedGroupId already encodes the responsible group for
            // group-based fallback; the template here is what the orchestrator reads.
            // We keep both aligned via StageGroupAssignments in seed data.
            _ = groupId; // reserved for future template-level group override.

            toAdd.Add(new WorkflowStageTask
            {
                StageDefinitionId = stage.Id,
                TaskTypeId = taskTypeId,
                DefaultAssigneeId = null,
                SortOrder = def.SortOrder,
                IsRequired = def.IsRequired,
                Notes = def.Notes,
                IsActive = true,
            });
        }

        if (toAdd.Count > 0)
        {
            db.WorkflowStageTasks.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] {workflowCode}: seeded {toAdd.Count} stage-task templates.");
        }
    }

    private static WorkflowTransitionActionType MapSeedAction(PlanningWorkflowSeedData.SeedActionType seed) => seed switch
    {
        PlanningWorkflowSeedData.SeedActionType.SetProjectStatus => WorkflowTransitionActionType.SetProjectStatus,
        PlanningWorkflowSeedData.SeedActionType.RecordTaskResult => WorkflowTransitionActionType.RecordTaskResult,
        PlanningWorkflowSeedData.SeedActionType.SetBillingPending => WorkflowTransitionActionType.SetBillingPending,
        PlanningWorkflowSeedData.SeedActionType.CloseProject => WorkflowTransitionActionType.CloseProject,
        PlanningWorkflowSeedData.SeedActionType.StartSubWorkflow => WorkflowTransitionActionType.StartSubWorkflow,
        _ => throw new ArgumentOutOfRangeException(nameof(seed), seed, "Unknown seed action type"),
    };

    private static string? BuildActionConfigJson(PlanningWorkflowSeedData.StageActionDefinition action) => action.ActionType switch
    {
        PlanningWorkflowSeedData.SeedActionType.SetProjectStatus when action.Payload is not null
            => $"{{\"ProjectStatusCode\":\"{action.Payload}\"}}",
        PlanningWorkflowSeedData.SeedActionType.RecordTaskResult when action.Payload is not null
            => $"{{\"TaskResultCode\":\"{action.Payload}\"}}",
        _ => null,
    };

    // ═══════════════════════════════════════════════════════════════════════
    // ProjectType ↔ PlanningWorkflow stage / discipline activation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seeds <see cref="ProjectTypeWorkflowStage"/> rows: per-<c>JobType</c>
    /// activation of <c>PLN.*</c> stages. Idempotent: only adds missing
    /// (ProjectTypeId, WorkflowStageDefinitionId) pairs.
    /// </summary>
    private async ValueTask SeedProjectTypeWorkflowStagesAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var planning = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.PlanningWorkflow, ct);
        if (planning is null)
        {
            DevToolsLog.Warn("[WorkflowSeed] PlanningWorkflow not found — skipping ProjectTypeWorkflowStage seed.");
            return;
        }

        var planningStageMap = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .Where(s => s.WorkflowDefinitionId == planning.Id)
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.OrdinalIgnoreCase, ct);

        var jobTypes = await db.JobTypes
            .AsNoTracking()
            .Where(j => j.Title != null)
            .ToListAsync(ct);

        var existingPairs = await db.ProjectTypeWorkflowStages
            .AsNoTracking()
            .Select(m => new { m.ProjectTypeId, m.WorkflowStageDefinitionId })
            .ToListAsync(ct);
        var existingSet = new HashSet<(int, int)>(
            existingPairs.Select(p => (p.ProjectTypeId, p.WorkflowStageDefinitionId)));

        var toAdd = new List<ProjectTypeWorkflowStage>();

        foreach (var jt in jobTypes)
        {
            var profile = ResolveStageProfile(jt.Title!);

            foreach (var stage in profile)
            {
                if (!planningStageMap.TryGetValue(stage.StageCode, out var stageId))
                    continue;
                if (!existingSet.Add(((int)jt.Id, (int)stageId)))
                    continue;

                toAdd.Add(new ProjectTypeWorkflowStage
                {
                    ProjectTypeId = jt.Id,
                    WorkflowStageDefinitionId = stageId,
                    IsRequired = stage.IsRequired,
                    CanRepeat = stage.CanRepeat,
                    SortOrder = stage.SortOrder,
                    IsActive = true,
                });
            }
        }

        if (toAdd.Count > 0)
        {
            db.ProjectTypeWorkflowStages.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] Seeded {toAdd.Count} ProjectType↔PLN stage activations.");
        }
    }

    /// <summary>
    /// Seeds <see cref="ProjectTypeDiscipline"/> rows: per-<c>JobType</c>
    /// activation of discipline TaskTypes. Idempotent: only adds missing
    /// (ProjectTypeId, DisciplineTaskTypeId) pairs.
    /// </summary>
    private async ValueTask SeedProjectTypeDisciplinesAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var taskTypeMap = await db.TaskTypes
            .AsNoTracking()
            .Where(t => t.Code != null)
            .ToDictionaryAsync(t => t.Code!, t => t.Id, StringComparer.OrdinalIgnoreCase, ct);

        var jobTypes = await db.JobTypes
            .AsNoTracking()
            .Where(j => j.Title != null)
            .ToListAsync(ct);

        var existingPairs = await db.ProjectTypeDisciplines
            .AsNoTracking()
            .Select(m => new { m.ProjectTypeId, m.DisciplineTaskTypeId })
            .ToListAsync(ct);
        var existingSet = new HashSet<(int, int)>(
            existingPairs.Select(p => (p.ProjectTypeId, p.DisciplineTaskTypeId)));

        var toAdd = new List<ProjectTypeDiscipline>();

        foreach (var jt in jobTypes)
        {
            var profile = ResolveDisciplineProfile(jt.Title!);

            foreach (var d in profile)
            {
                if (!taskTypeMap.TryGetValue(d.TaskTypeCode, out var taskTypeId))
                {
                    DevToolsLog.Warn($"[WorkflowSeed] Discipline TaskType not found by code: {d.TaskTypeCode} (ProjectType: {jt.Title})");
                    continue;
                }
                if (!existingSet.Add(((int)jt.Id, (int)taskTypeId)))
                    continue;

                toAdd.Add(new ProjectTypeDiscipline
                {
                    ProjectTypeId = jt.Id,
                    DisciplineTaskTypeId = taskTypeId,
                    IsRequired = d.IsRequired,
                    SortOrder = d.SortOrder,
                    IsActive = true,
                });
            }
        }

        if (toAdd.Count > 0)
        {
            db.ProjectTypeDisciplines.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
            DevToolsLog.Info($"[WorkflowSeed] Seeded {toAdd.Count} ProjectType↔Discipline activations.");
        }
    }

    private static ProjectTypeWorkflowStageSeedData.StageActivation[] ResolveStageProfile(string projectTypeTitle)
    {
        foreach (var p in ProjectTypeWorkflowStageSeedData.Profiles)
        {
            if (projectTypeTitle.Contains(p.Match.TitleContains, StringComparison.OrdinalIgnoreCase))
                return p.Stages;
        }
        return ProjectTypeWorkflowStageSeedData.DefaultProfile;
    }

    private static ProjectTypeDisciplineSeedData.DisciplineActivation[] ResolveDisciplineProfile(string projectTypeTitle)
    {
        foreach (var p in ProjectTypeDisciplineSeedData.Profiles)
        {
            if (projectTypeTitle.Contains(p.Match.TitleContains, StringComparison.OrdinalIgnoreCase))
                return p.Disciplines;
        }
        return ProjectTypeDisciplineSeedData.DefaultProfile;
    }
}
