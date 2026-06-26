# Phase 2 Technical Plan — Report Import from JSON Cache

- **Date:** 22.06.2026
- **Status:** First slice implemented 26.06.2026. Revised 26.06.2026 for skipCarryOver, duplicate prevention, and validation defaults.
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
| `InspectionReportService` | `CreateReportAsync(..., skipCarryOver: true)` | Create each report version — **with skipCarryOver=true** to prevent carry-over from previous report |
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
	│  │          CreateReportAsync(..., skipCarryOver: true)
	│  │          Populate notes from JSON — only for sections matched in Template Compatibility Preview (see §4)
	│  │          Notes for unmatched sections → skip + log warning
	│  └── If latest version: leave validation gaps visible. If historical: apply minimal defaults (see §4.4).
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

### 4.2 Placeholder note and duplicate prevention (revised 26.06.2026)

`CreateReportAsync` (with `skipCarryOver: true`) creates one placeholder `InspectionNote` per Section with `NoteSubIndex = "X.Y.1"`. No notes are carried over from previous reports.

**Duplicate prevention:** Before any note creation, `ReportImportService` builds a lookup of ALL existing notes in the report:

```
Key: (SectionId, NoteSubIndex) → NoteId
```

Before each `AddNoteAsync` call:
- If `(SectionId, NoteSubIndex)` already exists in the lookup → **reuse the existing NoteId** for content update. Do NOT call `AddNoteAsync`.
- If not found → call `AddNoteAsync`, add to lookup, use the new NoteId.

This prevents the `IX_InspectionNotes_Report_Section_SubIndex` duplicate key error that occurred before this fix.

For the **first sub-note** of a section from JSON (index `.1`):
- Reuse the existing placeholder note (already in lookup).
- Set `NoteText`, `NoteStatusId`, `PlannerResponseText`, etc.

For **additional sub-notes** (index `.2`, `.3`, …):
- Call `EnsureNoteExistsAsync` (which checks lookup first, then `AddNoteAsync` if needed).

All note creation goes through existing services only. No direct DB inserts.

### 4.3 Numbering gaps (revised 26.06.2026)

If JSON has `1.1.3` but not `1.1.1` or `1.1.2`:

**Critical rule:** Do NOT write content from `X.Y.3` into the placeholder at `X.Y.1`.

- `1.1.1` — the placeholder remains empty (created by `CreateReportAsync`).
- `1.1.2` — a gap note is created (empty) via `EnsureNoteExistsAsync`.
- `1.1.3` — created via `EnsureNoteExistsAsync`, then filled with actual JSON content.

Interior gaps (e.g., JSON has `1.1.1` and `1.1.5` but not `1.1.2`–`1.1.4`) are also handled — empty gap notes are created for the missing indexes.

All gap note creation goes through the same duplicate-safe `EnsureNoteExistsAsync` helper.
Log: `[Phase2] Gap note created: {sectionCode}.{subIndex}`.

### 4.4 Validation defaults (revised 26.06.2026)

The previous rule "every imported report must pass Validation" is **cancelled**.

**New rule — two modes based on `IsLatestVersion`:**

| Report type | `IsLatestVersion` | Both NoteText AND StatusKey empty | NoteText present, StatusKey empty | Action |
|---|---|---|---|---|
| Historical (not latest) | `false` | Yes | — | Fill: `NoteText=" "`, `StatusKey="Passed"`, `NoteStatusId` from `GetActiveStatusOptionsAsync()` |
| Historical (not latest) | `false` | — | Yes | Do NOT assign "Passed". Log: `[Phase2] Missing status for non-empty note: ...` |
| Latest / active | `true` | Any | Any | **No defaults.** Leave gaps visible for manual review. |

**Why "Passed" for historical defaults (when both empty):**
- "Passed" (StatusKey="Passed", HebrewLabel="מקובל") means "accepted / no active issue".
- In historical reports that have been superseded, empty unmarked items are implicitly resolved.
- "Passed" StatusId is looked up dynamically via `GetActiveStatusOptionsAsync()`, not hardcoded.

**Why NOT "Passed" when text is present but status is empty:**
- Assigning "Passed" to a note with real text would mark a real finding as "accepted" — misleading.
- Instead, a warning is logged. If Validation requires a status, the issue is reported as a blocker.

**Sheet status based validation classification is postponed.**
Currently using `IsLatestVersion` as the sole criterion.

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

## 10a. Full report preview rendering rule (template-shaped) — decided 27.06.2026; extended to GeneralFields 28.06.2026

The full report preview (`FullReportFillPreviewWindow`) is **template-shaped**: it shows only the
sections/notes **and general header fields** that are eligible for import into the **selected target template**.
It represents the final intended report, **not** the raw JSON cache.

*Sections / notes:*

- The report body shows **only** sections that exist in the selected target template — JSON sections
  whose parent code is import-eligible (`SectionMatchResult.Matched`: code found **and** title/description compatible).
- A note is shown **only** when its parent section was matched in Template Compatibility Preview.
  A note is **never** imported or previewed by section number alone.
- JSON-only sections are **not** shown in the report body. They are shown **only** as skipped / warning items.
- Code-matched-but-title-mismatched sections are likewise excluded from the body and shown as skipped.
- Skipped JSON sections are **not deleted** — they stay visible in a separate warnings / skipped panel,
  each with a reason (missing in template / title mismatch / unrecognized code).
- Eligibility is single-sourced via `TemplateCompatibilityResult.IsImportEligible(parentSectionCode)`,
  so the preview body and any future Phase 2 import gate on the exact same rule.

*General fields (header fields / `<<tag>>` labels):*

- The general-fields panel shows **only** fields whose key (tag label) exists as an `IsGeneralTag`
  entry in the selected target template (`TemplateScanTag.GeneralTagLabel`).
- JSON general fields that do not exist in the target template are **not** shown in the report body.
  They are shown in a **separate skipped general fields diagnostics area** with an explanatory reason.
- Skipped general fields are **not deleted** — they are retained and shown separately.
- Eligibility is single-sourced via `TemplateCompatibilityResult.IsGeneralFieldEligible(key)`
  (backed by `ImportEligibleGeneralFieldKeys`), populated from `TemplateScanResult.AllTags` at scan time.
- If no target template is selected, all JSON general fields are shown (no filtering).

*Both rules:*

- The preview stays strictly **read-only**: no DB write, no extraction, no AI, no commit.

### Dropped / cancelled / postponed (preview scope)

| Item | Status |
|---|---|
| Showing the raw JSON cache as the "full report" | ❌ Cancelled — body is template-shaped |
| Showing sections that do not exist in the template inside the report body | ❌ Cancelled — moved to skipped/warnings panel only |
| Importing/previewing a note by section number alone | ❌ Cancelled — must pass Template Compatibility |
| Showing JSON-only GeneralFields as part of the report body | ❌ Cancelled — moved to skipped diagnostics area |
| Phase 2 import (writing reports/notes to DB) | ⏳ Not started in this change |
| DB writes from the preview | 🔒 Not approved at this stage |
| Deleting skipped sections or skipped general fields from the data | ❌ Cancelled — kept and shown as warnings only |

---

## 11. Phase 2 UI location

Phase 2 import runs from the **Google Sheet Review Migration Preview tab (Tab 3 / Preview tab)** in `MigrationPocWindow`. It does not run from Tab 1 (Extraction) or Tab 2 (Task Generation).

**Implemented (26.06.2026 — first slice):**

- `ImportReportsSelectedButton` — **"ייבוא דוחות נבחרים (Phase 2)"** — runs Phase 2 for selected rows only.
- `ImportStatusLabel` — shows import eligibility status and result summary.
- Log output in the shared `LogBox` via `AppendToLog`.

**Button enable conditions (all must be true):**
- Preview rows are loaded (`_lastPreviewRows` is populated).
- A target template is selected (`_selectedTemplate` is set).
- Template compatibility was performed (`_lastCompatibilityResults` is set).
- Template sync rows are available (`_lastTemplateSyncRows` is populated).
- At least one row is selected in the grid.
- Selected rows must have: `ResolvedProjectId`, `TemplateValidationStatus` ∈ {FullMatch, PartialMatch}, JSON cache available, and no blocking classification.

**Postponed:**
- `ImportReportsButton` (import all rows) — not yet implemented; first slice is selected-rows-only.

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
- Writing "מולא על ידי migration" or "Filled by migration" into note text

---

## 14a. skipCarryOver — Migration Import mode (added 26.06.2026)

Phase 2 Migration Import creates reports with `skipCarryOver: true`.

**When `skipCarryOver == true`, the following are NOT executed:**
- `CarryOverUnresolvedNotesAsync` — does not copy unresolved notes from the previous report.
- `CopyGeneralFieldsFromPreviousAsync` — does not copy Chapter 0 / general header fields from the previous report.
- `CopyReviewedFilesFromPreviousAsync` — does not copy reviewed plan file links from the previous report.

**Reason:** In migration, the sole source of truth is the JSON cache. If regular CarryOver ran, it would create notes that duplicate the JSON-sourced notes, causing the `IX_InspectionNotes_Report_Section_SubIndex` unique constraint violation.

**What still runs (not skipped):**
- Report creation with auto-numbered `ReportNumber`
- Placeholder note creation for all active sections (the "snapshot")
- Transaction commit + diagnostic logging

**Important:**
- In regular (non-migration) report creation, `skipCarryOver` remains `false` (default).
- CarryOver is NOT deleted — it is only neutralized in Migration Import mode.
- Only `ReportImportService` passes `skipCarryOver: true`.

---

## 14b. Auto-fill and Chapter 0 / GeneralFields (added 26.06.2026)

In Migration Import, automatic field population from the current system state is not desired.
The goal is to preserve what was in the historical report per the JSON cache.

**Auto-fill mechanisms in the system:**

| Mechanism | Location | Neutralized by skipCarryOver? |
|---|---|---|
| Carry-over of unresolved notes | `CarryOverUnresolvedNotesAsync` | ✅ Yes |
| Copy Chapter 0 / general fields from previous | `CopyGeneralFieldsFromPreviousAsync` | ✅ Yes |
| Copy reviewed file links from previous | `CopyReviewedFilesFromPreviousAsync` | ✅ Yes |
| `InspectionDate = DateTime.UtcNow` | `CreateReportAsync` line 133 | NOT skipped — import timestamp is acceptable |
| UI auto-values (ישוב, שם פרויקט, etc.) | `FloatingInspectionVM.BuildInspectionTree` | N/A — UI display-time only, not saved during creation |

**There is no `Place` field on `InspectionReport` model.** Place/location comes from `Project.Place.Title` at UI display time and is shown in Chapter 0 general field labeled "ישוב" / "רשות מקומית". This auto-fill is UI-level only.

**Chapter 0 / GeneralFields import:**
- NOT implemented in this slice.
- No fuzzy matching.
- No new mechanism for general fields.
- If approved in the future, this will be a separate slice.

---

## 14c. Required tests after implementation (added 26.06.2026)

### V1 import
- Report created.
- CarryOver NOT activated (`skipCarryOver=true`).
- Notes come from JSON only.
- No duplicate key error.

### V2 import
- Report #2 created only if V1 exists in the series.
- No notes carried over from V1.
- Notes come from JSON only.
- No duplicate key error.

### Re-run same row
- Same row identified as `AlreadyExists`.
- No duplicate report created.
- No duplicate notes created.

### Gap notes
- If JSON starts from `X.Y.3`, content does NOT go into `X.Y.1`.
- Gap notes created only where needed.
- All gap notes created via `EnsureNoteExistsAsync` (duplicate-safe).

### Validation defaults
- Historical report: defaults applied only when **both** text AND status are empty.
- Historical report: note with text but missing status → warning logged, NOT auto-assigned "Passed".
- Latest/active report: no defaults, Validation gaps remain visible.

### Auto-fill / Chapter 0
- Chapter 0 / GeneralFields NOT copied from previous report in migration mode.
- No auto-fill replaces JSON values.

---

## 14d. Dropped / cancelled / postponed (דברים שירדו / בוטלו / הושהו)

| Item | Status |
|---|---|
| CarryOver mechanism | NOT deleted — neutralized only in Migration Import via `skipCarryOver: true` |
| Auto-fill / CopyGeneralFields | NOT deleted — not executed in Migration Import via `skipCarryOver: true` |
| Writing "מולא על ידי migration" in note text | ❌ Cancelled — text may appear in official reports |
| Validation defaults for every imported report | ❌ Cancelled — only for historical reports with both text AND status empty |
| Hiding Validation in latest/active report | ❌ Cancelled — gaps remain visible |
| Assigning "Passed" when note has text but missing status | ❌ Cancelled — would misleadingly mark findings as accepted |
| Sheet status based validation classification | ⏳ Postponed — currently using `IsLatestVersion` only |
| GeneralFields / Chapter 0 import | ⏳ Postponed |
| Fuzzy matching for general fields | ⏳ Not approved |
| Import all rows (broad button) | ⏳ Postponed — first slice is selected-rows-only |
| `MarkReportAsSentAsync` / version locking | ⏳ Postponed |
| Workflow / Task creation | ⏳ Phase 3 |
| Google Sheets / Index Sheet writeback | 🔒 Not in scope |
| Automatic rollback | ⏳ Not approved |
| Deleting test reports | Requires explicit user decision |
| New DB status | Not added |
| DB migration / schema change | Not added |
| New DB field for migration source marking | Not added |
