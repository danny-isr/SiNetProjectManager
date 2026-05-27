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

## Storage Destination (added 26.05.2026)

Every managed file in the system has **one binding Storage Destination**.
The Storage Destination is the location where the file is expected to live
and to be treated as "the correct file" by the system. Even when copies
exist elsewhere, only the configured Storage Destination is the physical
source of truth.

**Valid Storage Destination values:**

- **ACC** — ACC is the physical source of truth for the file.
- **File Server** — the configured server path is the physical source of truth.
- **Google Drive** — Google Drive is the physical source of truth (note:
  Google Drive upload itself remains **postponed**, see below).
- **Gmail** — *read-only ingestion source only.* Gmail is not a write/management
  target.

**Gmail is different from the other destinations:**

- Gmail is a read-only ingestion source.
- The system does **not** write back to Gmail.
- Gmail is **not** a permanent management destination for project files.
- A file that arrives via Gmail must be ingested into a write-capable
  Storage Destination (ACC / File Server / Google Drive) according to the
  configured destination for that file.

**Rules:**

- The system does **not** guess where the correct file is. It reads the
  configured Storage Destination.
- Copying between locations is allowed, but a copy does **not** change the
  source of truth unless the Storage Destination is explicitly and
  deliberately updated.
- Do **not** write back to Gmail.
- Do **not** treat Gmail as a permanent management destination.
- Do **not** decide truth based on "where we found a copy".
- Do **not** auto-change Storage Destination.
- Do **not** create a fallback that picks a copy from another location when
  the configured destination is missing.

## Metadata source of truth (added 26.05.2026)

Each kind of metadata has one owner:

- **Gmail / RFC822 headers** — email identity and global thread relations.
- **DB** — business process (projects, tasks, workflow, `ProjectFile` /
  `ProjectAlternative` / `ProjectFileInstance`, user decisions, links).
- **Storage Destination** — physical existence of the file.
- **ACC custom attributes** — metadata that must travel with the ACC item
  (origin email, `Message-ID` / `MessageKey` reference, Inbox source,
  `ProjectFile` reference, move target). Does not replace the DB as the
  business source of truth.
- **`manifest.json` / sidecar JSON** — audit snapshot at ingestion / upload /
  Inbox-creation time. Does **not** replace the DB, Storage Destination, or
  Gmail headers.
- **UI / DOM** — never a source of truth.

Forbidden:

- DB-alone as proof of physical file existence.
- ACC attributes as source of truth for general workflow.
- `manifest.json` instead of the DB.
- `manifest.json` instead of a Storage Destination existence check.
- UI / DOM as a source of truth.
- Writing the same metadata to multiple owners without declaring source and copy.

## What we do not do now
- Do not change `ProjectFileInstance` model, table, or FK layout.
- Do not change refile flows or `UpsertInstanceAsync`.
- Do not add startup-time browser authorization or unrelated ACC bootstrap behavior for filing.
- Do not write metadata that references missing definition IDs.

## Dropped / cancelled / postponed
- Filename-based version tracking — dropped.
- DB-only fallback for "file exists" — dropped.
- Gmail as a write / management Storage Destination — **dropped** (Gmail is
  read-only ingestion).
- Auto-changing a file's Storage Destination based on where a copy is found —
  **dropped**.
- Fallback to a copy in another location when the configured Storage
  Destination is missing — **dropped**.
- `manifest.json` as a substitute for the DB or for a Storage Destination
  existence check — **dropped**.
- Deep refactor of refile pipeline — postponed.
- **Google Drive upload — postponed.** Infrastructure may exist, but the specific Google Drive upload mechanism is not active. Do not add a new Google Drive upload mechanism and do not enable a new fallback for it.

## Relevant terms / search terms
ProjectFile, ProjectAlternative, ProjectFileInstance, MoveToProject, OpenQuoteProject, IProcessActionHandler, UpsertInstanceAsync, AccInboxLayout, AccInboxReconciliationService, SiInbox.Move.TargetAltId.

## Relevant code areas (informational)
- `ProjectFileFilingService`
- `MoveToProjectProcessActionHandler`
- `AccInboxReconciliationService`
