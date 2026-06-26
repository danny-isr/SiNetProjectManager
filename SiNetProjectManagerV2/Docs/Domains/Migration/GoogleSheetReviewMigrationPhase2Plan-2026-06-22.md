# Phase 2 Technical Plan — Report Import from JSON Cache

- **Date:** 22.06.2026
- **Status:** Plan only — Revised 26.06.2026 to align with user decisions. NOT yet implemented.
- **Scope:** Import `InspectionReport` structures from existing JSON cache into the DB.  
  Phase 2 does **not** reconstruct workflows. Phase 3 handles Workflow reconstruction.
- **Prerequisites:** Phase 1 Preview code is implemented (read-only). Functional testing against real Google Sheet data is pending.  
  JSON cache is populated and validated via ExtractionCacheService.

---

## 1. Phase 2 Goal

Phase 2 creates `InspectionSeries`, `InspectionReport`, and `InspectionNote` rows  
using **two sources** and only existing services, no new service creation:

- **Target structure (Chapters, Sections):** User-selected target template (via `GoogleInspectionTemplateProvider.ScanAndParseTemplateAsync`).
- **Historical content (notes, sub-notes):** JSON extraction cache (via `ExtractionCacheService.LoadAsync`).

The JSON cache is **not** the source of Chapters or Sections. It provides only the report-level note content.

**What Phase 2 writes:**
- `InspectionSeries` (if not already present for the project + template)
- `Chapter` + `Section` rows (via `TemplateSyncService.SyncAsync` with **user-selected target template** — not from JSON)
- `InspectionReport` rows (via `InspectionReportService.CreateReportAsync`)
- `InspectionNote` rows (via `AddNoteAsync` and placeholder update — only for notes whose parent section was matched in Template Compatibility Preview)

**What Phase 2 does NOT write (first slice):**
- `SentSpreadsheetId` / `MarkReportAsSentAsync` — postponed beyond first Phase 2 slice

**What Phase 2 does NOT write (ever):**
- `WorkflowInstance` — Phase 3 only
- `ProjectAssignment` tasks — Phase 3 only
- `TaskLink` rows — Phase 3 only
- `ProjectAlternative` — not required for report import (no FK from InspectionSeries/InspectionReport to ProjectAlternative)
- Any modification to Google Sheets
- Any modification to the Index Sheet
- Sections/Chapters derived from JSON (sections come only from user-selected template)

---

## 2. Existing services to reuse

| Service | Method(s) | Phase 2 usage |
|---|---|---|
| `ExtractionCacheService` | `LoadAsync(projNum, versionIdx, reportNum)` | Load JSON per version |
| `ExtractionCacheService` | `Exists(projNum, versionIdx, reportNum)` | Guard before loading |
| `TemplateSyncService` | `SyncAsync(rows, seriesId)` | Create/ensure Chapters + Sections from **user-selected target template** (not from JSON) |
| `GoogleInspectionTemplateProvider` | `ScanAndParseTemplateAsync(spreadsheetId)` | Read target template structure for section creation |
| `InspectionReportService` | `CreateReportAsync(...)` | Create each report version |
| `InspectionReportService` | `AddNoteAsync(reportId, sectionId, subIndex)` | Add sub-notes beyond the first |
| `InspectionReportService` | `MarkReportAsSentAsync(reportId, spreadsheetId)` | Lock non-latest versions — **postponed beyond first Phase 2 slice** |
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
Phase 1 Preview row (Commit Ready, template validated, report action ≠ Already Done)
	│
	▼
[A] Load JSON envelopes for all versions of this project/report from ExtractionCacheService
	│
	▼
[B] Ensure InspectionSeries exists (create if not, using user-selected target template SpreadsheetId)
	│
	▼
[C] Sync section structure from user-selected target template (revised 26.06.2026)
	│  Read template via GoogleInspectionTemplateProvider.ScanAndParseTemplateAsync(templateSpreadsheetId)
	│  Call TemplateSyncService.SyncAsync(templateRows, seriesId)
	│  Do NOT build TemplateSyncRow from JSON section codes
	▼
[D] For each version (ascending: V1, V2 ... Vn):
	│  ├── Check if InspectionReport (SeriesId + ReportNumber) already exists (primary guard)
	│  │      If exists → skip (AlreadyExists). SentSpreadsheetId is secondary check — see §4.5
	│  │      If not exists →
	│  │          CreateReportAsync(...)
	│  │          Populate notes from JSON — only for sections matched in Template Compatibility Preview (see §4)
	│  │          Notes for unmatched sections → skip + log warning
	│  └── If latest version: apply open/closed per §4 status mapping
	│
	▼
[E] Log per-row result: versions created / skipped / conflicted / notes skipped due to template mismatch
```

> **Note (26.06.2026):** `MarkReportAsSentAsync` for non-latest versions is postponed beyond the first Phase 2 slice.

---

## 4. Note import logic

### 4.1 Section code resolution

JSON `ExtractedSectionData.SectionCode` is in the format `"X.Y.Z"` (e.g., `"1.1.3"`).

- Extract the `X.Y` parent key (e.g., `"1.1"`).
- Look up the existing `Section` entity by `(SeriesId, ChapterNumber=X, SubCode=Y)` — the Section must have been created from the **user-selected target template** (Step C), not from JSON.
- If the Section is not found (JSON references a section code that does not exist in the target template) → **skip that note**, log warning: `[Phase2] Section {code} not found in target template — note skipped`. Do not crash. Do not create a new Section from JSON.

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
| Any non-latest version | `MarkReportAsSentAsync` **postponed** beyond first Phase 2 slice. In the first slice, non-latest versions are created but not locked. |
| Latest version | Open or closed per §4 of Design doc (status → `REV.*` → open/closed flag from status mapping table) |
| Latest version, active status (ProfessionalReview / ManagerApproval / etc.) | Keep open (`IsSent = false`) |
| Latest version, closed status (AwaitingCorrections / Completed) | `MarkReportAsSentAsync` — **postponed** beyond first Phase 2 slice |

### 4.5 Duplicate guard (revised 26.06.2026)

Before creating a report version:
1. Query `InspectionReport` for `(SeriesId, ReportNumber)` — this is the **primary duplicate guard**.
2. If found → the report already exists. Do **not** create a duplicate.
   - If `SentSpreadsheetId` is set and matches `envelope.ReportSpreadsheetId` → **AlreadyUpToDate**, skip.
   - If `SentSpreadsheetId` is set and does not match → **Conflict**, skip, log warning.
   - If `SentSpreadsheetId` is null (first Phase 2 slice — `MarkReportAsSentAsync` was not yet called) → treat as **AlreadyExists**, skip. Do not overwrite.
3. If not found → create.

**First Phase 2 slice note:** Because `MarkReportAsSentAsync` is postponed, `SentSpreadsheetId` may be null on already-imported reports. The primary guard `(SeriesId, ReportNumber)` must be sufficient on its own. `SentSpreadsheetId` comparison is an **additional** check that becomes stronger after `MarkReportAsSentAsync` is approved in a later phase.

---

## 5. Multiple versions handling

Each project row from the Index Sheet can have N linked report versions (V1…Vn).

- Versions are processed in ascending order: V1 first, Vn last.
- Each version maps to one `InspectionReport` with `ReportNumber = versionIndex`.
- All versions share the same `InspectionSeries` and `Section` structure.
- `TemplateSyncService` is called **once per series** (not once per report).
- The section structure comes from the **user-selected target template** — not from JSON section codes.
- Notes are imported per report, independently. Only notes whose parent section exists in the target template are imported.

---

## 6. Alternative 1 handling (revised 26.06.2026)

`ProjectAlternative` is **not required** for Phase 2 report import.

- `InspectionSeries` does not have a FK to `ProjectAlternative`.
- `InspectionReport` does not have a FK to `ProjectAlternative`.
- `ProjectAlternative` is used for reviewed files and per-note file linkage context (`InspectionNote.LinkedAlternative`, `InspectionReportReviewedFile.Alternative`) — both are string-based, not FK-based.
- Phase 2 does not create or require `ProjectAlternative`.
- Alternative handling may be addressed in a future phase if reviewed files / per-note file linkage is needed.

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

## 8. Rollback / cleanup strategy (revised 26.06.2026)

**Phase 2 does not claim true single-transaction atomicity** — existing services may commit internally.

| Scenario | Behavior |
|---|---|
| DB write fails mid-row | Log the failure. Mark that row as `Failed`. Continue with next row. |
| Multiple rows fail | Each row is independent. Failures are isolated. |
| Manual undo needed | Must be done via direct DB intervention or a future undo script. No automated rollback exists. If a report is created and note population fails, safe compensating cleanup may be attempted only if the created report is still latest and deletion is safe. |
| Safe to re-run? | **Yes, with the duplicate guard.** Re-running Phase 2 on the same data will skip rows where `(SeriesId, ReportNumber)` already exists. `SentSpreadsheetId` comparison is used as an additional check when available. New or partial rows will be re-attempted. |

**Mitigation:** Always run against a small selected subset first (see §9).

---

## 9. Subset testing strategy

Before running full import:

**Suggested first controlled slice:**
- One controlled project/report group, for example: ProjectNumber 3016, ReportNumber 2, Versions 1 and 2.
- Template Compatibility Preview must show `FullMatch` or `PartialMatch` for the selected rows.
- Phase 2 first slice does NOT call: `MarkReportAsSentAsync`, workflow creation, task creation, `TaskLink` creation, reviewer reassignment, `ProjectAlternative` creation.

**Verification steps:**
1. Filter the Phase 1 Preview result to the controlled slice with `HasJsonCache = true` and `TemplateValidationStatus ∈ {FullMatch, PartialMatch}`.
2. Run Phase 2 only on those rows.
3. Verify in DB:
   - `InspectionReport` rows created with correct `ReportNumber`.
   - `InspectionNote` rows populated with text from JSON (only for matched sections).
   - Notes for unmatched sections were skipped (not imported).
   - Report numbering does not conflict with existing reports.
   - Latest report has correct open/closed state.
4. Verify in UI: `FloatingInspectionView` shows the imported report and notes correctly.
5. After validation, expand to full batch.

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

Phase 2 import runs from the **Google Sheet Review Migration Preview tab (Tab 3 / Preview tab)** in `MigrationPocWindow`. It does not run from Tab 1 (Extraction) or Tab 2 (Task Generation).

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
| `InspectionReportService.MarkReportAsSentAsync` signature confirmed | ⏳ Postponed — not in first Phase 2 slice |
| `TemplateSyncService.SyncAsync` signature confirmed | ⏳ Needs verification (called with user-selected template rows, not JSON) |
| `ProjectAlternativeService.CreateAsync` signature confirmed | N/A — not required for Phase 2 |
| Template Compatibility Preview completed for target rows | ⏳ Required before Phase 2 import |
| No `TemplateError` or `NoMatch` for target rows | ⏳ Required before Phase 2 import |
| Phase 2 technical plan reviewed and approved | ⏳ This document — awaiting approval |

---

## 13. Things explicitly postponed to Phase 3

- Workflow reconstruction (`WorkflowInstance` creation/advancement)
- Stage task provisioning (`WorkflowStageTaskProvisioningService`)
- Reviewer task reassignment (`TaskService.ReassignTask`)
- Reviewer group validation
- `ProjectAlternative` creation — not required for Phase 2 (no FK dependency)
- `MarkReportAsSentAsync` / historical version locking — postponed beyond first Phase 2 slice
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
