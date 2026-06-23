# Google Sheet Review Migration Design

- **Date:** 21.06.2026
- **Status:** Active — Source of truth for the Google Sheet Review migration pipeline
- **Scope:** Google Sheet Index Sheet → AI Extraction → JSON Cache → Historical Inspection Report Import → Review Workflow Reconstruction

---

## 1. Purpose

Migrate historical inspection/review tracking data from a Google Sheet "Index Sheet" into the system's Workflow-first model. The migration:

1. Reads the Index Sheet to identify projects and their review status.
2. Uses AI extraction to build local JSON cache of report content from linked Google Sheet reports.
3. Creates Inspection Reports (with notes and versions) using existing report mechanisms.
4. Reconstructs Review Workflows at the correct stage using existing workflow mechanisms.
5. Lets the Workflow provision and manage stage tasks through existing provisioning services.
6. Reassigns the provisioned task to the Sheet reviewer using existing task reassignment.

After migration, users continue working through the standard `FloatingInspectionView` and workflow interfaces as if the data was created normally.

---

## 2. Current pipeline

### 2.1. File locations

| Component | File | Purpose |
|---|---|---|
| `MigrationPocWindow` | `WPF Window\MigrationPocWindow.xaml.cs` | UI: Tab 1 (Extraction), Tab 2 (Task Generation) |
| `IndexSheetReader` | `Services\Migration\IndexSheetReader.cs` | Reads Index Sheet, auto-detects Hebrew headers, parses rows + hyperlinks |
| `MigrationTaskService` | `Services\Migration\MigrationTaskService.cs` | Two-phase: BuildPreview + CommitTasks (old model) |
| `GeminiExtractionService` | `Services\Migration\GeminiExtractionService.cs` | AI-powered report content extraction via Gemini API |
| `ReportContentExtractor` | `Services\Migration\ReportContentExtractor.cs` | Deterministic anchor-based extraction (no AI) |
| `ExtractionCacheService` | `Services\Migration\ExtractionCacheService.cs` | JSON cache: save/load/exists per project+version+report |
| `NoteSplitter` | `Services\Migration\NoteSplitter.cs` | Splits merged note cells into individual segments |
| `IndexSheetRow` | `Services\Migration\IndexSheetRow.cs` | DTOs |
| `ExtractedSectionData` | `Services\Migration\ExtractedSectionData.cs` | Section-level extraction result model |

#### Phase 1 Preview files (added 22.06.2026)

| Component | File | Purpose |
|---|---|---|
| `GoogleSheetReviewMigrationPreviewService` | `Services\Migration\GoogleSheetReviewMigrationPreviewService.cs` | Read-only preview: scans reviewers, resolves projects, checks workflows/reports, classifies rows |
| `GoogleSheetReviewMigrationPreviewRow` | `Services\Migration\Models\GoogleSheetReviewMigrationPreviewRow.cs` | DTO for a single preview result row |
| `MigrationPreviewClassification` | `Services\Migration\Models\MigrationPreviewClassification.cs` | Enum: CommitReady, CommitReadyWithWarning, NoMatch, ManagerReview, ExistingReportConflict, etc. |
| `ReviewerMappingItem` | `Services\Migration\Models\ReviewerMappingItem.cs` | DTO for the reviewer mapping UI (Sheet name → system user) |

### 2.2. Active legacy path

The current `MigrationTaskService` is **active legacy** and is **not deleted now**:

| Method | What it does | Classification |
|---|---|---|
| `BuildPreviewAsync` | Ensures old-model TaskType ("בדיקת תוכנית") + Status rows in DB, resolves projects, builds preview | **Active legacy** — writes global entities |
| `CommitTasksAsync` | Creates standalone `ProjectAssignment` tasks with `ProjectTypeTaskType` / `ProjectTypeStatus` linkage | **Active legacy** — candidate for future replacement |

The legacy path:
- May create old-model `TaskType` / `ProjectAssignmentStatus` rows.
- Creates standalone `ProjectAssignment` tasks (not linked to any workflow).
- Uses inline `new ProjectAssignment` + `TaskPriorityEngine`, not `TaskFactory`.
- Remains active for now. Not deleted. Not the target path for the Workflow-first migration.

### 2.3. New target path (this design)

The new migration path is **Workflow-first** and **InspectionReport-first**:

- **Preview is truly read-only** — no DB writes, no entity creation, no Google Drive writes.
- **Commit creates**: InspectionSeries → Sections (from template, via `TemplateSyncService`) → InspectionReports (with notes from JSON, via `CreateReportAsync` + `AddNoteAsync`) → Review Workflow (at correct stage) → stage tasks (via Workflow provisioning) → task reassignment (to Sheet reviewer).
- **Does not create**: standalone old-model `ProjectAssignment` tasks, `ProjectTypeTaskType` links, `ProjectTypeStatus` links.

---

## 3. Closed product decisions

### Decision 1 — Reviewer / Assignee from Sheet

The Sheet field בודק represents the person responsible for the Review task.

**Accepted approach:**
1. Start/reconstruct the Workflow using the normal workflow mechanism.
2. Let the Workflow create the stage task through `WorkflowStageTaskProvisioningService`.
3. After the task exists, if the Sheet reviewer resolves to a system user, reassign the task using `TaskService.ReassignTask()`.

**Research findings — task reassignment mechanism:**

| Item | Finding |
|---|---|
| Service method | `TaskService.ReassignTask(int taskId, int newAssigneeId, int changedByUserId)` in `SiNetSQL\Services\TaskService.cs` |
| Validation | Same-group constraint: new assignee must share at least one active `UserGroup` with current assignee |
| Updates | Sets `AssignedToId`, `Modified`, `EditorId` |
| Audit | Creates `ProjectAssignmentEvent` with `EventType = AssigneeChange` and Hebrew note |
| Priority gap | Does **NOT** update `WorkPriority` — task retains old position. No call to `CompactAfterRemoval` or `GetNextPriority` |
| UI support | Full UI support in `TaskViewModelBase.ReassignSelectedTaskCommand` |

**WorkflowInstance reviewer field:**
`WorkflowInstance` has **no** `OwnerId`, `ResponsibleUserId`, `AssigneeId`, or `ReviewerId` field. Only `CreatedByUserId`. The reviewer identity must be applied to the workflow-created task, not to the `WorkflowInstance` itself.

**Reassignment failure visibility in Preview:**
- If the reviewer is resolved and belongs to the same `UserGroup` as the workflow default assignee → `Commit Ready`.
- If the reviewer is resolved but may fail reassignment due to group mismatch → Preview shows a warning: `"⚠️ Reviewer not in same group — reassignment may fail; group default will be kept"`. Row becomes `Manager Review` unless the user explicitly accepts keeping the group default.
- If reassignment fails during Commit despite Preview → clearly reported in the Commit result as `"Reassignment failed: {reason}. Task assigned to group default."`.
- Do not silently keep the group default without Preview visibility.

**Identified gaps:**
1. `ReassignTask` does not update `WorkPriority` — the task retains its old queue position after reassignment.
2. Same-group constraint may block reassignment if the Sheet reviewer is not in the same group as the workflow-provisioned default assignee.

> **Related design:** Gap 1 is also referenced by `Docs/Domains/ProjectWork/PersonalWorkQueuesByTaskSize-2026-06-23.md`. That design widens the fix scope: once per-employee task-size buckets exist, the reassignment compact/insert must operate within `AssignedToId + WorkQueueBucket`, not just per employee.

### Decision 6 — Phase 1 reviewer group validation deferral (decided 23.06.2026)

Phase 1 Preview is strictly read-only and does not perform actual commit or task reassignment. Therefore:

- **Phase 1 may classify mapped reviewers as `CommitReadyWithWarning`** even when group membership has not been validated. This is acceptable because Phase 1 does not commit or reassign.
- The warning message `"Reviewer mapped — group validation not verified in Phase 1"` is shown on every mapped-reviewer row.
- **Before any real commit/reassignment is implemented (Phase 3+), group-membership validation is mandatory:**
  - The Preview must query the mapped user's `UserGroup` and compare it to the workflow stage's default assignee group.
  - If group validation fails, the row must be classified as `ManagerReview` or require explicit user acceptance before proceeding.
  - `CommitReady` (full, no warning) is allowed only when group membership is verified and matches.
- This decision does not change the design intent of Decision 1. It only defers the validation code to the phase where it is functionally needed.

### Decision 2 — Final statuses create Completed Workflow

For final statuses (מאושר תנועתית, מאושר תנועתית לאחר משטרה), the migration creates a Review Workflow in Completed state.

**Research findings — IsFinal auto-completion behavior:**

| Question | Answer |
|---|---|
| Does `StartAsync` with `initialStageCode = "REV.Completed"` auto-complete? | **NO.** `StartAsync` hardcodes `Status = Active`. Does NOT check `IsFinal`. |
| Is `REV.Completed` marked `IsFinal = true`? | **YES.** Confirmed in seed data. |
| Does `REV.Completed` have stage task templates? | **NO.** No templates and no `AssignedGroupId`. |
| What happens if you call `StartWorkflowAsync` at `REV.Completed`? | Instance created `Status = Active`, zero tasks, stuck. |
| Can `CompleteAsync` be called after `StartAsync`? | **YES.** Does not validate current stage. Sets `Status = Completed`. |

**Accepted approach:**
```
1. _engine.StartAsync(..., initialStageCode: "REV.Completed")
   → Creates instance: Status=Active, CurrentStageId=REV.Completed
2. _engine.CompleteAsync(instance.Id, userId, "Migrated from legacy Index Sheet — already approved", ct)
   → Sets Status=Completed, CompletedAtUtc=now
```

No open tasks are created. Reports are imported and linked to the project context. The completed workflow provides historical process context.

**Metadata for migrated workflows:**
- `TriggerType = System` (enum value 2)
- `Notes = "Migrated from legacy Index Sheet"` (or similar, with police-path indication for מאושר תנועתית לאחר משטרה)

### Decision 3 — Template establishes main structure; JSON provides detailed historical content

The Template may be used to establish the main report structure (Chapters and Sections at the X.Y level). The JSON is the source for the detailed historical sub-sections (at the X.Y.Z level) and their content. The migration populates those detailed sub-sections through the same existing mechanism used when a user manually adds them in the normal report UI.

**Research findings — Section vs Sub-section architecture:**

The report model has a strict two-tier structure:

| Level | Entity | Example | Created by | Scope |
|---|---|---|---|---|
| **Main sections** (X.Y) | `Section` (with parent `Chapter`) | "1.1", "3.6" | `TemplateSyncService.SyncAsync()` | Per `InspectionSeries` (template-level, shared across reports) |
| **Detailed sub-sections** (X.Y.Z) | `InspectionNote` rows with 3-level `NoteSubIndex` | "1.1.1", "1.1.2", "3.6.1" | `AddNoteAsync()` or `CreateReportAsync` snapshot | Per `InspectionReport` (report-level) |

**Key constraint**: `InspectionNote.SectionId` is a non-nullable `int` FK to `Section`. Every note must belong to a valid Section. But Sections are template-level — they are shared across all reports in a series.

**How the normal UI adds sub-sections:**
- `FloatingInspectionViewModel.AddNoteToSection()` counts existing sub-notes for the section and auto-increments: `NoteSubIndex = "{X.Y}.{count+1}"` (e.g., "1.1.3").
- Calls `InspectionReportService.AddNoteAsync(reportId, sectionId, subIndex)` which creates a plain `InspectionNote` row. No new `Section` entity is ever created by the UI.
- `CreateReportAsync` creates one initial placeholder note per section with `NoteSubIndex = "X.Y.1"`.
- Additional notes are added manually or carried over from previous reports.

**Unique constraint**: `(ReportId, SectionId, NoteSubIndex)` — each report can have at most one note per section+subindex combination.

**TemplateSyncService scope**: Operates on main sections only (X.Y level). Parses "chapter.sub" format (two levels). Creates/updates `Chapter` and `Section` entities. Does NOT create `InspectionNote` rows. Does NOT handle 3-level sub-sections.

**Correct migration approach:**
1. Use `TemplateSyncService.SyncAsync()` with `TemplateSyncRow` data to create/ensure main sections (Chapters + Sections at X.Y level) for the InspectionSeries. The rows can be constructed from JSON section codes (extracting the chapter number and section sub-code) or from the Google Sheet template if available.
2. Call `InspectionReportService.CreateReportAsync()` which creates one placeholder note ("X.Y.1") per section.
3. For each JSON `ExtractedSectionData` entry, populate the corresponding note or add additional notes via `AddNoteAsync()`:
   - First sub-note for a section → update the placeholder note created by `CreateReportAsync`.
   - Additional sub-notes for the same section → add via `AddNoteAsync(reportId, sectionId, nextSubIndex)`.
   - Set `NoteText`, `NoteStatusId`, `PlannerResponseText`, etc. from JSON data.

### Decision 4 — Existing Review Workflow handling

If a project already has a Review Workflow:

| Scenario | Action | Commit eligible? |
|---|---|---|
| No existing workflow | Create new | ✅ Yes |
| One Active workflow, same stage as Sheet target | `Already Up To Date` — skip | ❌ Skip |
| One Active workflow, earlier stage (forward movement) | Propose advancing to Sheet target stage | ✅ Yes — if no other conflicts |
| One Active workflow, later stage (backward movement) | `Conflict / Manager Review` | ❌ No |
| One Completed workflow | `Already Completed` — skip | ❌ Skip |
| Multiple Review Workflows | `Manager Review` — show all | ❌ No |

**Clear forward advancement can be Commit Ready** as long as:
- The stage SortOrder comparison confirms forward movement.
- No other conflicts exist (no report version conflicts, project match is exact).
- Preview clearly shows: current stage, target stage, and proposed action.

**Backward movement or unclear state always requires Manager Review.**

**Research findings — workflow lookup:**
- `WorkflowQueryService.GetByProjectAsync(projectId, statusFilter?)` returns all instances with `WorkflowDefinition` navigation. Filter client-side by `WorkflowDefinitionId` for Review.
- Stage comparison: each `WorkflowStageDefinition` has `SortOrder` (20…140). Compare current vs target for forward/backward detection.

### Decision 5 — Existing report version conflict

If an `InspectionReport` version already exists:

| Scenario | Classification |
|---|---|
| Same `ReportNumber` + same `SentSpreadsheetId` | `Already Up To Date` |
| Same `ReportNumber` + different or missing `SentSpreadsheetId` | `Conflict / Manager Review` |
| `ReportNumber` exists but no JSON for comparison | `Already Exists — cannot verify` |
| `ReportNumber` does not exist | `Ready to import` |

Unique constraint `(ProjectId, SeriesId, ReportNumber)` prevents duplicates at DB level.

**Commit Ready Rows must exclude conflicts.**

---

## 4. Status mapping

| Index Sheet Status (Hebrew) | Target REV.\* Stage | Stage SortOrder | Latest Report State | Stage Task |
|---|---|---|---|---|
| בתהליך בדיקה | `REV.ProfessionalReview` | 50 | **Open** | `PerformProfessionalReview` (Reviewers) |
| נבדק- ממתין לבדיקה פנימית | `REV.AwaitingManagerApproval` | 60 | **Open** | `ApproveReviewReport` (ReviewManagers) |
| ממתין לתיקון הערות | `REV.AwaitingPlannerCorrections` | 70 | **Closed** | `TrackPlannerCorrections` (Reviewers) |
| ממתין לתיקון הערות משטרה | `REV.AwaitingPoliceCorrections` | 110 | **Closed** | `ForwardPoliceCommentsToPlanner` (PoliceLiaison) |
| בתהליך בדיקה הערות משטרה | `REV.AwaitingPoliceApproval` ⚠️ | 100 | **Open** | `TrackPoliceApproval` (PoliceLiaison) |
| נבדק- ממתין לתשובה מהרשויות | `REV.AwaitingPoliceApproval` | 100 | **Closed** | `TrackPoliceApproval` (PoliceLiaison) |
| מאושר תנועתית | `REV.Completed` | 140 | **Closed** | None (final — StartAsync + CompleteAsync) |
| מאושר תנועתית לאחר משטרה | `REV.Completed` | 140 | **Closed** | None (final — StartAsync + CompleteAsync) |

> **⚠️ Known semantic mismatch — בתהליך בדיקה הערות משטרה:**
> This status means "actively reviewing police comments", but `REV.AwaitingPoliceApproval` means "waiting for police approval". There is no exact existing stage for "active review of police comments" — the Review workflow does not distinguish between active review and waiting. `REV.AwaitingPoliceApproval` (sort 100) is the closest existing stage. If a future distinction is needed, it requires a workflow definition change, which is not approved as part of this migration design.

---

## 5. Report-to-Workflow relationship

### 5.1. No direct link exists

| Link type | Exists? |
|---|---|
| Direct FK (InspectionReport → WorkflowInstance) | ❌ No column or navigation property |
| Direct FK (WorkflowInstance → InspectionReport) | ❌ No column or navigation property |
| Direct FK (InspectionSeries → WorkflowInstance) | ❌ No column or navigation property |
| Service method linking them | ❌ No service explicitly connects them |

### 5.2. Indirect relationship through shared Project

The current relationship between reports and workflows is indirect:
- Both `InspectionReport` and `WorkflowInstance` have a `ProjectId` FK.
- The Review Workflow and Inspection Reports coexist under the same project context.
- After migration, a project has: InspectionSeries → Reports (with notes) AND WorkflowInstance (at correct stage).
- The connection is implicit through the shared project.

### 5.3. TaskLink infrastructure exists but is not wired

`TaskLinkEntityType` includes:
- `InspectionReport = 2` — designed to link a task to a report
- `InspectionNote = 3` — designed to link a task to a specific note
- `WorkflowInstance = 6` — links a task to a workflow

The designed but not-yet-implemented linkage path would be:
```
WorkflowInstance → (creates) Task → TaskLink(InspectionReport, Role=Source)
```
This would allow a workflow task to explicitly reference the report it relates to. No code currently creates this link.

### 5.4. Migration approach

For this migration, the indirect relationship through shared `ProjectId` is sufficient. Reports and workflows are created under the same project. The `FloatingInspectionView` and workflow dashboard both query by project, so they naturally show the relevant data.

If a direct report-to-workflow link is needed in the future, `TaskLink` with `LinkedEntityType = InspectionReport` is the designed mechanism. This is a **gap for a future decision**, not a migration blocker.

---

## 6. Existing mechanisms to reuse

| Mechanism | Service | Method | How migration uses it |
|---|---|---|---|
| Workflow start at arbitrary stage | `WorkflowTaskOrchestrator` | `StartWorkflowAsync(initialStageCode)` | Start Review workflow at inferred stage |
| Workflow completion | `WorkflowEngine` | `CompleteAsync(instanceId, userId, notes)` | Complete workflow for final statuses |
| Stage task provisioning | `WorkflowStageTaskProvisioningService` | `CreateStageTasksAsync` | Auto-creates stage task with `TaskLink` |
| Task reassignment | `TaskService` | `ReassignTask(taskId, newAssigneeId, changedByUserId)` | Assign Sheet reviewer to workflow task |
| User lookup | `TaskPriorityEngine` | `BuildUserLookupCachesAsync` | Resolve Sheet email/name to user ID |
| Main section creation | `TemplateSyncService` | `SyncAsync(IReadOnlyList<TemplateSyncRow>, seriesId)` | Create Chapters + Sections from JSON section codes |
| Report creation | `InspectionReportService` | `CreateReportAsync` | Create report with version number + note snapshot |
| Sub-note addition | `InspectionReportService` | `AddNoteAsync(reportId, sectionId, subIndex)` | Add detailed sub-notes from JSON |
| Report lock (sent) | `InspectionReportService` | `MarkReportAsSentAsync` | Lock historical versions as sent/closed |
| Alternative creation | `ProjectAlternativeService` | `CreateAsync` | Ensure Alternative "1" exists |
| Index Sheet reading | `IndexSheetReader` | `ReadAsync`, `ReadReportHyperlinksAsync` | Read Sheet data and hyperlinks |
| AI extraction | `GeminiExtractionService` | `ExtractWithAiAsync` | Extract report content |
| JSON cache | `ExtractionCacheService` | `SaveAsync`, `LoadAsync`, `Exists` | Persist/load extraction results |
| Workflow lookup | `WorkflowQueryService` | `GetByProjectAsync` | Check for existing workflows |
| Project resolution | `MigrationTaskService` | `ResolveProjectAsync` logic | 4-step project matching |

---

## 7. Gaps requiring future implementation

| # | Gap | Severity | Proposed future fix |
|---|---|---|---|
| 1 | **WorkPriority not updated on reassignment** | MEDIUM | Call `CompactAfterRemoval` for old assignee + `InsertInQueue` for new assignee in `ReassignTask` |
| 2 | **Same-group constraint on ReassignTask** | MEDIUM | May need bypass or relaxation for migration scenarios; or ensure Sheet reviewers are added to the correct group before migration |
| 3 | **No "Ensure Alternative" utility** | LOW | Add helper: query + conditional `CreateAsync` |
| 4 | **No WorkflowInstance reviewer field** | LOW | Reviewer captured on the task, not the workflow instance |
| 5 | **No direct report-to-workflow link** | LOW | Use `TaskLink(InspectionReport)` when needed; not a migration blocker |
| 6 | **StartAsync does not auto-complete for IsFinal** | LOW | Call `CompleteAsync` separately; consider adding IsFinal check to engine |
| 7 | **No direct ProjectId+DefinitionId workflow query** | LOW | Filter client-side or add query method |

---

## 8. Preview design

### 8.1. Read-only guarantee

The new migration Preview must be **truly read-only**:

- ❌ Must NOT write to DB (no TaskType, no Status, no ProjectTypeTaskType, no ProjectTypeStatus, no ProjectAssignment, no WorkflowInstance, no TaskLink, no InspectionReport, no InspectionSeries, no Chapter, no Section, no InspectionNote)
- ❌ Must NOT create Google Drive / Google Sheet files
- ✅ MAY read from DB (projects, existing workflows, existing reports, user groups, stage definitions)
- ✅ MAY read from JSON cache (extraction results)
- ✅ MAY read from Google Sheets API (Index Sheet)

### 8.2. Preview columns

| Column | Source | Purpose |
|---|---|---|
| Project Ref | Sheet `פרויקט` | Raw value from Sheet |
| Match Type | Resolution logic | `Exact Match` / `Likely Match` / `Multiple Candidates` / `No Match` |
| Resolved Project | DB query | Project name + ID |
| Sheet Status | Sheet `סטטוס` | Hebrew status label |
| Target Stage | Status mapping (§4) | e.g., `REV.ProfessionalReview` |
| Inspector | Sheet `בודק` | Name from Sheet |
| Inspector Match | User lookup (read-only) | `Resolved: UserName` / `Not Found` / `⚠️ Group mismatch` |
| Existing Workflow | DB query | `None` / `Active at REV.X` / `Completed` / `Multiple (conflict)` |
| Workflow Action | Logic | `Create New` / `Already Up To Date` / `Advance Forward` / `Conflict` |
| JSON Available | Cache check | `Yes (N versions)` / `No` / `Partial` |
| Existing Reports | DB query | `None` / `N versions exist` |
| Report Action | Comparison | `Import N versions` / `Add N new` / `Already Up To Date` / `Conflict` |
| Report State | §4 mapping | Latest report Open / Closed |
| Row Status | Classification | `Commit Ready` / `Manager Review` / `Conflict` / `Already Done` / `No Match` |
| Notes | Accumulated | Warnings, mismatches, missing data |

### 8.3. Row classification rules

| Classification | Criteria | Eligible for auto-commit? |
|---|---|---|
| **Commit Ready** | Exact project match + valid status mapping + single row per project + no conflicting workflow + no conflicting report versions + reviewer resolved with same-group OR no reviewer (group default acceptable) | ✅ Yes |
| **Commit Ready (with warning)** | Same as Commit Ready but missing JSON → workflow can be created, report content cannot be fully imported | ✅ Yes (warning shown) |
| **Manager Review** | Likely match, multiple candidates, multiple rows for same project, reviewer resolved but group mismatch (unless user accepts default), existing workflow needs advancing (user must confirm), backward status movement, insufficient Sheet data | ❌ No |
| **Conflict** | Existing workflow at incompatible stage, differing report versions, backward status movement | ❌ No |
| **Already Done** | Existing workflow at same or later stage with matching reports | ❌ No (skip) |
| **No Match** | Project not found in DB | ❌ No |

**Missing JSON rules:**
- Missing JSON does **not** automatically block workflow reconstruction if the Sheet has enough data (project + status → stage mapping is sufficient).
- Missing JSON means report content/history cannot be fully imported. The row can still be `Commit Ready (with warning)` for workflow-only creation.
- Missing JSON becomes `Manager Review` only when the Sheet data is also insufficient (missing status, missing project, etc.) or when the selected action requires report content.

**Forward advancement rules:**
- If the existing workflow is behind the Sheet status and movement is clearly forward (target `SortOrder` > current `SortOrder`), Preview may propose advancing.
- Clear forward advancement is `Commit Ready` if no other conflicts exist.
- Preview clearly shows: current stage → target stage → proposed action.
- Backward movement or unclear state requires `Manager Review`.

### 8.4. Phase 1 Preview UI — Tab 3 (added 23.06.2026)

`MigrationPocWindow` Tab 3 (`"📋 מיגרציית ביקורת מגיליון — Preview בלבד"`) is the read-only Preview UI for Phase 1. It does not appear in the legacy Tab 1 (Extraction) or Tab 2 (Task Generation).

**Step 1 — Reviewer scan and mapping:**

| Control | `x:Name` | Purpose |
|---|---|---|
| TextBox | `NewIndexSheetIdBox` | Input for Google Sheet ID or full URL |
| Button | `ScanReviewersButton` | Triggers `GetDistinctReviewersAsync` to scan the sheet for unique reviewer names |
| DataGrid | `ReviewerMappingGrid` | Displays each sheet reviewer name with a ComboBox to map to a system user |

**Step 2 — Preview build:**

| Control | `x:Name` | Purpose |
|---|---|---|
| Button | `BuildPreviewButton` | Triggers `BuildPreviewAsync` — enabled only after reviewer mapping is populated |
| TextBlock | `NewPreviewStatusLabel` | Displays progress/status messages during preview build |

**Step 3 — Results display:**

| Control | `x:Name` | Purpose |
|---|---|---|
| DataGrid | `NewPreviewGrid` | Read-only preview results grid (17 columns: row index, version, project, status, classification, reviewer, JSON, workflow, report, blocking reason, warnings, proposed actions, etc.) |
| TextBlock | `NewPreviewLogBox` | Scrollable diagnostic log output |

All controls are read-only in Phase 1. There is no enabled Commit action. A disabled Commit placeholder button is shown (`CommitPhase1Button`, `IsEnabled="False"`) to make clear that commit is not implemented in Phase 1. Double-clicking a preview row opens JSON cache content (if available) for inspection.

---

## 9. Commit design

### 9.1. Per-row commit sequence

**Step 1 — Ensure Alternative "1":**
- Query existing alternatives for the project.
- If no alternative named "1" exists → `ProjectAlternativeService.CreateAsync(projectId, "1", userId)`.

**Step 2 — Ensure InspectionSeries:**
- Query existing series for the project by template.
- If not exists → create `InspectionSeries` with `ProjectId`, `SeriesName`, `TemplateSpreadsheetId` from Sheet link.

**Step 3 — Build main section structure:**
- Collect all unique main section codes (X.Y level) from JSON across all versions.
- Build `TemplateSyncRow` list with chapter numbers and section codes.
- Call `TemplateSyncService.SyncAsync(rows, seriesId)` to create/ensure Chapters + Sections.
- If no JSON is available, skip this step (workflow-only creation).

**Step 4 — Import report versions (if JSON available):**
- For each version (ascending order, 1, 2, 3…):
  - Check if `InspectionReport` with matching `(SeriesId, ReportNumber)` already exists.
  - If exists and matches → skip.
  - If exists and conflicts → skip with conflict marker.
  - If not exists:
    - Call `InspectionReportService.CreateReportAsync(projectId, seriesId, inspectorName, inspectorId, templateUrl)`.
    - This creates one placeholder note ("X.Y.1") per section.
    - For each JSON `ExtractedSectionData` entry:
      - Resolve the parent `Section` from the section code (X.Y level).
      - First sub-note for a section → update the placeholder note's text, status, designer response.
      - Additional sub-notes for the same section → call `AddNoteAsync(reportId, sectionId, nextSubIndex)` and populate.
      - Numbering gaps: if JSON has "1.1.1" and "1.1.3" but not "1.1.2", create an empty placeholder note for "1.1.2" with no text (structural only).
    - For non-latest versions: call `MarkReportAsSentAsync()`. Set `SentSpreadsheetId` from the version's Google Sheet ID.
  - For the latest version: apply open/closed state per §4.

**Step 5 — Create/reconstruct Review Workflow:**
- Query existing `WorkflowInstance` for project + Review definition.
- Handle per Decision 4 (§3).
- If creating new:
  - For **active statuses**: `WorkflowTaskOrchestrator.StartWorkflowAsync(initialStageCode: mappedStageCode)` with `TriggerType = System`, `Notes = "Migrated from legacy Index Sheet"`.
  - For **final statuses**: `WorkflowEngine.StartAsync(initialStageCode: "REV.Completed")` then `CompleteAsync()`.
- If advancing existing: use `WorkflowEngine.AdvanceStageAsync()` (requires valid transition rule) or document as a gap if no rule exists for the skip.

**Step 6 — Reassign task to Sheet reviewer (active statuses only):**
- If Step 5 created or advanced a workflow with a provisioned task:
  - If Sheet reviewer resolved to a system user:
    - Call `TaskService.ReassignTask(taskId, reviewerUserId, migrationUserId)`.
    - If same-group constraint fails → report in Commit result: `"Reassignment failed: reviewer not in same group. Task assigned to group default."`.
  - If Sheet reviewer not resolved → keep group default, report in Commit result.

**Step 7 — Record migration metadata:**
- Log which rows were migrated, timestamps, user, outcomes per row.

### 9.2. Error handling

- Each row processed in its own `DbContext` scope.
- Failures in one row do not block other rows.
- Row-level errors reported in results with clear messages.

---

## 10. Report section import — detailed design

### 10.1. Two-tier architecture

```
InspectionSeries (per project, one for migration)
  └── Chapter (SeriesId + ChapterNumber) — template-level, shared across reports
        ├── ChapterName (shared dictionary)
        └── Section (ChapterId + SectionCode) — template-level, shared across reports
              ├── SectionName (shared dictionary)
              └── InspectionNote (SectionId FK — required, non-nullable) — per report
                    ├── NoteSubIndex = "X.Y.1", "X.Y.2", "X.Y.3"... — per sub-note
                    └── InspectionReport (ReportId FK) — per report version
```

**Sections** (X.Y) are template-level entities scoped to `InspectionSeries`. They persist across all reports in a series. Created by `TemplateSyncService`.

**Sub-sections** (X.Y.Z) are `InspectionNote` rows scoped to a specific `InspectionReport`. Each is an individual finding/item. Created by `CreateReportAsync` (initial placeholder) or `AddNoteAsync` (manual addition).

### 10.2. Main section creation from JSON

Extract main section codes from JSON:
```
JSON SectionCode "3.6" → ChapterNumber = 3, SectionSubCode = 6
JSON ChapterTitle "תנועה" → ChapterName
JSON SectionTitle "חניה [גישה]" → SectionName
→ TemplateSyncRow { ChapterNumber=3, SectionCode="3.6", SectionTitle="חניה [גישה]" }
→ TemplateSyncService.SyncAsync(rows, seriesId)
→ Creates Chapter 3 + Section (Code=6) for the series
```

### 10.3. Sub-note population from JSON

For each report version, after `CreateReportAsync` creates placeholder notes:

| JSON entry | Maps to |
|---|---|
| First sub-note for section X.Y (e.g., SectionCode "1.1", NoteSubIndex "1.1.1") | Update the placeholder note "X.Y.1" created by `CreateReportAsync` |
| Second sub-note for section X.Y (e.g., NoteSubIndex "1.1.2") | Call `AddNoteAsync(reportId, sectionId, "1.1.2")` |
| Third sub-note (e.g., NoteSubIndex "1.1.3") | Call `AddNoteAsync(reportId, sectionId, "1.1.3")` |

Note content population:
- `NoteText` ← `ExtractedSectionData.NoteText`
- `NoteStatusId` ← resolve `StatusKey` via `InspectionNoteStatus` lookup
- `NoteStatus` ← `"OK"` if `IsResolved`, else null or status text
- `PlannerResponseText` ← `DesignerResponse`
- `NoteSubIndex` ← from JSON or auto-generated sequential

### 10.4. Numbering gaps

If JSON has "1.1.1" and "1.1.3" but not "1.1.2":
- Create an empty placeholder `InspectionNote` for "1.1.2" via `AddNoteAsync(reportId, sectionId, "1.1.2")`.
- Leave `NoteText` empty. Leave `NoteStatus` null.
- This is structural only — no invented content.
- Ensures the report UI (`FloatingInspectionView`) can open and operate correctly.
- The UI's `ReindexSectionNotesAsync` can later renumber if the user deletes the placeholder.

### 10.5. Report lifecycle states

| Version | Is Latest? | Status requires open? | Action |
|---|---|---|---|
| Non-latest | No | N/A | `MarkReportAsSentAsync()` → `IsLockedAfterSend = true` |
| Latest | Yes | Yes (בתהליך בדיקה, etc.) | Leave as-is (not sent, not locked) |
| Latest | Yes | No (ממתין לתיקון, מאושר, etc.) | `MarkReportAsSentAsync()` → `IsLockedAfterSend = true` |

### 10.6. FloatingInspectionView compatibility

Confirmed: the view queries notes by `ReportId`, navigates to `Section → Chapter → ChapterName/SectionName`. Notes with `NoteSubIndex` having ≥2 dots (e.g., "1.1.1") are displayed as sub-notes under their section. The UI does not care how sections or notes were created — it renders whatever the navigation properties provide. Imported JSON-based reports will display correctly.

---

## 11. JSON cache design

### 11.1. Current state

- **Root**: `%APPDATA%\SiNet\ExtractionCache\`
- **Structure**: `{root}\{projectNumber}\R{reportNumber}_V{versionIndex}.json`
- **One JSON per report version**
- **Envelope**: `ExtractionCacheEnvelope` with `ProjectNumber`, `ReportNumber`, `VersionIndex`, `TemplateSpreadsheetId`, `ReportSpreadsheetId`, `ExtractedAtUtc`, `Sections`, `GeneralFields`, `Warnings`

### 11.2. Proposed extensions (design only)

| Feature | Design |
|---|---|
| **Default local folder** | Keep `%APPDATA%\SiNet\ExtractionCache\`. Allow override via `appsettings.json` key. |
| **Export to ZIP** | Zip entire tree with manifest JSON. |
| **Import from ZIP** | Extract to cache folder. Skip existing files. |
| **Validation** | Verify deserialization + required fields. Do not blindly trust. |

---

## 12. Workflow reconstruction design

### 12.1. For active statuses

```
1. WorkflowTaskOrchestrator.StartWorkflowAsync(
       definitionId, projectId, TriggerType.System, null, userId,
       "Migrated from legacy Index Sheet", ct,
       initialStageCode: mappedStageCode
   )
   → Creates WorkflowInstance (Status=Active, CurrentStageId=target)
   → Provisions stage task via CreateStageTasksAsync
   → TaskLink created automatically

2. If Sheet reviewer resolved and same-group:
   TaskService.ReassignTask(taskId, reviewerUserId, migrationUserId)
```

### 12.2. For final statuses

```
1. WorkflowEngine.StartAsync(..., initialStageCode: "REV.Completed")
   → Status=Active, CurrentStageId=REV.Completed, 0 tasks
2. WorkflowEngine.CompleteAsync(instance.Id, userId, "Migrated — already approved", ct)
   → Status=Completed, CompletedAtUtc=now
```

### 12.3. Historical transitions

Not reconstructed individually. Single `WorkflowStageTransition` entry: `FromStageId = null → target`. The JSON content and report versions capture the historical record. The workflow provides the current/final state context.

---

## 13. Manager Review cases

| Case | Trigger | Preview display |
|---|---|---|
| Multiple Sheet rows for same project | Duplicate project detection | All rows shown with duplicate warning |
| Project = Likely / Multiple candidates | Non-exact resolution | Show candidates with match type |
| Project = No Match | Not in DB | `❌ No Match — project must be created first` |
| Inspector not found | User lookup failure | `⚠️ Inspector not resolved — group default will be used` |
| Inspector group mismatch | Same-group constraint | `⚠️ Reviewer not in same group — reassignment may fail` |
| Existing workflow backward movement | Target SortOrder < current | `⚠️ Status moved backward — requires confirmation` |
| Multiple existing Review Workflows | Duplicate workflows | `⚠️ Multiple workflows — select which to use` |
| Existing report version differs | SpreadsheetId mismatch | Show existing vs JSON details |
| Insufficient Sheet data | Missing status or project | `❌ Insufficient data — cannot determine action` |

---

## 14. Incremental rerun behavior

| Scenario | Result |
|---|---|
| Unchanged project (same status, same versions) | `Already Up To Date` — skip |
| Sheet status moved forward | Propose advancing workflow — `Commit Ready` if clear forward |
| New report versions in JSON | Add only new versions |
| Existing matching versions (same SpreadsheetId) | `Already Up To Date` |
| Existing differing versions | `Conflict` — Manager Review |
| Backward status movement | `Conflict` — Manager Review |
| New project appeared in Sheet | Normal processing |
| Project removed from Sheet | Not detected — no deletion |
| Missing JSON, Sheet data sufficient | `Commit Ready (with warning)` — workflow-only creation |
| Missing JSON, Sheet data insufficient | `Manager Review` |

---

## 15. Technical checks required before implementation

| # | Check | Question | Status |
|---|---|---|---|
| 1 | **Task reassignment for workflow tasks** | Does `ReassignTask` work for tasks created by workflow provisioning? | To verify |
| 2 | **Same-group constraint** | Are Sheet reviewers members of the `Reviewers` UserGroup? | To verify per environment |
| 3 | **CompleteAsync after StartAsync** | Call sequence for final stages. Verify Status + timestamps. | Designed; to verify in code |
| 4 | **Final stage no tasks** | Confirm `StartWorkflowAsync` at `REV.Completed` returns empty task list without throwing. | Designed; to verify |
| 5 | **TemplateSyncService from JSON** | Construct `TemplateSyncRow` from JSON section codes; call `SyncAsync`. Verify Chapters/Sections. | To verify |
| 6 | **CreateReportAsync with synced sections** | After `SyncAsync`, call `CreateReportAsync`. Verify placeholder notes. | To verify |
| 7 | **AddNoteAsync for sub-notes** | After `CreateReportAsync`, call `AddNoteAsync` for additional sub-notes. Verify in UI. | To verify |
| 8 | **Placeholder empty notes** | Create empty notes for numbering gaps. Verify `FloatingInspectionView` handles them. | To verify |
| 9 | **MarkReportAsSentAsync without export** | Call with migration `SentSpreadsheetId`. Verify lock behavior. | To verify |
| 10 | **Existing workflow lookup** | Query + filter by Review definition. Verify accuracy. | To verify |
| 11 | **Stage SortOrder comparison** | Verify forward/backward detection is consistent with mapping. | To verify |
| 12 | **בתהליך בדיקה הערות משטרה target** | Confirm `REV.AwaitingPoliceApproval` is closest. | Verified ⚠️ known semantic mismatch |
| 13 | **Read-only Preview** | Verify all preview queries can run without DB writes. | To verify |
| 14 | **Workflow advancement for existing** | Can `AdvanceStageAsync` skip stages? Does it require transition rules? | To verify |

---

## 16. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Wrong stage mapping from Hebrew status | MEDIUM | Closed mapping table (§4); Preview shows proposed stage |
| Duplicate workflows per project | MEDIUM | Logical guard in Preview + Commit |
| Same-group constraint blocks reassignment | MEDIUM | Preview warning; fallback to group default with visibility |
| Police-comments semantic mismatch | LOW | Documented; acceptable for now |
| Section mismatch between JSON versions | MEDIUM | Union all sections across versions; use latest title |
| Placeholder notes confuse users | LOW | Clearly empty; UI handles gracefully |
| Missing JSON → incomplete report history | MEDIUM | Preview warning; workflow-only creation allowed |
| WorkPriority inconsistency after reassignment | LOW | Known gap; fix in future phase |
| JSON cache corruption | LOW | Validation step on import |

---

## 17. Dropped / cancelled / postponed

| Item | Status |
|---|---|
| Existing open DB task migration as main source | **Postponed** — background reference only |
| Direct implementation before documentation approval | **Postponed** |
| Changing Google Sheet schema | **Postponed** |
| Creating parallel report mechanism | **Cancelled** |
| Requiring modern Template to contain every historical detailed sub-section | **Dropped** — Template creates main sections; JSON provides sub-note content |
| Inventing missing section content | **Not approved** |
| Creating migration-only manual tasks instead of Workflow tasks | **Cancelled** |
| Temporarily changing group default assignee for migration | **Cancelled** |
| Silent overwrite of report versions | **Not approved** |
| Moving Workflow backwards automatically | **Not approved** |
| JSON storage in DB or Google Drive | **Cancelled** — local filesystem only |
| DB migrations | **Not approved** in this phase |
| Code changes in design phase | **Historical** — Code changes were not approved in the original design-only session (21.06.2026). Phase 1 Preview code was subsequently implemented on 22.06.2026 and is tracked in §2.1 above. |
| Deleting or disabling old migration path | **Not approved** |
| Queue frequency design (daily/weekly follow-up) | **Postponed** |
| Adding fallback follow-up tasks | **Not approved** |
| Adding new workflow stage for police-comments review | **Not approved** — existing stage used with documented mismatch |

---

## 18. Open questions

1. **Same-group constraint relaxation:** If the Sheet reviewer is not in the same `UserGroup` as the workflow default assignee, should the migration bypass the same-group constraint? Or should reviewers be added to the correct group before migration?

2. **Workflow advancement without transition rules:** Can an existing workflow be advanced from stage A to stage C (skipping B) via `AdvanceStageAsync` if no direct transition rule exists? If not, what mechanism should be used for migration advancement? This needs verification before the advancing-existing-workflow scenario can be implemented.

---

## 19. Out of Scope

- Modifying existing workflow engine behavior.
- Creating new workflow stages.
- Changing the Google Sheet Index Sheet structure.
- Migrating existing open DB tasks (separate analysis exists as background).
- Automating Manager Review resolution.
- Building a migration scheduling/queue system.
- Phase 2 (Report import) — see separate plan document.
- Phase 3 (Workflow reconstruction) — not yet planned.
- Group-membership validation code (deferred to Phase 3 per Decision 6).

---

## 20. No-code-change confirmation — historical note

> This section applied to the original documentation-only design session of **21.06.2026**.
> Phase 1 Preview code was subsequently implemented on **22.06.2026** and is tracked in the file table (§2.1 — "Phase 1 Preview files").
> The implementation includes: `GoogleSheetReviewMigrationPreviewService`, preview model/enum files, `MigrationPocWindow` Tab 3 XAML + code-behind handlers, and extensions to `IndexSheetReader.ReadReportHyperlinksAsync`.
> No DB schema changes, no migrations, no data imports, and no old mechanisms were deleted or disabled.
