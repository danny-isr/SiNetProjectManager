# Project Files Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for project file filing.
- **Scope:** `ProjectFile`, `ProjectAlternative`, `ProjectFileInstance`, file filing, `MoveToProject`, external files, links to ACC and tasks.

## Purpose
Define how files are filed into projects, how they relate to ACC, and how `MoveToProject` behaves.

## Source of truth
- **ACC** for the physical file after upload.
- **DB** for the logical project structure (`ProjectFile` → `ProjectAlternative` → `ProjectFileInstance`).
- `MoveToProject-Decisions-2026-05-24.md` remains the authoritative decision log for `MoveToProject` behavior.

## Core principles
1. Filing of files happens **after** project creation. During "פתיחת פרויקט" the user reviews attachments (already uploaded to ACC Inbox); actual filing occurs once the project exists.
2. `OpenQuoteProject` does **not** create an ACC project. `MoveToProject` performs the ACC ensure at move time, and only required folders are created.
3. `MoveToProject` outcome enrichment is backward compatible:
   - All existing properties (including `ProjectFileInstanceId`) are preserved.
   - New enrichment fields are nullable / default-valued.
   - No schema, migration, or ModelSnapshot changes are required for enrichment.
4. `ProjectFileInstance` is a cache/helper for the ACC item. ACC remains authoritative for existence.
5. `Version` segment in the filename is not a version tracker. New files get `Version = 1`. Existing files with `Version = 2+` keep their name as identity. ACC handles versioning natively.
6. External files and uploaded attachments share the same filing pipeline (`IProcessActionHandler` dispatcher), not parallel ad-hoc paths.
7. Refile and MoveToProject paths are protected: their model/table/foreign-key layout, `UpsertInstanceAsync`, and broad UI / inspection behavior are not changed as part of domain documentation or enrichment changes.

## What we do not do now
- Do not change `ProjectFileInstance` model, table, or FK layout.
- Do not change refile flows or `UpsertInstanceAsync`.
- Do not add startup-time browser authorization or unrelated ACC bootstrap behavior for filing.
- Do not write metadata that references missing definition IDs.

## Dropped / cancelled / postponed
- Filename-based version tracking — dropped.
- DB-only fallback for "file exists" — dropped.
- Deep refactor of refile pipeline — postponed.
- **Google Drive upload — postponed.** Infrastructure may exist, but the specific Google Drive upload mechanism is not active. Do not add a new Google Drive upload mechanism and do not enable a new fallback for it.

## Relevant terms / search terms
ProjectFile, ProjectAlternative, ProjectFileInstance, MoveToProject, OpenQuoteProject, IProcessActionHandler, UpsertInstanceAsync, AccInboxLayout, AccInboxReconciliationService, SiInbox.Move.TargetAltId.

## Relevant code areas (informational)
- `ProjectFileFilingService`
- `MoveToProjectProcessActionHandler`
- `AccInboxReconciliationService`
