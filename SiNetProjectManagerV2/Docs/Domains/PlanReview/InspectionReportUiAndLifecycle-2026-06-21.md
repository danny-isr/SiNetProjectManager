# Inspection Report UI and Lifecycle

- **Date:** 21.06.2026
- **Status:** Active
- **Scope:** FloatingInspectionView, InspectionReportService, TemplateSyncService, report creation / versioning / export / locking lifecycle, section and sub-note architecture

---

## 1. Purpose

Document the Inspection Report UI and lifecycle as currently implemented. This document is the source of truth for how reports are created, edited, versioned, sent, locked, displayed, and related to sections/notes. It also describes the relationship to the Google Sheet Review Migration design.

---

## 2. Relevant screens and files

### UI files

| Component | File | Purpose |
|---|---|---|
| FloatingInspectionView (XAML) | `SiNetProjectManagerV2\WPFUserControl\FloatingInspectionView.xaml` (1355 lines) | Main report UI — tree view, create bar, action bar |
| FloatingInspectionView (code-behind) | `SiNetProjectManagerV2\WPFUserControl\FloatingInspectionView.xaml.cs` | Window management, WPF service injection |
| FloatingInspectionViewModel | `SiNetSQL\MVVM\FloatingInspectionViewModel.cs` (4973 lines) | All report logic — commands, tree building, save flow, export, planner response |
| InspectionTreeItems | `SiNetSQL\MVVM\InspectionTreeItems.cs` (791 lines) | Tree item models — Chapter, Section, Note, General Data |

### Service files

| Service | File | Purpose |
|---|---|---|
| InspectionReportService | `SiNetSQL\Services\InspectionSync\InspectionReportService.cs` (1714 lines) | Report CRUD, versioning, note management, carry-over, locking |
| TemplateSyncService | `SiNetSQL\Services\InspectionSync\TemplateSyncService.cs` (565 lines) | Template → Chapter + Section sync |
| GoogleReportExportService | `SiNetProjectManagerV2\Services\GoogleReportExportService.cs` (1887 lines) | Export report to Google Sheets |

### Model files

| Model | File | Key fields |
|---|---|---|
| InspectionReport | `SiNetSQL\Models\InspectionReport.cs` | ReportId, ProjectId, SeriesId, ReportNumber, InspectorId, InspectorName, SentAt, SentByUserId, SentSpreadsheetId, SentSpreadsheetUrl, IsLockedAfterSend, ReviewedVersion |
| InspectionSeries | `SiNetSQL\Models\InspectionSeries.cs` | SeriesId, ProjectId, SeriesName, TemplateSpreadsheetId |
| InspectionNote | `SiNetSQL\Models\InspectionNote.cs` | NoteId, ReportId, SectionId (non-nullable FK), NoteSubIndex, NoteText, NoteStatus, NoteStatusId, PlannerResponseText, PreviousNoteId, AccMarkupLink, LinkedFileName, LinkedAlternative, LinkedVersion |
| Section | `SiNetSQL\Models\Section.cs` | SectionId, ChapterId, SectionNameId, SectionCode, Version, IsActive |
| Chapter | `SiNetSQL\Models\Chapter.cs` | ChapterId, SeriesId, ChapterNumber, ChapterNameId |
| InspectionNoteAttachment | `SiNetSQL\Models\InspectionNoteAttachment.cs` | Screenshot metadata (no image in DB) |
| InspectionReportDrawing | `SiNetSQL\Models\InspectionReportDrawing.cs` | Drawing file links per report |
| InspectionReportReviewedFile | `SiNetSQL\Models\InspectionReportReviewedFile.cs` | Reviewed plan files per report |
| InspectionReportSnapshot | `SiNetSQL\Models\InspectionReportSnapshot.cs` | Sent-state snapshot JSON |

### Dialog files

| Dialog | File | Purpose |
|---|---|---|
| R01ReportDialog | `SiNetProjectManagerV2\Dialogs\R01ReportDialog.xaml.cs` | Separate R01-specific report dialog (not part of the Inspection lifecycle) |
| TemplateValidationWindow | `SiNetProjectManagerV2\WPF Window\TemplateValidationWindow.xaml.cs` | Template structure validation UI |

> **Note:** R01ReportDialog uses its own `R01ReportService`, `ReplicaR01Repository`, and `MasterPlanR01Repository` with separate connection strings. It is **not** part of the standard Inspection Report system documented here.

---

## 3. Current behavior

### 3.1. How a user opens the report UI

The `FloatingInspectionView` is a floating WPF `UserControl` that opens as an overlay within the main application. When loaded, the code-behind injects WPF-specific services (Google Drive integrations, screenshot upload, email workflow, etc.) into the ViewModel via `OnLoaded`. The view subscribes to `ActiveProjectContext` — changing the active project automatically reloads the report data.

### 3.2. How a new report is created

Report creation is a multi-step process triggered by the `CreateReportCommand`:

1. **Resolve template**: From a selected Google Drive template or manually pasted URL → extract spreadsheet ID.
2. **Resolve series**: User-selected existing series, "Create New" (prompts for name), or auto-resolve via `TemplateSyncService.EnsureSeriesAsync`.
3. **Scan template**: `IInspectionTemplateProvider.ScanAndParseTemplateAsync` — parses the Google Sheet for structured tags, validates structure.
4. **Block on errors**: If `scanResult.HasErrors`, show validation dialog and abort.
5. **Sync to DB**: `TemplateSyncService.SyncAsync(rows, seriesId)` — creates/versions/deactivates sections (see §4).
6. **Create report**: `InspectionReportService.CreateReportAsync` — snapshots active sections, creates placeholder notes, carries over unresolved notes from previous report.
7. **Post-creation validation**: Verifies every template section has a corresponding note.
8. **Refresh + select**: Reloads report list, selects the new report.

### 3.3. How report versions are represented

Versioning is **per ReportNumber within a Series**:
- `InspectionReport.ReportNumber` is auto-incremented within `(ProjectId, SeriesId)` scope.
- Unique constraint: `(ProjectId, SeriesId, ReportNumber)`.
- Report 1 = first version, Report 2 = second version (after send + next round), etc.
- There is **no explicit status enum** (Active/Draft/etc.). Lifecycle is based on:
  - `SentAt` — null until exported/sent
  - `IsLockedAfterSend` — true after export, false after unlock
  - `SentSpreadsheetId` / `SentSpreadsheetUrl` — set on export

### 3.4. How InspectionSeries groups reports

`InspectionSeries` represents a template-bound collection of reports for a project:
- One series per template per project.
- All reports in a series share the same section structure (Chapters + Sections).
- Series carries `TemplateSpreadsheetId` to identify the source template.
- Reports within a series form a version chain.

### 3.5. How Alternative is used

`ProjectAlternative` is currently used in the context of **reviewed files** and **per-note file links**:
- `InspectionReportReviewedFile` has `Alternative` and `SortOrder` fields.
- `InspectionNote` has `LinkedAlternative` and `LinkedVersion` for per-note file links.
- There is no direct FK from `InspectionReport` or `InspectionSeries` to `ProjectAlternative`.
- The concept is used for "which version/alternative of the plan is being reviewed."

### 3.6. How sections and chapters are loaded

Sections and Chapters are template-level entities scoped to `InspectionSeries`:
- Created by `TemplateSyncService.SyncAsync()` when a report is created from a template.
- `Chapter` has `(SeriesId, ChapterNumber)` — unique per series.
- `Section` has `(ChapterId, SectionCode, Version)` — versioned per chapter.
- Only `IsActive = true` sections are used for new report snapshots.
- Dictionary pattern: `ChapterName` and `SectionName` are shared lookup tables.

### 3.7. Template-created main sections vs manually added detailed notes

This is a strict two-tier architecture:

| Level | Entity | Example | Created by | Scope |
|---|---|---|---|---|
| **Main sections** (X.Y) | `Section` with parent `Chapter` | "1.1", "3.6" | `TemplateSyncService.SyncAsync()` | Per `InspectionSeries` (template-level, shared across all reports in series) |
| **Detailed sub-sections** (X.Y.Z) | `InspectionNote` rows with 3-level `NoteSubIndex` | "1.1.1", "1.1.2", "3.6.1" | `CreateReportAsync` snapshot or `AddNoteAsync` | Per `InspectionReport` |

**Key constraint**: `InspectionNote.SectionId` is a non-nullable `int` FK to `Section`. Every note must belong to a valid Section.

### 3.8. How the user manually adds detailed sub-sections

`FloatingInspectionViewModel.AddNoteToSection(SectionTreeItem)`:
1. Reads the section's `NumericCode` (e.g., "1.1").
2. Counts existing sub-notes with matching prefix in the section's `Notes` collection.
3. Generates next index: `"{numericCode}.{count+1}"` (e.g., "1.1.3").
4. Calls `InspectionReportService.AddNoteAsync(reportId, sectionId, nextIndex)`.
5. Adds a `NoteTreeItem` to the section's `Notes` observable collection.

The `AddNoteAsync` method creates a plain `InspectionNote` row with `ReportId`, `SectionId`, `NoteSubIndex`. No new `Section` entity is ever created by the UI.

Guard: `CanAddNoteToSection` returns false if the last note in the section has empty text.

### 3.9. How NoteSubIndex works

| Context | NoteSubIndex format | Example |
|---|---|---|
| Chapter 0 (general data) | Single number | "1", "2", "3" |
| Initial placeholder (numbered chapters) | "X.Y.1" | "1.1.1", "3.6.1" |
| Manual additions | "X.Y.N" (sequential) | "1.1.2", "1.1.3" |
| Carry-over from previous report | Reuses placeholder or auto-increments | "1.1.1" or "1.1.4" |

Unique constraint: `(ReportId, SectionId, NoteSubIndex)`.

### 3.10. How numbering and reindexing work

`ReindexSectionNotesAsync` (triggered after add/delete/move):
- Collects all sub-notes for a section.
- Renumbers them sequentially: "X.Y.1", "X.Y.2", "X.Y.3"…
- Persists via `RenumberNotesAsync` which updates each note's `NoteSubIndex` in DB.

### 3.11. How skipped numbering is handled today

The system does **not** create placeholder notes for skipped numbers. If a user deletes note "1.1.2", the remaining notes are renumbered to fill the gap. Skipped numbering does not occur in normal usage because reindexing is automatic.

For migration scenarios (importing historical data with gaps), empty placeholder notes would need to be created explicitly.

### 3.12. Report lifecycle states

There is **no explicit status enum**. Lifecycle is based on fields:

| State | SentAt | IsLockedAfterSend | Behavior |
|---|---|---|---|
| Draft / Open | null | false | Editable. All commands available. |
| Sent / Locked | set | true | Read-only. Yellow banner shown. Locked actions: Unlock, Repull Responses, Open Source, Share. |
| Unlocked (latest only) | set | false | Re-editable. Only the latest report in a series can be unlocked. |

### 3.13. How a report becomes locked after send

`InspectionReportService.MarkReportAsSentAsync`:
1. Sets `SentAt = DateTime.UtcNow`, `SentByUserId`, `SentSpreadsheetId`, `SentSpreadsheetUrl`.
2. Sets `IsLockedAfterSend = true`.
3. Flips previous snapshot's `IsCurrentSentSnapshot = false`.
4. Builds `InspectionReportSnapshot` JSON with all notes + `NoteCellMap` (row/column mapping for deterministic re-import).
5. Transaction-safe.

### 3.14. How previous versions are displayed or protected

- Report list shows all reports in the series, ordered by `ReportNumber`.
- Previous (non-latest) reports are sent/locked and cannot be unlocked — `UnlockReportAsync` only works for the latest report in the series.
- Previous reports can be viewed but not edited.

### 3.15. How designer/planner responses are stored

- `InspectionNote.PlannerResponseText` — stores the planner's response text.
- Imported via `IPlannerResponseImportService`:
  - `ImportFromSnapshotMapAsync` — deterministic import using `NoteCellMap` from snapshot.
  - `PullResponsesByColumnAAsync` — column-A rule (A=NoteSubIndex, D=response).
  - `ScanForResponsesAsync` — heuristic scan.
- Placeholder filter: Hebrew header labels like "תגובת המתכנן" are stripped.
- Previous round's response preserved as `PreviousPlannerResponseText` in tree items.

### 3.16. How note statuses are stored

Dual-write: both `NoteStatus` (string key) and `NoteStatusId` (FK to `InspectionNoteStatus`) are written simultaneously.

Status keys: `""` (empty), `Passed`, `Failed`, `RecurringFailed`, `NotApplicable`, `ManagerReview`.

Auto-sync: setting `NoteText` on an empty-status note auto-sets status to `"Failed"`. Clearing text clears status.

`ManagerReview` hard-blocks report export.

### 3.17. How report export to Google Sheets works

`GoogleReportExportService.ExportReportAsync`:
1. Load report + notes + project data from DB.
2. Copy template spreadsheet on Google Drive.
3. Global tag replacement (`<<FieldName>>`).
4. Tag-based cell scanning: `<<X.Y Title [...]>>` → aggregated status, `<<X.Y Title>>` → note text.
5. "Strongest status" aggregation: Failed > RecurringFailed > Passed > NotApplicable.
6. Row cloning for sections with multiple notes (bottom-up to avoid index shifting).
7. Rich text injection with `TextFormatRun` conversion.
8. Returns `NoteCellMap` per note (for deterministic re-import).

Pre-export gates (`CanExportReport`): not locked, not loading, not exporting, must have `ReviewedVersion`, must have ≥1 `ReviewedFile`, all notes must pass validation (no `HasValidationError`, no `ManagerReview`).

Post-export: `MarkReportAsSentAsync` → locks, `ClearNotRelevantNotesAsync` → clears `NotApplicable` notes, `CaptureSnapshot` for change detection.

### 3.18. How the report email/send workflow works

`IInspectionReportEmailWorkflow` → `InspectionReportEmailWorkflow`:
1. `IInspectionReportEmailBuilder.BuildAsync(reportId)` → builds email context.
2. `IEmailComposerService.ComposeAndSendAsync(context)` → sends via Gmail.
3. Returns `EmailSendResult` with `GmailMessageId`.

Gate: Report must have `SentSpreadsheetId` or `SentSpreadsheetUrl` (must be exported first).

### 3.19. How the UI loads and displays notes

1. `LoadReportsInternalAsync` → `InspectionReportService.GetReportsForProjectAsync(projectId, seriesId, filterInspectorId)`.
2. User selects a report.
3. `LoadReportNotes` → `InspectionReportService.GetNotesForReportAsync(reportId)` — includes `Section → Chapter → ChapterName`, `SectionName`, `PreviousNote`, `Attachments`.
4. `BuildInspectionTree(notes)`:
   - Chapter 0 → `GeneralDataChapterItem` with auto-populated fields.
   - Numbered chapters → groups notes by Chapter → Section → Note.
   - Base notes (2-level index "X.Y") → hidden from tree (section-level).
   - Sub-notes (3-level "X.Y.Z") → visible `NoteTreeItem` rows.
   - Auto-creates first sub-note for empty sections via `EnsureFirstSubNotesAsync`.
5. After tree build: `CaptureSnapshot()` for change detection, `ScanTemplateForSyncAsync()` for template comparison.

### 3.20. Next-round flow

`OpenOrCreateNextRoundAsync` (after planner responses received):
1. Load sent report with series.
2. Check for existing unlocked next report in same series.
3. If found → validate carry-over → if invalid, offer rebuild.
4. If not found → create new report from same template.
5. Always validates freshly created reports.

### 3.21. Additional features

**Screenshot attachments**: Clipboard → PNG → SHA256 → duplicate detection → Google Drive upload → `InspectionNoteAttachment` (metadata only).

**Drawing management**: Scan file sources → filter by `InspectionSeriesFileConfig` → attach/remove `InspectionReportDrawing` → stamp with "מאושר" overlay.

**Reviewed plans**: Phase A = `ReviewedVersion` text field (required for export). Phase B = `ReviewedFile` list from `ActiveFileQueryRegistry`.

**Template integrity**: Hash-based check before export blocks stale template exports.

---

## 4. Existing mechanisms to reuse

For the Google Sheet Review Migration, the following existing mechanisms should be reused:

| Mechanism | Method | Migration use |
|---|---|---|
| Section creation | `TemplateSyncService.SyncAsync(TemplateSyncRow list, seriesId)` | Create main sections from JSON section codes |
| Report creation | `InspectionReportService.CreateReportAsync` | Create report with version number + note snapshot |
| Sub-note addition | `InspectionReportService.AddNoteAsync(reportId, sectionId, subIndex)` | Add detailed sub-notes from JSON |
| Report locking | `InspectionReportService.MarkReportAsSentAsync` | Lock historical versions |
| Report unlocking | `InspectionReportService.UnlockReportAsync` | Latest-only unlock |
| Note save | `InspectionReportService.SaveNotesAsync` | Populate note content from JSON |
| Note reindex | `FloatingInspectionViewModel.ReindexSectionNotesAsync` | Renumber after manipulation |

---

## 5. Active legacy behavior

| Mechanism | Status | Notes |
|---|---|---|
| Dual-write `NoteStatus` + `NoteStatusId` | **Active legacy** | Both string key and FK are written simultaneously. Candidate for future cleanup to FK-only. |
| Manual DI injection in code-behind | **Active legacy** | WPF-specific services injected via `OnLoaded`, not through DI container. Architectural constraint. |
| `ImportPlannerResponsesCommand` (legacy row scanning) | **Active legacy** | Older import path. `MarkResponseReceived` uses the newer deterministic snapshot-map import. |
| R01ReportDialog | **Active, separate** | Uses its own service, repository, and connection strings. Not part of Inspection lifecycle. |

---

## 6. Known gaps

| # | Gap | Severity |
|---|---|---|
| 1 | No placeholder notes for skipped numbering | LOW — only matters for migration |
| 2 | No direct report-to-workflow link | LOW — indirect via shared ProjectId is sufficient |
| 3 | `AddNoteAsync` does not populate note content | LOW — migration must update notes separately after creation |
| 4 | `CanAddNoteToSection` blocks if last note is empty | LOW — migration creates notes programmatically, not via UI guard |
| 5 | No bulk note creation API | MEDIUM — migration must call `AddNoteAsync` per sub-note |
| 6 | ViewModel is 4973 lines with 30+ commands | MEDIUM — candidate for future decomposition |

---

## 7. Relationship to Google Sheet Review Migration

The migration design (see `Docs/Domains/Migration/GoogleSheetReviewMigrationDesign-2026-06-21.md`) reuses this system as follows:

1. **Template creates main sections** via `TemplateSyncService.SyncAsync` with `TemplateSyncRow` data from JSON section codes.
2. **`CreateReportAsync` creates placeholder notes** ("X.Y.1" per section).
3. **JSON provides detailed sub-notes** via `AddNoteAsync` — same mechanism as manual user addition.
4. **Non-latest versions locked** via `MarkReportAsSentAsync`.
5. **Existing report versions** detected via `(SeriesId, ReportNumber)` unique constraint.
6. **FloatingInspectionView** displays imported reports correctly — it queries notes by `ReportId` and navigates to `Section → Chapter`.

---

## 8. Out of Scope

- Implementing the migration code.
- Refactoring the ViewModel.
- Changing the dual-write status pattern.
- Adding a bulk note creation API.
- Modifying the export service.

---

## 9. Dropped / cancelled / postponed

| Item | Status |
|---|---|
| Creating a new report model | **Cancelled** |
| Requiring the modern template to contain every historical detailed sub-section | **Dropped** — Template creates main sections; JSON provides sub-note content |
| Inventing missing section content | **Not approved** |
| Silent overwrite of report versions | **Not approved** |
| DB schema changes | **Not approved** |
| Code changes | **Not approved** |

---

## 10. No-code-change confirmation

- **No code was changed.**
- **No DB was changed.**
- **No Google Sheet was changed.**
- **No data was imported.**
- **No reports, tasks, workflows, or TaskLinks were created.**
- **No old mechanisms were deleted or disabled.**
