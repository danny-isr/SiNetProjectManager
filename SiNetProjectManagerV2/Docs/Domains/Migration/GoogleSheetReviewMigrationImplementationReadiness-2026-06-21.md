# Google Sheet Review Migration — Implementation Readiness

- **Date:** 21.06.2026
- **Status:** Active — implementation readiness gap register
- **Scope:** Readiness assessment for the Google Sheet Review Migration design against the current codebase.

---

## 1. Executive summary

**Overall readiness level:** Mostly ready with gaps

**Recommended next step:** Implementation can start with blockers resolved.

The core infrastructure for the migration (Inspection Reports, Workflow Engine, UI components) is robust and functional. However, there are specific implementation gaps, particularly around the read-only preview guarantees and workflow advancement rules, that must be addressed before coding the actual commit pipeline.

**Top 5 blockers / gaps:**
1. Verifying same-group constraint for reviewer reassignment.
2. Workflow advancement rules (skipping stages) if no direct transition exists.
3. Guaranteeing the new Preview is truly read-only (unlike the legacy `BuildPreviewAsync`).
4. Comparing existing report versions with JSON cleanly without relying on brittle string comparisons.
5. Creating placeholder notes for skipped numbering.

---

## 2. Readiness matrix

| Requirement | Current existing mechanism | Ready status | Recommended action | Files/services involved |
|---|---|---|---|---|
| Read Index Sheet | `IndexSheetReader.ReadAsync()` | Ready as-is | Use existing service | `IndexSheetReader.cs` |
| Resolve Project | `MigrationTaskService.ResolveProjectAsync` logic | Needs small extension | Extract read-only resolution logic | `MigrationTaskService.cs` |
| Detect duplicate project rows | None (assumes unique per project) | Needs new integration | Add grouping/validation in preview builder | Preview logic |
| Resolve reviewer / בודק | `TaskPriorityEngine.BuildUserLookupCachesAsync` | Ready as-is | Use existing lookup | `TaskPriorityEngine.cs` |
| Resolve or create Alternative 1 | `ProjectAlternativeService` | Needs small extension | Add `EnsureAlternativeAsync` helper | `ProjectAlternativeService.cs` |
| Read AI JSON cache | `ExtractionCacheService.LoadAsync()` | Ready as-is | Use existing service | `ExtractionCacheService.cs` |
| Export/import JSON cache ZIP | None | Gap / risk | Evaluate if needed for Phase 1. If yes, implement. | `ExtractionCacheService.cs` |
| Validate JSON envelope | `ExtractionCacheService` serialization | Needs small extension | Add structural validation on load | `ExtractionCacheService.cs` |
| Build main sections from JSON | `TemplateSyncService.SyncAsync()` | Ready as-is | Map JSON codes to `TemplateSyncRow` | `TemplateSyncService.cs` |
| Create InspectionSeries | `TemplateSyncService.EnsureSeriesAsync()` / manual creation | Needs small extension | Ensure series creation before reports | `InspectionReportService.cs` |
| Create InspectionReport versions | `InspectionReportService.CreateReportAsync()` | Ready as-is | Call per version | `InspectionReportService.cs` |
| Add detailed sub-notes from JSON | `InspectionReportService.AddNoteAsync()` | Ready as-is | Call per sub-note | `InspectionReportService.cs` |
| Create placeholder notes for numbering gaps | `AddNoteAsync` | Needs design decision | Decide if empty notes are acceptable long-term | `InspectionReportService.cs` |
| Populate note text/status/planner response | `InspectionReportService.SaveNotesAsync()` / direct DB | Needs small extension | Map JSON data to note entities | `InspectionReportService.cs` |
| Lock non-latest reports | `InspectionReportService.MarkReportAsSentAsync()` | Ready as-is | Call for historical versions | `InspectionReportService.cs` |
| Decide latest report open/closed state | Logic based on status | Ready as-is | Implement mapping rules | Migration logic |
| Compare existing report version with JSON | DB query `(SeriesId, ReportNumber)` + `SentSpreadsheetId` | Needs small extension | Add robust conflict detection | Migration logic |
| Detect report version conflict | DB query | Needs small extension | Surface in preview | Migration logic |
| Start Review Workflow at mapped stage | `WorkflowTaskOrchestrator.StartWorkflowAsync(initialStageCode)` | Ready as-is | Call with target stage | `WorkflowTaskOrchestrator.cs` |
| Complete final-status Workflow | `WorkflowEngine.CompleteAsync()` | Ready as-is | Call after start | `WorkflowEngine.cs` |
| Advance existing Workflow forward | `WorkflowTaskOrchestrator.AdvanceWithTasksAsync()` | Blocked | Verify if skipping stages is allowed | `WorkflowTaskOrchestrator.cs` |
| Prevent backward Workflow movement | `WorkflowStageDefinition.SortOrder` comparison | Needs small extension | Add validation in preview | Migration logic |
| Reassign workflow-created task to Sheet reviewer | `TaskService.ReassignTask()` | Blocked | Verify same-group constraint | `TaskService.cs` |
| Report reassignment failure clearly | None | Needs new integration | Add explicit logging/UI feedback | Migration commit logic |
| Classify Preview row | None | Needs new integration | Implement classification logic | Preview logic |
| Commit ready rows | None | Needs new integration | Implement commit pipeline | Commit logic |
| Manager Review rows | None | Needs new integration | Surface in UI | Preview logic |
| Log migration outcomes | `IActionLifecycleReporter` (maybe) | Needs design decision | Decide log storage mechanism | Logging logic |
| Ensure idempotency / rerun safety | DB unique constraints | Needs small extension | Wrap in transaction, verify existing | Commit logic |

---

## 3. Existing mechanisms to reuse

The following existing mechanisms must be reused before writing new code:

- `IndexSheetReader`: For reading the Google Sheet data.
- **Existing project resolution logic inside `MigrationTaskService`**: Reusable for finding projects, provided it is extracted to be read-only.
- `ExtractionCacheService`: For loading local JSON data.
- `GeminiExtractionService`: For AI extraction.
- `TemplateSyncService.SyncAsync`: For creating the main chapter/section structure from JSON.
- `InspectionReportService.CreateReportAsync`: To create the report version and initial placeholder notes.
- `InspectionReportService.AddNoteAsync`: To add detailed sub-notes from the JSON.
- `InspectionReportService.SaveNotesAsync`: To update note text and status.
- `InspectionReportService.MarkReportAsSentAsync`: To lock historical versions.
- `WorkflowTaskOrchestrator.StartWorkflowAsync`: To start the workflow at the correct stage.
- `WorkflowEngine.CompleteAsync`: To finalize completed workflows.
- `WorkflowTaskOrchestrator.AdvanceWithTasksAsync`: To move existing workflows forward.
- `WorkflowQueryService`: For finding existing workflows and stages.
- `WorkflowStageTaskProvisioningService`: To create workflow tasks.
- `TaskFactory`: Centralized task creation.
- `TaskService.ReassignTask`: For assigning the reviewer.
- `TaskPriorityEngine`: For user lookup caches.
- **Existing TaskLink behavior**: To link tasks to the workflow.
- **Existing user/group lookup mechanisms**: For resolving the reviewer.
- **Existing logging / diagnostics mechanisms**: If suitable for recording migration outcomes.

---

## 4. Active legacy mechanisms

These old mechanisms still exist and **must not be deleted yet**:

| Mechanism | Current status | Why it remains | Can be touched in phase 1? |
|---|---|---|---|
| `MigrationTaskService.BuildPreviewAsync` | Active legacy | Used by the current active migration path | No. Must be preserved. |
| `MigrationTaskService.CommitTasksAsync` | Active legacy | Creates standalone ProjectAssignment tasks for current path | No. Must be preserved. |
| Old standalone task creation path | Active legacy | Still actively used across the system | No. |
| R01ReportDialog / R01 path | Active, separate | Separate workflow from standard Inspection Reports | No. |
| Oversized ViewModels (e.g., `EmailManagementViewModel`, `FloatingInspectionViewModel`) | Candidate for future cleanup | Large refactoring risk | No. |
| Disabled legacy code (e.g., `GmailVisibleAttachmentsDomExtractor`) | Disabled legacy | Candidate for deletion, but out of scope | No. |

**Default rule:** Do not delete legacy mechanisms in this implementation phase.

---

## 5. Blockers

The following issues must be resolved before implementation starts:

1. **Same-group constraint for reviewer reassignment:** Verify if `TaskService.ReassignTask()` will block reassignment if the Sheet reviewer is not in the same `UserGroup` as the workflow default assignee.
2. **Workflow advancement without transition rules:** Verify if `WorkflowTaskOrchestrator.AdvanceWithTasksAsync()` can advance an existing workflow from stage A to stage C (skipping B) if no direct transition exists.
3. **Read-only preview:** Verify that `MigrationTaskService.BuildPreviewAsync` currently writes to the DB (it does) and therefore cannot be reused as-is. A new, truly read-only preview builder is required.
4. **Comparing existing report versions:** Verify the existing clean way to compare JSON report content with existing `InspectionReport` versions (likely using `SentSpreadsheetId`).
5. **Placeholder notes:** Verify the acceptable way to create placeholder notes for skipped numbering (e.g., calling `AddNoteAsync` with empty text).
6. **Ensure Alternative 1:** Verify if there is an existing mechanism to ensure Alternative "1" without duplicating alternatives.
7. **JSON cache ZIP export/import:** Verify if this exists or needs implementation.
8. **Migration outcome logging:** Verify if a mechanism already exists or if a new documentation-approved mechanism is needed.

---

## 6. Non-blocking gaps

These gaps should be documented but do not block the first implementation phase:

- No bulk note creation API.
- Large ViewModels (`FloatingInspectionViewModel`, `EmailManagementViewModel`).
- No direct report-to-workflow link.
- No migration mode in UI.
- No dedicated diagnostics for every Preview classification.
- No dedicated Migration dashboard yet.

---

## 7. Implementation phases proposal

**Phase 0 — Safety and documentation alignment:**
- Confirm no write operations in new Preview.
- Keep old migration path active legacy.
- Define new Preview row model.
- Define classification enum.

**Phase 1 — Read-only Preview:**
- Read Index Sheet.
- Resolve project exactly.
- Detect duplicate rows.
- Read JSON cache.
- Resolve reviewer.
- Determine workflow target stage.
- Determine report action.
- Classify rows.
- Show warnings/conflicts.

**Phase 2 — Report import only:**
- Ensure Alternative 1.
- Ensure InspectionSeries.
- Create sections from JSON.
- Create report versions.
- Add sub-notes.
- Lock versions as needed.
- Idempotency checks.

**Phase 3 — Workflow reconstruction:**
- Start Workflow at mapped stage.
- Complete final-status workflows.
- Reassign task to reviewer.
- Handle reassignment failures.
- Do not move backwards.
- Manager Review for unclear existing workflows.

**Phase 4 — Combined commit:**
- Combine report import + workflow reconstruction.
- Row-level transaction/scoping.
- Result report.
- Rerun safety.

**Phase 5 — Cleanup candidate review:**
- Only after testing, review whether old task-generation migration path can be deprecated.
- **Do not delete in this task.**

---

## 8. Preview classifications

| Classification | Trigger condition | Blocks commit? | Message to show | Auto-resolvable? |
|---|---|---|---|---|
| Commit Ready | Exact project, valid status, no conflicts, reviewer resolved | No | "Ready to commit" | Yes |
| Commit Ready with warning | Same as above, but missing JSON | No | "Workflow only; missing JSON" | Yes |
| Already Done | Existing workflow at same/later stage + matching reports | Yes (Skip) | "Already up to date" | Yes (Skip) |
| Manager Review | Likely project match, existing workflow needs advancing, reviewer group mismatch | Yes | "Requires review: [Reason]" | No |
| Conflict | Incompatible existing workflow, differing reports | Yes | "Conflict: [Reason]" | No |
| No Match | Project not found | Yes | "Project not found" | No |
| Missing Data | Insufficient Sheet data | Yes | "Insufficient data" | No |
| JSON Missing | Required for action, but missing | Yes | "Missing required JSON" | No |
| Reviewer Not Found | Reviewer lookup failed | No (Warning) | "Reviewer not found; using default" | Yes (Fallback) |
| Reviewer Group Mismatch | Reviewer found but not in group | Yes (Unless accepted) | "Reviewer group mismatch" | No (Requires accept) |
| Existing Workflow Conflict | Multiple workflows, or backward movement | Yes | "Workflow conflict" | No |
| Existing Report Conflict | Differing versions | Yes | "Report version conflict" | No |
| Duplicate Project Row | Multiple rows for same project | Yes | "Duplicate project row" | No |
| Backward Movement | Target stage < Current stage | Yes | "Backward movement" | No |

---

## 9. Data safety and idempotency

- **No silent overwrite:** Existing reports must not be overwritten.
- **Same report version with same source:** Classified as `Already Done`.
- **Same report version with different source/content:** Classified as `Conflict`.
- **Existing workflow same stage:** Classified as `Already Done` or no-op.
- **Existing workflow behind target:** Forward advancement only if valid and clear.
- **Existing workflow ahead or backward movement:** Classified as `Manager Review` / `Conflict`.
- **Duplicate Sheet rows for same project:** Classified as `Manager Review` / `Conflict`.
- **Missing JSON but valid Sheet data:** Workflow-only creation allowed with warning.
- **Missing Sheet data:** Classified as `Manager Review` or `No Match`.

---

## 10. Open decisions

1. Should reviewer reassignment be allowed across groups for migration, or should users/groups be fixed before migration?
2. If Workflow cannot advance from stage A to C directly, should migration:
   - create Workflow directly at target stage when no existing workflow exists only,
   - leave existing unclear workflow for Manager Review,
   - or add a documented migration-only advancement helper?
3. Should placeholder notes for skipped numbering be preserved permanently or allowed to be reindexed by UI later?
4. What is the exact migration outcome log storage mechanism?
5. Should JSON ZIP export/import be implemented in phase 1 or later?

---

## 11. Explicit non-goals

- No DB schema changes in readiness task.
- No implementation in readiness task.
- No new fallback mechanisms.
- No new report storage model.
- No new workflow task mechanism.
- No email action changes.
- No deletion of legacy code.
- No automatic backward workflow movement.
- No silent overwrite.
- No new police-comments workflow stage.

---

## 12. Dropped / cancelled / postponed

| Item | Status |
|---|---|
| Creating a parallel report mechanism | Cancelled |
| Creating migration-only standalone tasks | Cancelled |
| Deleting old migration task path now | Not approved |
| Requiring modern template to contain every historical detailed sub-section | Dropped |
| Inventing missing section content | Not approved |
| Adding fallback actions/mechanisms | Not approved |
| Moving workflows backwards automatically | Not approved |
| Adding new workflow stage for police-comments review | Not approved |
| Refactoring oversized ViewModels before migration | Postponed |
| Creating bulk note API before proving need | Postponed |
| Direct report-to-workflow link | Postponed unless proven necessary |
| DB migrations | Not approved |
| Code changes | Not approved in this readiness task |
