# Phase 2 Technical Plan — Report Import from JSON Cache

- **Date:** 22.06.2026
- **Status:** Plan only — NOT yet implemented. Awaiting review and approval before starting.
- **Scope:** Import `InspectionReport` structures from existing JSON cache into the DB.  
  Phase 2 does **not** reconstruct workflows. Phase 3 handles Workflow reconstruction.
- **Prerequisites:** Phase 1 Preview is complete and validated (read-only; ✅ done).  
  JSON cache is populated and validated via ExtractionCacheService.

---

## 1. Phase 2 Goal

Phase 2 creates `InspectionSeries`, `InspectionReport`, and `InspectionNote` rows  
from the existing JSON extraction cache — using only existing services, no new service creation.

**What Phase 2 writes:**
- `InspectionSeries` (if not already present for the project + template)
- `Chapter` + `Section` rows (via `TemplateSyncService.SyncAsync`)
- `InspectionReport` rows (via `InspectionReportService.CreateReportAsync`)
- `InspectionNote` rows (via `AddNoteAsync` and placeholder update)
- `SentSpreadsheetId` set on historical versions (via `MarkReportAsSentAsync`)

**What Phase 2 does NOT write:**
- `WorkflowInstance` — Phase 3 only
- `ProjectAssignment` tasks — Phase 3 only
- `TaskLink` rows — Phase 3 only
- `ProjectAlternative` — handled in Phase 3 step 1 (see Design §9.1)
- Any modification to Google Sheets
- Any modification to the Index Sheet

---

## 2. Existing services to reuse

| Service | Method(s) | Phase 2 usage |
|---|---|---|
| `ExtractionCacheService` | `LoadAsync(projNum, versionIdx, reportNum)` | Load JSON per version |
| `ExtractionCacheService` | `Exists(projNum, versionIdx, reportNum)` | Guard before loading |
| `TemplateSyncService` | `SyncAsync(rows, seriesId)` | Create/ensure Chapters + Sections (X.Y level) |
| `InspectionReportService` | `CreateReportAsync(...)` | Create each report version |
| `InspectionReportService` | `AddNoteAsync(reportId, sectionId, subIndex)` | Add sub-notes beyond the first |
| `InspectionReportService` | `MarkReportAsSentAsync(reportId, spreadsheetId)` | Lock non-latest versions |
| `WorkflowQueryService` | `GetByProjectAsync(projectId)` | Read-only: check if series/reports exist (Phase 2 will query only) |
| `SiNetSQLDbContext` | EF read queries | Read project, series, section, existing report data |
| `GoogleSheetReviewMigrationPreviewService` | `BuildPreviewAsync` output | Drive the per-row work units |
| `IndexSheetReader` | Existing parsed result | Provide project number, reviewer info, report links |

### Services NOT created in Phase 2

- No new `*MigrationService`.
- All writes go through the above existing services.
- A new lightweight `ReportImportService` is acceptable **only** as a thin coordinator  
  that orchestrates the above services — no new DB logic.

---

## 3. Data flow per project row

```
Phase 1 Preview row (Commit Ready, report action ≠ Already Done)
	│
	▼
[A] Load JSON envelopes for all versions of this project/report from ExtractionCacheService
	│
	▼
[B] Ensure InspectionSeries exists (create if not)
	│
	▼
[C] Build TemplateSyncRow list from JSON section codes (X.Y extraction)
	│  Call TemplateSyncService.SyncAsync(rows, seriesId)
	▼
[D] For each version (ascending: V1, V2 ... Vn):
	│  ├── Check if InspectionReport (SeriesId + ReportNumber) already exists
	│  │      If exists + SentSpreadsheetId matches → skip (AlreadyUpToDate)
	│  │      If exists + mismatch → mark Conflict, skip
	│  │      If not exists →
	│  │          CreateReportAsync(...)
	│  │          Populate notes from JSON (see §4)
	│  │          If not latest version → MarkReportAsSentAsync(spreadsheetId)
	│  └── If latest version: apply open/closed per §4 status mapping
	│
	▼
[E] Log per-row result: versions created / skipped / conflicted
```

---

## 4. Note import logic

### 4.1 Section code resolution

JSON `ExtractedSectionData.SectionCode` is in the format `"X.Y.Z"` (e.g., `"1.1.3"`).

- Extract the `X.Y` parent key (e.g., `"1.1"`).
- Look up the existing `Section` entity by `(SeriesId, ChapterNumber=X, SubCode=Y)`.
- If the Section is not found → log warning, skip note, do not crash.

### 4.2 Placeholder note (created by CreateReportAsync)

`CreateReportAsync` creates one placeholder `InspectionNote` per Section with `NoteSubIndex = "X.Y.1"`.

For the **first sub-note** of a section from JSON (index `.1`):
- Update the existing placeholder note: set `NoteText`, `NoteStatusId`, `PlannerResponseText`, etc.
- Do NOT call `AddNoteAsync` for the first sub-note — update in place.

For **additional sub-notes** (index `.2`, `.3`, …):
- Call `AddNoteAsync(reportId, sectionId, nextSubIndex)` then set fields.

### 4.3 Numbering gaps

If JSON has `1.1.1` and `1.1.3` but not `1.1.2`:
- Create an empty placeholder `InspectionNote` for `1.1.2` with no text.
- This preserves structural integrity. The note text remains blank/unresolved.
- Log: `[Phase2] Gap note created: {sectionCode} {subIndex}`.

### 4.4 Report open/closed state

| Version | Action |
|---|---|
| Any non-latest version | `MarkReportAsSentAsync(reportId, reportSpreadsheetId)` — sets `IsSent = true`, `SentAtUtc`, `SentSpreadsheetId` |
| Latest version | Open or closed per §4 of Design doc (status → `REV.*` → open/closed flag from status mapping table) |
| Latest version, active status (ProfessionalReview / ManagerApproval / etc.) | Keep open (`IsSent = false`) |
| Latest version, closed status (AwaitingCorrections / Completed) | `MarkReportAsSentAsync` |

### 4.5 SentSpreadsheetId as duplicate guard

Before creating a report version:
1. Query `InspectionReport` for `(SeriesId, ReportNumber)`.
2. If found and `SentSpreadsheetId == envelope.ReportSpreadsheetId` → **AlreadyUpToDate**, skip.
3. If found and `SentSpreadsheetId != envelope.ReportSpreadsheetId` → **Conflict**, skip, log warning.
4. If not found → create.

This is the primary duplicate prevention mechanism. Do not invent additional hash/date comparison.

---

## 5. Multiple versions handling

Each project row from the Index Sheet can have N linked report versions (V1…Vn).

- Versions are processed in ascending order: V1 first, Vn last.
- Each version maps to one `InspectionReport` with `ReportNumber = versionIndex`.
- All versions share the same `InspectionSeries` and `Section` structure.
- TemplateSyncService is called **once per series** (not once per report).
- The section structure is built from the **union of all version JSON section codes**.
- Notes are imported per report, independently.

---

## 6. Alternative 1 handling

Per Design §9.1, Alternative "1" must exist before creating an `InspectionSeries`.  
Phase 2 defers this to Phase 3 (which does the full commit sequence including Alternative).  
However, if Phase 2 runs standalone (report-import-only mode):

- Query `ProjectAlternative` for the project where `Name == "1"`.
- If not found → call `ProjectAlternativeService.CreateAsync(projectId, "1", userId)`.
- If found → use it (do not create a duplicate).
- Log: `[Phase2] Alternative "1" ensured for project {projectId}`.

---

## 7. Missing JSON handling

If a preview row has `IsLatestVersion == true` and the JSON cache for a version is missing:

| Scenario | Phase 2 Action |
|---|---|
| No JSON for any version | Skip report import for this row. Log: `[Phase2] No JSON cache — report import skipped for project {projectNumber}`. Do not fail the row. |
| JSON missing for some versions but present for others | Import the versions that have JSON. Log missing versions. Mark the row result as `PartialImport`. |
| JSON invalid (parse failure) | Log the parse failure, skip that version. Continue with others. |

Phase 2 never blocks on missing JSON — it just skips and logs.

---

## 8. Rollback / undo strategy

**Phase 2 has no automatic rollback.**

| Scenario | Behavior |
|---|---|
| DB write fails mid-row | Log the failure. Mark that row as `Failed`. Continue with next row. |
| Multiple rows fail | Each row is independent. Failures are isolated. |
| Manual undo needed | Must be done via direct DB intervention or a future undo script. No automated rollback exists. |
| Safe to re-run? | **Yes, with the duplicate guard.** Re-running Phase 2 on the same data will skip rows where `SentSpreadsheetId` already matches (AlreadyUpToDate). New or partial rows will be re-attempted. |

**Mitigation:** Always run against a small selected subset first (see §9).

---

## 9. Subset testing strategy

Before running full import:

1. Filter the Phase 1 Preview result to 2–3 `CommitReady` rows with `HasJsonCache = true`.
2. Run Phase 2 only on those rows.
3. Verify in DB:
   - `InspectionReport` rows created with correct `ReportNumber`.
   - `InspectionNote` rows populated with text from JSON.
   - Non-latest reports have `IsSent = true` and `SentSpreadsheetId` set.
   - Latest report has correct open/closed state.
4. Verify in UI: `FloatingInspectionView` shows the imported report and notes correctly.
5. After validation, run full batch.

The UI for Phase 2 will include a "selected rows only" mode (checkboxes or row filter) to support this.

---

## 10. What is read-only until later phases

| Item | Status in Phase 2 |
|---|---|
| `WorkflowInstance` | 🔒 Read-only until Phase 3 |
| `ProjectAssignment` (tasks) | 🔒 Read-only until Phase 3 |
| `TaskLink` | 🔒 Read-only until Phase 3 |
| `WorkflowStageDefinition` | 🔒 Never modified by migration |
| `UserGroup` / `User` | 🔒 Never modified by migration |
| Google Sheets / Index Sheet | 🔒 Never modified by migration |
| `ProjectTypeTaskType` / `ProjectTypeStatus` | 🔒 Not used in new path |

---

## 11. Phase 2 UI location

Phase 2 import runs from the **Preview tab (Tab 2)**, not Tab 1.

Suggested button: **"ייבוא דוחות (Phase 2)"** — enabled only when preview rows are loaded.

Expected UI controls:
- `ImportReportsButton` — runs Phase 2 for all Commit Ready rows with JSON.
- `ImportReportsSelectedButton` — runs Phase 2 for selected rows only (subset test).
- Progress bar / log output in the shared `LogBox`.
- Result summary: rows imported / skipped / conflicted / failed.

The button is **disabled** until Phase 1 Preview is built and validated.  
The button is **read-only disabled** if no `CommitReady` rows with `HasJsonCache = true` exist.

---

## 12. What must be implemented before Phase 2 can start

| Prerequisite | Status |
|---|---|
| Phase 1 Preview works correctly | ✅ Done |
| JSON cache export/import/validate | ✅ Done (this session) |
| `ExtractionCacheService.LoadAsync` | ✅ Already exists |
| `InspectionReportService.CreateReportAsync` signature confirmed | ⏳ Needs verification of exact signature |
| `InspectionReportService.AddNoteAsync` signature confirmed | ⏳ Needs verification |
| `InspectionReportService.MarkReportAsSentAsync` signature confirmed | ⏳ Needs verification |
| `TemplateSyncService.SyncAsync` signature confirmed | ⏳ Needs verification |
| `ProjectAlternativeService.CreateAsync` signature confirmed | ⏳ Needs verification |
| Phase 2 technical plan reviewed and approved | ⏳ This document — awaiting approval |

---

## 13. Things explicitly postponed to Phase 3

- Workflow reconstruction (`WorkflowInstance` creation/advancement)
- Stage task provisioning (`WorkflowStageTaskProvisioningService`)
- Reviewer task reassignment (`TaskService.ReassignTask`)
- `ProjectAlternative` creation (unless Phase 2 runs standalone — see §6)
- Full commit sequence (Design §9.1 Steps 5–6)

---

## 14. Things explicitly NOT in scope (ever, in migration)

- Creating or modifying `ProjectTypeTaskType`
- Creating or modifying `ProjectTypeStatus`
- Old-model `ProjectAssignment` (standalone tasks, not workflow-linked)
- Changing `WorkflowStageDefinition` seed data
- Modifying Google Sheets or the Index Sheet
- Any automatic rollback mechanism
- Silent overwrite of JSON cache files
- Fallback reviewer assignment
