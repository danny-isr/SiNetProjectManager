# Google Sheet Review Migration — Implementation Readiness

- **Date:** 21.06.2026
- **Status:** Active — implementation readiness gap register (revised 26.06.2026 for template alignment)
- **Scope:** Readiness assessment for the Google Sheet Review Migration design against the current codebase.

---

## 1. Executive summary

**Overall readiness level:** Mostly ready with gaps

**Recommended next step:** Implementation can start with Phase 0 and Phase 1.

The core infrastructure for the migration (Inspection Reports, Workflow Engine, UI components) is robust and functional. Following user decisions on the migration blockers, we have a clear path forward. The migration will use a new read-only Preview, a user-approved reviewer mapping step, and strict workflow advancement rules.

**Top remaining blockers / gaps:** *(revised 26.06.2026)*
1. Creating placeholder notes for skipped numbering cleanly.
2. Defining the exact migration outcome log storage mechanism.
3. Report numbering / idempotency validation for first Phase 2 slice (primary guard: `SeriesId + ReportNumber`).

> **Note:** "Determining how to enforce Alternative 1" was previously listed here but is **resolved / postponed** — `ProjectAlternative` is not required for Phase 2 (no FK from `InspectionSeries`/`InspectionReport` to `ProjectAlternative`). See §5 blocker #2.

---

## 2. Readiness matrix

> **Phase key:** P1 = Phase 1 (read-only Preview), P2 = Phase 2 (Report import), P3 = Phase 3 (Workflow reconstruction), P4 = Phase 4 (Combined commit).
> Phase 1 Preview does not require any write-side services. It only reads from DB, Google Sheets, and the local JSON cache.

| Requirement | Phase | Current existing mechanism | Ready status | Recommended action | Files/services involved |
|---|---|---|---|---|---|
| Read Index Sheet | P1 | `IndexSheetReader.ReadAsync()` | Ready as-is | Use existing service | `IndexSheetReader.cs` |
| Resolve Project | P1 | `MigrationTaskService.ResolveProjectAsync` logic | Needs small extension | Extract read-only resolution logic | `MigrationTaskService.cs` |
| Detect duplicate project rows | P1 | None (assumes unique per project) | Needs new integration | Add grouping/validation in preview builder | Preview logic |
| Resolve reviewer / בודק | P1 | `TaskPriorityEngine.BuildUserLookupCachesAsync` | Needs new integration | Build mapping UI for user approval | `TaskPriorityEngine.cs` + new UI |
| Resolve or create Alternative 1 | P3+ | `ProjectAlternativeService` | Not required for Phase 2 | No FK from InspectionSeries/InspectionReport to ProjectAlternative. Postponed. | `ProjectAlternativeService.cs` |
| Read AI JSON cache | P1 | `ExtractionCacheService.LoadAsync()` | Ready as-is | Use existing service | `ExtractionCacheService.cs` |
| Export/import JSON cache ZIP | P2+ | None | Postponed | Not needed for Phase 1 | `ExtractionCacheService.cs` |
| Validate JSON envelope | P1 | `ExtractionCacheService` serialization | Ready as-is | Envelope schema is stable | `ExtractionCacheService.cs` |
| Build main sections from user-selected template | P2 | `TemplateSyncService.SyncAsync()` | Ready as-is | Call with full template rows from `GoogleInspectionTemplateProvider.ScanAndParseTemplateAsync` — **not** from JSON section codes | `TemplateSyncService.cs`, `GoogleInspectionTemplateProvider.cs` |
| Create InspectionSeries | P2 | `TemplateSyncService.EnsureSeriesAsync()` / manual creation | Needs small extension | Ensure series creation before reports | `InspectionReportService.cs` |
| Create InspectionReport versions | P2 | `InspectionReportService.CreateReportAsync()` | Ready as-is | Call per version | `InspectionReportService.cs` |
| Add detailed sub-notes from JSON | P2 | `InspectionReportService.AddNoteAsync()` | Ready as-is | Call per sub-note | `InspectionReportService.cs` |
| Create placeholder notes for numbering gaps | P2 | `AddNoteAsync` | Needs design decision | Decide if empty notes are acceptable long-term | `InspectionReportService.cs` |
| Populate note text/status/planner response | P2 | `InspectionReportService.SaveNotesAsync()` / direct DB | Needs small extension | Map JSON data to note entities | `InspectionReportService.cs` |
| Lock non-latest reports | P2+ | `InspectionReportService.MarkReportAsSentAsync()` | Ready as-is | **Postponed** beyond first Phase 2 slice | `InspectionReportService.cs` |
| Decide latest report open/closed state | P1 | Logic based on status | Ready as-is | Implement mapping rules | Migration logic |
| Compare existing report version with JSON | P1 | `SentSpreadsheetId` | Ready as-is | Compare with JSON `ReportSpreadsheetId` | Migration logic |
| Detect report version conflict | P1 | DB query | Needs small extension | Surface in preview | Migration logic |
| Start Review Workflow at mapped stage | P3 | `WorkflowTaskOrchestrator.StartWorkflowAsync(initialStageCode)` | Ready as-is | Call with target stage | `WorkflowTaskOrchestrator.cs` |
| Complete final-status Workflow | P3 | `WorkflowEngine.CompleteAsync()` | Ready as-is | Call after start | `WorkflowEngine.cs` |
| Advance existing Workflow forward | P3 | `WorkflowTaskOrchestrator.AdvanceWithTasksAsync()` | Blocked | Direct jumps not supported. Must classify as Manager Review. | `WorkflowTaskOrchestrator.cs` |
| Prevent backward Workflow movement | P1 | `WorkflowStageDefinition.SortOrder` comparison | Needs small extension | Add validation in preview | Migration logic |
| Reassign workflow-created task to Sheet reviewer | P3 | `TaskService.ReassignTask()` | Needs new integration | Classify Group Mismatches and use UI mapping | `TaskService.cs` |
| Report reassignment failure clearly | P3 | None | Needs new integration | Add explicit logging/UI feedback | Migration commit logic |
| Classify Preview row | P1 | None | Needs new integration | Implement classification logic | Preview logic |
| Read-only preview | P1 | None | Needs new integration | Build new preview logic without DB writes | Preview logic |
| Commit ready rows | P4 | None | Needs new integration | Implement commit pipeline | Commit logic |
| Manager Review rows | P1 | None | Needs new integration | Surface in UI | Preview logic |
| Log migration outcomes | P4 | `IActionLifecycleReporter` (maybe) | Needs design decision | Decide log storage mechanism | Logging logic |
| Ensure idempotency / rerun safety | P4 | DB unique constraints | Needs small extension | Wrap in transaction, verify existing | Commit logic |
| Select target template via dropdown | P1 | `InspectionTemplateItem` dropdown, `GoogleInspectionTemplateProvider` | ✅ Done (26.06.2026) | User selects template from existing dropdown; uses `ScanAndParseTemplateAsync` | `MigrationPocWindow.xaml.cs`, `GoogleInspectionTemplateProvider.cs` |
| Template Compatibility Preview | P1 | `TemplateCompatibilityResult`, `ValidateTemplateCompatibility()` | ✅ Done (26.06.2026) | Validates JSON sections against selected template; blocks Phase 2 for NoMatch/TemplateError | `GoogleSheetReviewMigrationPreviewService.cs`, `TemplateCompatibilityResult.cs` |
| Phase 2 blocked for NotValidated/TemplateError/NoMatch | P2 | Template Compatibility Preview | Implemented in Preview | Phase 2 import only allowed for FullMatch/PartialMatch rows | Preview + import logic |

---

## 3. Existing mechanisms to reuse

The following existing mechanisms must be reused before writing new code:

- `IndexSheetReader`: For reading the Google Sheet data.
- **Existing project resolution logic inside `MigrationTaskService`**: Reusable for finding projects, provided it is extracted to be read-only.
- `ExtractionCacheService`: For loading local JSON data.
- `GeminiExtractionService`: For AI extraction.
- `TemplateSyncService.SyncAsync`: For creating the main chapter/section structure from the **user-selected target template** (not from JSON).
- `GoogleInspectionTemplateProvider.ScanAndParseTemplateAsync`: For reading the target template structure.
- `InspectionReportService.CreateReportAsync`: To create the report version and initial placeholder notes.
- `InspectionReportService.AddNoteAsync`: To add detailed sub-notes from the JSON.
- `InspectionReportService.SaveNotesAsync`: To update note text and status.
- `InspectionReportService.MarkReportAsSentAsync`: To lock historical versions.
- `WorkflowTaskOrchestrator.StartWorkflowAsync`: To start the workflow at the correct stage.
- `WorkflowEngine.CompleteAsync`: To finalize completed workflows.
- `WorkflowTaskOrchestrator.AdvanceWithTasksAsync`: To move existing workflows forward (if direct transition exists).
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
| `MigrationTaskService.BuildPreviewAsync` | Active legacy | Used by the current active migration path (writes to DB) | No. Must be preserved. |
| `MigrationTaskService.CommitTasksAsync` | Active legacy | Creates standalone ProjectAssignment tasks for current path | No. Must be preserved. |
| Old standalone task creation path | Active legacy | Still actively used across the system | No. |
| R01ReportDialog / R01 path | Active, separate | Separate workflow from standard Inspection Reports | No. |
| Oversized ViewModels (e.g., `EmailManagementViewModel`, `FloatingInspectionViewModel`) | Candidate for future cleanup | Large refactoring risk | No. |
| Disabled legacy code (e.g., `GmailVisibleAttachmentsDomExtractor`) | Disabled legacy | Candidate for deletion, but out of scope | No. |

**Default rule:** Do not delete legacy mechanisms in this implementation phase.

---

## 5. Blockers

The following issues must be resolved before implementation starts:

1. **Placeholder notes:** Verify the acceptable way to create placeholder notes for skipped numbering (e.g., calling `AddNoteAsync` with empty text).
2. ~~**Ensure Alternative 1:**~~ **Resolved (26.06.2026):** `ProjectAlternative` is not required for Phase 2. No FK from `InspectionSeries`/`InspectionReport` to `ProjectAlternative`. Postponed.
3. **Migration outcome logging:** Verify if a mechanism already exists or if a new documentation-approved mechanism is needed.
4. **Template Compatibility Preview:** ✅ **Resolved (26.06.2026).** Template selection dropdown and compatibility validation are implemented in Phase 1 Preview.

*(Note: Reviewer reassignment, workflow skipping, read-only preview, and report comparison blockers were resolved by User Decisions. See Section 13.)*

---

## 6. Non-blocking gaps

These gaps should be documented but do not block the first implementation phase:

- No bulk note creation API.
- Large ViewModels (`FloatingInspectionViewModel`, `EmailManagementViewModel`).
- No direct report-to-workflow link.
- No migration mode in UI.
- No dedicated diagnostics for every Preview classification.
- No dedicated Migration dashboard yet.
- JSON cache ZIP export/import is postponed.

---

## 7. Implementation phases proposal

**Phase 0 — Safety and documentation alignment:**
- Confirm no write operations in new Preview.
- Keep old migration path active legacy.
- Define new Preview row model.
- Define classification enum.

**Phase 1 — Read-only Preview & Mapping:**
- Read Index Sheet.
- Resolve project exactly (read-only).
- Detect duplicate rows.
- Read JSON cache (read-only).
- Build Reviewer Mapping UI (User maps Sheet reviewers to System Users).
- Apply mapped reviewers.
- Determine workflow target stage.
- Determine report action.
- Classify rows strictly based on new classification definitions.
- Show warnings/conflicts.

**Phase 2 — Report import only:** *(revised 26.06.2026)*
- Ensure InspectionSeries (using user-selected target template SpreadsheetId).
- Sync sections from **user-selected target template** (not from JSON).
- Create report versions.
- Add sub-notes (only for sections matched in Template Compatibility Preview).
- Skip notes for unmatched/missing sections with warnings.
- Idempotency checks.
- **First slice does NOT call:** `MarkReportAsSentAsync`, `ProjectAlternativeService.CreateAsync`.

**Phase 3 — Workflow reconstruction:**
- Start Workflow at mapped stage.
- Complete final-status workflows.
- Reassign task to reviewer (based on mapped user).
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
| Manager Review | Likely project match, existing workflow needs advancing without direct rules | Yes | "Requires review: [Reason]" | No |
| Conflict | Incompatible existing workflow, differing reports | Yes | "Conflict: [Reason]" | No |
| No Match | Project not found | Yes | "Project not found" | No |
| Missing Data | Insufficient Sheet data | Yes | "Insufficient data" | No |
| JSON Missing | Required for action, but missing | Yes | "Missing required JSON" | No |
| Reviewer Not Mapped | Reviewer missing from User Mapping | Yes | "Reviewer mapping required" | No (Requires Map) |
| Reviewer Group Mismatch | Reviewer mapped but not in required group | Yes | "Reviewer group mismatch" | No (Requires Accept) |
| Existing Workflow Conflict | Multiple workflows, or backward movement | Yes | "Workflow conflict" | No |
| Existing Report Conflict | Differing versions (Sheet vs DB) | Yes | "Report version conflict" | No |
| Duplicate Project Row | Multiple rows for same project | Yes | "Duplicate project row" | No |
| Backward Movement | Target stage < Current stage | Yes | "Backward movement" | No |

### 8.1 Phase 2 readiness criteria (added 26.06.2026)

Phase 2 import is allowed only when:

| Criterion | Required? | How verified |
|---|---|---|
| Target template selected via dropdown | Mandatory | `TargetTemplateComboBox.SelectedItem != null` |
| Template Compatibility Preview completed | Mandatory | `TemplateValidationStatus ≠ NotValidated` |
| No `TemplateError` for target row | Mandatory | `TemplateValidationStatus ≠ TemplateError` |
| At least one matched section/note | Mandatory | `TemplateMatchedNoteCount > 0` |
| Mismatch/missing sections visible to user | Mandatory | Preview displays `TemplateMismatchCount`, `TemplateMissingSectionCount`, `TemplateWarnings` |
| Import blocked for `NoMatch` | Mandatory | Rows with `TemplateValidationStatus = NoMatch` are not eligible for Phase 2 |
| Report numbering does not conflict | Mandatory | Validate against existing reports in target series before import |

---

## 9. Data safety and idempotency

- **No silent overwrite:** Existing reports must not be overwritten.
- **Same report version with same source:** Classified as `Already Done`.
- **Same report version with different source/content:** Classified as `Conflict`.
- **Existing workflow same stage:** Classified as `Already Done` or no-op.
- **Existing workflow behind target:** Forward advancement only if valid direct rule exists. If no direct rule exists, classify as `Manager Review`.
- **Existing workflow ahead or backward movement:** Classified as `Manager Review` / `Conflict`.
- **Duplicate Sheet rows for same project:** Classified as `Manager Review` / `Conflict`.
- **Missing JSON but valid Sheet data:** Workflow-only creation allowed with warning.
- **Missing Sheet data:** Classified as `Manager Review` or `No Match`.

---

## 10. Open decisions

1. Should placeholder notes for skipped numbering be preserved permanently or allowed to be reindexed by UI later?
2. What is the exact migration outcome log storage mechanism?

*(Note: Open decisions regarding Reviewer Group Bypass, Workflow Stage Jumping, and JSON zip export were resolved by User Decisions. See Section 13.)*

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
| Reusing legacy `BuildPreviewAsync` as new Preview | Cancelled |
| Silent fallback to default reviewer | Cancelled |
| Bypassing same-group reassignment rules | Not approved |
| Automatic stage jump helper | Not approved |
| Running extraction again in this task | Not approved |
| JSON ZIP import/export | Undecided; likely postponed unless needed |
| Full migration commit | Postponed |
| DB schema changes | Not approved |
| Code implementation | Not approved in this investigation task |
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

---

## 13. User decisions after blocker review

### Decision 1 — New read-only Preview is required
- The migration must use a **new, purely read-only Preview mechanism**.
- It must not reuse `MigrationTaskService.BuildPreviewAsync` as-is, because the legacy code writes to the DB (it creates `ProjectAssignment` tasks directly, sets status, and creates historical records).
- The new Preview must not create `TaskType`, `Status`, `ProjectAssignment`, `WorkflowInstance`, `TaskLink`, `InspectionReport`, etc.
- `MigrationTaskService.BuildPreviewAsync` remains **active legacy** and must not be deleted.

### Decision 2 — Reviewer / בודק mapping will be user-approved
- Do not solve reviewer assignment by bypassing the same-group constraint.
- A **Reviewer Mapping Step** must be built before the migration commit phase.
- The UI will present all distinct reviewer names from the Sheet, allowing the user to map them to internal system users.
- If unmapped, classify as `Reviewer Not Mapped`.
- If mapped but group rules prevent assignment, classify as `Reviewer Group Mismatch`.
- **No automatic fallback to default user.**

---

## 14. Workflow advancement research result

**Result:** Only direct transition supported (for existing workflows).

**Findings:**
1. `WorkflowEngine.AdvanceStageAsync` strictly validates that a transition rule exists between the current stage and the target stage. It cannot jump stages.
2. `WorkflowTaskOrchestrator.AdvanceWithTasksAsync` relies on `AdvanceStageAsync`, meaning it also cannot skip stages.
3. If we advance step-by-step, it will create tasks for all intermediate stages, creating false history.
4. However, `StartWorkflowAsync(initialStageCode)` **does** allow starting a brand-new workflow directly at the requested stage, and it provisions only that stage's tasks. For final statuses, `CompleteAsync()` must be called separately.

**Recommendation:**
- For brand-new workflows: Use `StartWorkflowAsync(initialStageCode)` safely.
- For existing workflows behind the target stage: **Do not jump stages.** Classify the row as `Manager Review` / `Conflict`.
- Do not implement a migration-only stage-jump helper unless explicitly approved later.

---

## 15. JSON cache and report comparison research result

**Result:** Current JSON is sufficient for both Preview and Import. The deterministic comparison rule is clear.

**Findings:**
1. **Cache Path:** `%APPDATA%\SiNet\ExtractionCache\{project_number}\`
2. **File Naming Convention:** `R{reportNumber}_V{versionIndex}.json` (e.g., `R1_V2.json`). Duplicate handling adds suffixes (e.g., `.1.json`).
3. **JSON Structure:** `ExtractionCacheEnvelope` contains `ProjectNumber`, `ReportNumber`, `VersionIndex`, `TemplateSpreadsheetId`, `ReportSpreadsheetId`, `ExtractedAtUtc`, `Sections`, `GeneralFields`, and `Warnings`.
4. **Report Comparison Rule (Phase 1 & 2):** Use the `ReportSpreadsheetId` from the JSON to compare with the `SentSpreadsheetId` on the existing `InspectionReport` in the DB. This is a robust and deterministic key.
5. **Missing JSON:** If JSON is missing for a valid sheet row, we classify it as `Commit Ready with warning` ("Workflow only; missing JSON").
6. **ZIP Export/Import:** Not necessary for Phase 1 since the cache is on disk and Phase 1 only reads it. It is postponed.

**Recommendation:**
- Phase 1 can read the existing JSON cache via `ExtractionCacheService.LoadAsync()` without changing it.
- Use `ReportSpreadsheetId` vs `SentSpreadsheetId` as the primary conflict-detection rule.
