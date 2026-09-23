# Adopt Existing Workflow

> **Status:** Active
> **Updated:** 23.09.2026
> **Host:** `SiNet.App.Wpf`

## Purpose

Take a project whose business process already started before SiNet, place one workflow at the real current stage, and continue with the normal workflow engine from there.

The entry point is **הטמעת תהליך קיים** on the projects dashboard. It calls `IWorkflowAdoptionService` (`PreviewAsync` then `CommitAsync`). It does not start a second workflow engine.

## Historical truth and runtime truth

`WorkflowInstance.CurrentStageId` is the runtime state SiNet continues from.

What happened before SiNet is not replayed. Commit does not call `Advance`. It calls the existing `WorkflowEngine.StartAsync` shared-context overload with `InitialStageCode`.

That writes:

- one `WorkflowInstance` (`Status = Active`, `TriggerType = System`, explicit `JobTypeId`)
- one `WorkflowStageTransition` with `FromStageId = null`
- tasks for the current stage only

It does not write previous transitions, previous tasks, task results, transition actions, or a historical child workflow.

Stages with a lower `SortOrder` appear in Preview as "already happened before SiNet". That list is informational.

## Marker

No schema change. Notes start with `[ADOPTED]` via `WorkflowAdoptionMarkers`, for example:

`[ADOPTED] Existing workflow adopted into SiNet at REV.ProfessionalReview. Source=Manual.`

`TriggerType = System` is not enough on its own, because automatic continuation also uses System. The instance detail screen labels the first transition "התהליך הוטמע ב־SiNet בשלב …" when the marker is present. The transition row itself is unchanged.

## Track identity and duplicates

`JobTypeId` is required. There is no adoption with a null JobType.

For `Project + WorkflowDefinition + JobType`, root instances only:

| Existing | Result |
| --- | --- |
| None, or Cancelled only | Ready to adopt |
| Active at the same stage | `AlreadyExists` on a second commit. No new instance and no new task |
| Active at an earlier stage | `RequiresReviewExistingWorkflow`. No advance chain |
| Active at a later stage | `BlockedBackwardMovement` |
| Paused | `BlockedAlreadyPaused` |
| One Completed | `RequiresReviewCompletedExists` |
| More than one live or completed instance | `BlockedConflict` |

The existing Active/Paused unique index remains the database guard. Adoption adds the Completed and same-stage rules on top of it.

Open policy applies only when no JobType on the project has any `ProjectTypeWorkflowDefinition` row. Once any such row exists, adoption of a track requires an enabled row for that exact JobType and workflow. A mapping on another JobType, or a disabled row, does not allow it.

Opening adoption from the projects dashboard requires `AppFeatureCodes.WorkflowOpsStart`, the same feature as manual workflow start.

## Stage restrictions (v1)

Allowed stages are real runtime stages: not `NodeType = Start`, not `NodeType = SubWorkflow`, not `IsFinal`, and active in `ProjectTypeWorkflowStage` for the JobType.

`REV.MaterialIntake` is blocked. Starting it would open a new Material Intake child from `MAT.Receive`. A later phase can choose:

- intake has not started: start the child normally
- intake is already partial: adopt the child with its own `InitialStageCode`, with the Review parent sitting on `REV.MaterialIntake`

`REV.Completed` is blocked. Start at a final stage would leave an Active instance with no task.

`REV.Intake` is not offered when it is not a seeded stage.

## What does not run

Skipped transition actions are not executed. In Review, adopting at `REV.ProfessionalReview` does not run `OpenReviewProject`, does not start Material Intake, and does not run `SetProjectStatus` from the skipped edges.

Preview warns when the project's current status is not the status the nearest skipped `SetProjectStatus` action would have set. Adoption does not change `ProjectStatus`.

The current stage still gets its normal task. For `REV.ProfessionalReview` that is `PerformProfessionalReview`. If the stage group cannot resolve an assignee, Preview blocks before Commit.

An optional responsible user is accepted only when that user is an active member of the stage group. Reassignment uses `ITaskQueueService.ReassignAsync` after the workflow transaction. If reassignment fails, the workflow remains and the task stays on the group default.

## Transaction

`StartWorkflowAsync` (the public path) is unchanged and still uses separate contexts, including sub-workflow auto-start.

Adoption uses `StartWorkflowAtomicAsync`: one context, and on SQL Server one transaction around the instance, the initial transition, and the current-stage tasks. If provisioning throws, the transaction rolls back. The EF InMemory provider used by tests cannot roll back `SaveChanges`, so that path deletes the rows created by the failed start. Relational providers do not use that cleanup.

If the current stage provisions no task, the start is rolled back. Adoption does not leave an Active workflow with no task.

## Review reports

Report content import stays in `ReportImportService` (V2 `MigrationPocWindow`). Adoption does not copy that importer.

In the new host, Preview lists `InspectionReport` rows that already belong to the project, labeled with `InspectionSeries.SeriesName` and `ReportNumber` because report numbers are scoped to a series. Each selected report is Historical or Active. At most one report is Active.

- Historical, with no export snapshot: `MarkReportAsSentAsync` is not called, and `SentAt` / `IsLockedAfterSend` are not set by hand. Preview warns that the report stays open. `MarkReportAsSentAsync` needs an export spreadsheet and a note-cell map; inventing those would fake a send.
- Active: the current stage must have exactly one created task whose `ReviewTaskInteractionRegistry` work target is `InspectionReport`. That task is linked through `SqlInspectionReportTaskLinkService` on the existing `TaskLink` table (`Related`, `IsWorkTarget`, `Pending`). Zero matches or more than one match blocks Preview. There is no fallback to the first created task. The link is inside the adoption transaction and is idempotent.
- All reports Historical: the current task is not linked. A task such as `PerformProfessionalReview` stays in its normal creation mode, which can open the next report through the existing inspection flow.

The wizard Commit button stays disabled until Preview is run again after a change to workflow, JobType, stage, responsible user, or report choice.

`[ADOPTED]` is a Notes prefix (`StartsWith`), not a substring.

`InspectionDate` on reports created by `CreateReportAsync` / `ReportImportService` is the import time. Adoption does not rewrite it. There is no reliable historical inspection date on that import path.

## Limitations

- Sheet/JSON report import is still the V2 migration window, not this wizard.
- Historical reports are not locked unless they were already sent through the real export path.
- `REV.MaterialIntake` and final stages cannot be adopted yet.
- An existing Active workflow is never jumped forward.
- Reassignment of the responsible user is a second step after the workflow transaction.
- No `IsImported` / `ImportedAtUtc` columns. Query adopted workflows by the `[ADOPTED]` notes prefix.
