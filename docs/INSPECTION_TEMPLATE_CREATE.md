# Inspection report create — template import contract

> **Status:** Target (2026-09-05)  
> **Hosts:** Standalone `SiNet.App.Wpf`, V2 adapter

## Principle

A normal **דוח חדש** with a selected Google template must never create an empty questionnaire.

Required pipeline (single shared path):

1. Resolve `SpreadsheetId` from the selected template (not `series[0]`).
2. `EnsureSeriesAsync(projectId, spreadsheetId, templateUrl)` — series keyed by project + spreadsheet.
3. Scan sheet tags → validate → require `SyncRows.Count > 0`.
4. `TemplateSyncService.SyncAsync` → Chapters/Sections.
5. Create `InspectionReport` and snapshot active sections → `InspectionNotes`.
6. Fail closed before inserting the report when scan/validation/sync/sections fail.

Empty `native://empty-template` is **not** allowed on the normal Create button.

## Hydrate (DEV / empty unsent repair)

Idempotent hydrate of an existing unsent, unlocked, empty report (e.g. #4):

- Resolve template from `SourceFileUrn` / series
- Ensure series + scan + sync
- Attach report to series if missing
- Insert missing placeholder notes only (no duplicates)
