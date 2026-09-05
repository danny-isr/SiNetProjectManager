# REV — SendReportToPlanner stage (target)

> **Status:** Target / Implementation  
> **Updated:** 2026-09-05  
> **Scope:** Review workflow happy path after manager approval

## Problem

`ManagerApproved` previously advanced directly to `REV.AwaitingPlannerCorrections`, which only provisions `TrackPlannerCorrections`. Nothing created/sent `SendReportToPlanner`, so the planner never received the review comments on that path.

## Target happy path

```
REV.ProfessionalReview
  --ProfessionalReviewCompleted-->
REV.AwaitingManagerApproval
  --ManagerApproved-->
REV.SendReportToPlanner          ← stage task: SendReportToPlanner
  --CommentsSentToPlanner-->
REV.AwaitingPlannerCorrections   ← stage task: TrackPlannerCorrections
```

`ManagerRequestedChanges` remains:

```
REV.AwaitingManagerApproval → REV.ProfessionalReview
```

## Status rule

`ProjectStatus = WaitingForClient` is set **only** on `CommentsSentToPlanner` (actual outbound send), not on manager approval alone.

## Work-target contract (related)

Stage tasks whose interaction declares `PrimaryWorkTargetEntityType = InspectionReport` must:

1. Keep workflow trigger Email as **Source** provenance only.
2. Carry forward the single InspectionReport work-target from prior tasks on the **same** WorkflowInstance (fail closed on 0 when required, or >1 distinct reports).
3. Never treat Email as the primary work target for report tasks (`ApproveReviewReport`, `SendReportToPlanner`, `RecheckPlan`, etc.).

## Existing instances

`#83` already at `REV.AwaitingManagerApproval` continues from there. Re-seed adds the new stage/rules and removes the obsolete `ManagerApproved → AwaitingPlannerCorrections` rule; it must not reset `CurrentStageId`.

## Manager approval surface

`ApproveReviewReport` / `ResubmitToManager` use component key `Component.ManagerReviewApproval`.
That key must open the **same** Inspection Report work surface as `Component.InspectionReport`
(exact task-linked report + allowed manager result codes). It must not fall through as unsupported.

When more than one allowed result exists (`ManagerApproved` / `ManagerRequestedChanges`), the Inspection
window must expose a result ComboBox before **השלם משימה** (same contract as `InspectionShellView`).
