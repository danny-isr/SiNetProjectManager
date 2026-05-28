# Project Files Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for project file filing.
- **Scope:** `ProjectFile`, `ProjectAlternative`, `ProjectFileInstance` (runtime projection), file filing, `MoveToProject`, external files, links to ACC and tasks.

## Purpose
Define how files are filed into projects, how they relate to ACC, and how `MoveToProject` behaves.

## Source of truth
- **DB** for the logical project structure and stable business definitions (`Project`, `ProjectFolder`, `ProjectFile`, `ProjectAlternative`, `Storage Destination`, tasks, workflow, user decisions, business links).
- **Storage Destination** (ACC / File Server / Google Drive) for the **physical existence** of a file.
- **Gmail** is a read-only ingestion source only — never a permanent management destination.
- `ProjectFileInstance` is **not** a source of truth — it is a runtime projection (see below).
- `MoveToProject-Decisions-2026-05-24.md` remains the authoritative decision log for `MoveToProject` behavior.

## Core principles
1. Filing of files happens **after** project creation. During "פתיחת פרויקט" the user reviews attachments (already uploaded to ACC Inbox); actual filing occurs once the project exists.
2. `OpenQuoteProject` does **not** create an ACC project. `MoveToProject` performs the ACC ensure at move time, and only required folders are created.
3. `MoveToProject` outcome enrichment is backward compatible:
   - All existing properties (including `ProjectFileInstanceId`) are preserved.
   - New enrichment fields are nullable / default-valued.
   - No schema, migration, or ModelSnapshot changes are required for enrichment.
4. `ProjectFileInstance` is a **runtime projection** of the selected project's file state (see the dedicated section below). It is not a stable per-instance DB entity, not a permanent cache, and not a source of truth on its own.
5. `ProjectFile` is a relatively stable DB entity that defines the **type / target / template** of a file (which file types or filing targets exist in the system); it is part of the stable business structure.
6. `ProjectAlternative` is a persisted DB entity that is **dynamic per project**. It may be **auto-created** from a real file name or a filing action after **normalization and duplicate prevention**, but it is **not** auto-removed when files disappear (see the dedicated section below).
7. `Version` segment in the filename is not a version tracker.
7. External files and uploaded attachments share the same filing pipeline (`IProcessActionHandler` dispatcher), not parallel ad-hoc paths.
8. Refile and MoveToProject paths are protected:

## ProjectAlternative — persisted but dynamic per project (added 26.05.2026)

`ProjectAlternative` is a **persisted DB entity**, but its lifecycle is
**dynamic per project** and **discovered from actual project files / filing
actions** rather than predefined up-front:

- It belongs to a project.
- It represents an alternative / variant of a `ProjectFile` within that
  project.
- It is stored in the DB.
- It **can be created automatically** when the system detects a new
  alternative from a **real file name** or from a **filing action**
  (`MoveToProject`, refile, ingestion of an external/uploaded file).

**Auto-creation is not blind — it must run through these steps in order:**

1. **Normalization** — trim leading and trailing whitespace from the
   candidate alternative name. Apply any other simple normalization
   already defined by the existing alternative-name rules.
2. **Illegal-character cleanup** — remove or handle characters that are
   not allowed in an alternative name, according to the **existing**
   alternative-name rules in the system. No new naming rule is introduced
   in this documentation round.
3. **Duplicate prevention** — after normalization + cleanup, check for an
   existing alternative in the same project with the same effective name
   and do **not** create a duplicate. Example: if `A1` already exists and a
   filename contains `A1`, no new alternative is created.
4. **User-visible warning on suspicious / invalid names** — if the
   candidate name contains illegal characters or otherwise looks invalid:
   - if it can be safely normalized per the existing rules, normalize it;
   - otherwise, **do not silently ignore the issue** — surface a
     user-visible warning / indication that the name needs handling.

**Name-based linkage (added 26.05.2026):**

- The link between a file and its `ProjectAlternative` is **name-based**:
  it is derived from the alternative name as it appears in the file name /
  file naming template, not from a hard ID relationship persisted on every
  file path.
- Therefore any **deletion, rename, or merge** of an alternative must
  consider the **actual file names** in the project, not just the DB row.
- Deleting a DB row is **not** sufficient if files in the project still
  carry the alternative name.

**Deletion of `ProjectAlternative` (maintenance action only):**

- `ProjectAlternative` is **not** removed automatically just because no
  file currently uses it. Absence at a given moment does not prove the
  alternative is obsolete.
- Deletion is a **dedicated maintenance action**, never an automatic side
  effect of scanning or refresh.
- Before deletion, the system must perform a **full scan of the project's
  files** and verify that **no file / instance currently uses the
  alternative name**.
  - If the alternative is fully empty in the project, it may be deleted.
  - If any file still uses the name, or if no full scan was performed, it
    must **not** be deleted.
- Reminder: deleting a `ProjectAlternative` while files still carry its
  name will cause it to be **re-created automatically** on the next scan,
  because alternative linkage is name-based.

**Merging `ProjectAlternative` (maintenance action only):**

- Merging alternatives is an **explicit, user-driven maintenance action**.
  It is never automatic.
- Required steps:
  1. The user selects a **source** alternative.
  2. The user selects a **target** alternative.
  3. The system **remaps** usages from the source to the target.
  4. Where required for consistency, the system **renames file names** so
     they reflect the **target** alternative name (name-based linkage).
  5. The source alternative may be **removed only after** it has **no
     remaining file usage** in the project (verified by full project
     scan).
- No new merge mechanism is created in this documentation round; this
  section only describes the required behavior for a future approved
  implementation round.

**Forbidden for `ProjectAlternative`:**

- Creating duplicates that differ only by leading / trailing whitespace.
- Creating an alternative with illegal characters without a user-visible
  warning.
- Silently ignoring an invalid / suspicious alternative name.
- Auto-deleting alternatives because a file is missing / not found.
- Deleting an alternative without a full scan of the project's files.
- Assuming a DB-row deletion alone is sufficient when files still carry
  the alternative name.
- Auto-merging alternatives without an explicit user-selected source and
  target.
- Performing a merge without considering / updating the relevant file
  names.
- Treating the `ProjectAlternative` list as a runtime projection — it is
  persisted business data, not a runtime view.

## ProjectFileInstance — runtime projection (added 26.05.2026)

`ProjectFileInstance` is defined as a **runtime projection / runtime view** of
the file state for the currently selected project. It is **not** a permanent
DB entity that holds one row per every possible file instance in every
project. The DB must not be expected to materialise "millions of instances"
just because a project has many files, alternatives, or storage destinations.

**What the DB stores (stable business data):**

- `Project`
- `ProjectFolder`
- `ProjectFile`
- `ProjectAlternative`
- `Storage Destination`
- Stable business links (project ↔ file ↔ task ↔ workflow)
- Tasks, workflow state, user decisions

**What is built at runtime (the `ProjectFileInstance` projection):**

When the user enters a project — in particular the `ProjectWork` /
"בעבודה 2" screen — a service builds a `ProjectFileInstance` projection
**for the current project only**. The projection combines:

- Definitions from the DB (folders, `ProjectFile`, `ProjectAlternative`,
  configured Storage Destination).
- The current physical state at the configured Storage Destination
  (ACC / File Server / Google Drive).
- Links to tasks / workflow when relevant.

The projection gives the UI and services a coherent snapshot of *what
currently exists in this project* — nothing more, nothing less.

**Source-of-truth boundaries for the projection:**

- `ProjectFileInstance` is **not** a source of truth.
- DB is the source of truth for business definitions and structure.
- Storage Destination is the source of truth for physical existence.
- ACC / File Server / Google Drive answer the existence question according
  to the configured destination.
- Gmail is read-only ingestion only.

**Project entry — initial full scan (added 26.05.2026):**

- When the user **enters a project**, the system performs a **full scan of
  that project** in order to build the `ProjectFileInstance` runtime
  projection for the current project.
- This full scan is **scoped to the current project only** — it is never a
  full scan of all projects or a system-wide scan.
- During this initial scan, new `ProjectAlternative` rows may be created
  automatically (per the `ProjectAlternative` rules above), but existing
  alternatives are **not** removed.

**After the initial scan:**

- The projection is updated through **internal application events**
  (file added / uploaded / filed / moved / deleted / metadata updated).
- The projection may be refreshed through **focused / targeted refresh**
  scoped to the current project, the open folders, and the active work
  area.
- A **full project rescan** is performed **only**:
  - on **explicit user request** (e.g. a "refresh" / "rescan" action), or
  - through a **dedicated maintenance / system action** that explicitly
    requires it.
- A full rescan is **not** scheduled automatically every few minutes on an
  open project.

**Refresh strategy (future, not implemented in this round):**

The runtime projection must reflect events that change file state, for
example:

- A file is added.
- A file is uploaded.
- A file is filed.
- A file is moved.
- A file is deleted / missing / not found.
- Metadata is updated.

Rules for a future refresh mechanism:

- When the storage source supports reliable events, **use the events**.
- When the storage source has no reliable event stream in the current
  implementation (for example Google Drive in its current state, or a
  File Server without a reliable watcher, or ACC when explicit
  reconciliation / validation is required), a future **focused refresh /
  targeted polling** mechanism will be required.
- Any future refresh must be **scoped**:
  - to the **current project**,
  - to the **open folders**,
  - to the **active work area**.
- Broad, system-wide polling across all projects is **not** allowed.
- Automatic full rescans on a timer for an open project are **not** allowed.
- No new refresh / polling mechanism is added in this documentation round.
  It is described here as a principle for a future approved round.

**Forbidden:**

- Recreating a `ProjectFileInstance` entity / DB table / `DbSet` (removed in Stage 9E.4 — see Gap 9).
- Treating any DB row (current or future) as proof of physical file existence.
- Using a persisted placement / instance row instead of a Storage Destination existence check.
- Persisting the runtime resolver's cache as if it were stable business data.
- Wide, system-wide polling.
- Adding a new refresh mechanism in this documentation round.

**Runtime resolver (post-Stage 9E.4):**

- Runtime resolution is done by `IProjectFileLocationResolver`
  (implementation `ProjectFileLocationResolver`).
- The resolver is **runtime / session only**, holding a short-lived
  **in-memory** cache. It is **not** a persisted replacement for
  `ProjectFileInstance` and **not** a source of truth.
- The actual storage state — ACC item / folder / version / metadata,
  File Server path + sidecar, Google Drive file / folder + sidecar — is
  the source of truth. The resolver consults the `IFileStore`
  implementations; it does not duplicate their truth.

## Storage Destination (added 26.05.2026)

Every managed file in the system has **one binding Storage Destination**.
The Storage Destination is the location where the file is expected to live
and to be treated as "the correct file" by the system. Even when copies
exist elsewhere, only the configured Storage Destination is the physical
source of truth.

**Valid Storage Destination values** (mirrored by the
`FileStorageDestination` enum in `SiNetSQL\Models\FileStorageDestination.cs`):

- **File Server** (`FileStorageDestination.FileServer`, default) — the
  configured server path is the physical source of truth.
- **ACC** (`FileStorageDestination.Acc`) — ACC is the physical source of
  truth for the file.
- **Google Drive** (`FileStorageDestination.GoogleDrive`) — **active**.
  Drive is an active Storage Destination. Routing goes through
  `ProjectFileUploadService` → `SiNetSQL.FileIndex.Stores.GoogleDriveStore`,
  which writes the data file plus a `*.si.json` sidecar under the
  configured Shared Drive / projects-root folder. **Duplicate filename in
  the target Drive folder is a conflict**
  (`FileStoreConflictException`); the system never auto-picks among
  duplicates and never falls back silently to another destination. Drive
  **delete** wiring in refile cleanup remains *postponed*. See
  [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md)
  (*Round update — Gap 9 closure + Google Drive activation* and
  *Round update — Stage 9E.5*).

**Gmail is NOT a Storage Destination.**

- Gmail is **not** a value in `FileStorageDestination` and **not** a
  managed-file destination.
- Gmail is a **read-only ingestion source** only, represented by
  `EmailInboxMessage` / `EmailInboxAttachment` and the
  `EmailIngestionService` path.
- The system does **not** write back to Gmail.
- A file that arrives via Gmail is managed after ingestion through its
  configured Storage Destination (typically ACC Inbox, then ACC project
  folders, or File Server), never through Gmail itself.

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

**Routing source of truth (post-Stage 9E.4):**

- `ProjectFile.StorageDestination` is the **only** routing source.
  `ProjectFileFilingService`, `ProjectFileUploadService`, and
  `IFileOpenService` all dispatch strictly on it.
- `ProjectFileInstance.StorageDestination` **no longer exists** —
  the entity, the column, and all reads of it were removed in Stage 9E.4
  (Gap 9). Migration `RemoveProjectFileInstanceTable` drops the table
  and the related FK columns / indexes; `Update-Database` is user-run.
- **Physical existence** of a file is established by the actual storage
  backend (ACC item / version / folder; File Server path + sidecar;
  Google Drive file / folder + sidecar), reached uniformly through the
  `IFileStore` implementations (`AccFileStore`, `FileServerStore`,
  `GoogleDriveStore`). The DB does **not** prove file existence.

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

## Google service boundary for project files (added 26.05.2026)

This section is the ProjectFiles-domain companion to the Google service
boundary documented in
[`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Architecture/ServiceCatalog-2026-05-26.md)
and
[`Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](../Architecture/ArchitecturePrinciples-2026-05-26.md).

### Role of `SiOffice.GoogleConnector` / `GoogleService` for project files

- It is the **connector / service layer** for Google API access (Gmail,
  Google Drive, Google Sheets, OAuth / token handling).
- For project files, it provides API operations only: reading from Google
  Drive, downloading attachments from Gmail for ingestion, etc.

### Gmail vs Google Drive vs Google Sheets

- **Gmail** — read-only ingestion source for email attachments. **Not** a
  Storage Destination for project files. The system does **not** write
  back to Gmail.
- **Google Drive** — a **possible Storage Destination** for project files,
  separate from Gmail. When Drive is the configured Storage Destination,
  Drive is the **physical source of truth** for that file. However:
  - Google Drive **upload remains postponed** — no new Drive upload
    mechanism is added without an explicit decision.
  - Google Drive is **not** a fallback when ACC / File Server is missing.
  - Google Drive does **not** replace DB as the business source of truth.
- **Google Sheets** — reporting / template integration surface only. It is
  **not** a Storage Destination and **not** a general business source of
  truth for project files or workflow.

### What `GoogleService` does NOT do for project files

- Does **not** decide Storage Destination.
- Does **not** file files into a project.
- Does **not** create `ProjectAlternative` rows by itself — alternative
  creation runs through `ProjectFileFilingService` and the alternative
  rules above (normalization, illegal-character cleanup, duplicate
  prevention, user-visible warnings).
- Does **not** decide workflow.
- Does **not** decide whether AI is invoked on a file.
- Is **not** a general business source of truth for project files.

### Forbidden across the Google boundary (project files view)

- Mixing Gmail and Google Drive as the same Storage Destination.
- Treating Gmail as a write / management destination for project files.
- Adding a new Google Drive upload mechanism without an explicit decision.
- Adding a Google Drive fallback when the configured Storage Destination
  is missing.
- Using `SiOffice.GoogleConnector` / `GoogleService` to bypass
  ProjectFiles / Workflow / Storage Destination rules.
- Using Google Sheets as a business source of truth for project files.

## What we do not do now
- Do not reintroduce a `ProjectFileInstance` entity, `DbSet`, table, or FK (removed in Stage 9E.4 — see Gap 9).
- Do not add a new persistent DB table that holds per-instance file-placement truth.
- Do not persist the `IProjectFileLocationResolver` session cache to the DB.
- Do not add a silent fallback between Storage Destinations (ACC ↔ File Server ↔ Google Drive).
- Do not auto-delete or auto-merge `ProjectAlternative` rows.
- Do not delete a `ProjectAlternative` without a full project scan verifying no file still uses its name.
- Do not silently accept an invalid / illegal `ProjectAlternative` name — surface a user-visible warning.
- Do not assume that deleting a `ProjectAlternative` DB row alone is sufficient when files still carry the alternative name.
- Do not implement a merge/delete maintenance flow for `ProjectAlternative` in this round; only document it as a principle.
- Do not perform a global / system-wide full scan across all projects.
- Do not perform an automatic recurring full rescan on an already-open project.
- Do not change refile flows or `UpsertInstanceAsync`.
- Do not add a refresh / polling mechanism in this round.
- Do not auto-change a file's Storage Destination based on where a copy was found during scan.
- Do not add startup-time browser authorization or unrelated ACC bootstrap behavior for filing.
- Do not write metadata that references missing definition IDs.

## Dropped / cancelled / postponed
- `ProjectFileInstance` as a persisted entity / DB table / `DbSet` — **removed** (Stage 9E.4). The model file, configuration, and `ProjectFileUploadService` were physically deleted; migration `RemoveProjectFileInstanceTable` drops the table, both FKs, both indexes, and the `EmailInboxAttachment.ProjectFileInstanceId` / `InspectionReportDrawing.FileInstanceId` columns; `Update-Database` is user-run.
- `ProjectFileInstanceId` as file placement / filer state — **removed** (Stage 9E.4). Replaced by `IProjectFileLocationResolver` (session cache only).
- `ProjectFileInstance.StorageDestination` as a routing source — **removed** (Stage 9E.4). Routing is `ProjectFile.StorageDestination` only.
- PFI supersession chain — **removed** (Stage 9E.4).
- PFI-based rename warning (`CheckRenamedFileInstance`) — **removed** (Stage 9E.4).
- Persisting the runtime resolver's cache as if it were stable business data — **dropped**.
- Google Drive **upload** activation — **done** (Drive is now an active Storage Destination with duplicate-filename conflict semantics).
- Google Drive **delete** wiring in refile cleanup — **postponed** to a future approved round.
- Future rename-warning replacement via `FileIndex` / sidecar — **postponed**.
- Any audit mechanism replacing the old PFI supersession fields — **not created**; postponed unless explicitly approved.
- Automatic deletion of `ProjectAlternative` when no file currently uses it — **dropped** (removal / merge is a dedicated maintenance action only).
- Deleting a `ProjectAlternative` without a full project scan — **dropped**.
- Silently ignoring an invalid / illegal `ProjectAlternative` name — **dropped** (must surface a user-visible warning).
- Automatic merge of `ProjectAlternative` rows without user-selected source / target — **dropped**.
- Implementing the actual merge / delete maintenance flow for `ProjectAlternative` — **postponed** to a future approved round; only documented as a principle here.
- Global / system-wide full scan across all projects — **dropped** (not approved).
- Automatic recurring full rescan on an open project — **dropped** (full rescan only by explicit user request or dedicated maintenance/system action).
- Broad, system-wide polling of file state — **dropped** (not approved).
- New refresh / polling mechanism — **postponed** to a future approved round; must be scoped (current project / open folders / active work area).
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
- A new Google Drive upload mechanism without an explicit decision —
  **not approved**.
- Google Drive fallback when ACC / File Server is missing — **not approved**.
- Mixing Gmail and Google Drive as the same Storage Destination —
  **dropped**.
- Treating Gmail as a write / management destination for project files —
  **dropped** (Gmail is read-only ingestion).
- Using `SiOffice.GoogleConnector` / `GoogleService` as a general business
  engine for project files / Storage Destination / workflow / PlanReview /
  AI — **dropped**.
- Google Sheets as a Storage Destination or general business source of
  truth for project files — **not approved**.

## Relevant terms / search terms
ProjectFile, ProjectAlternative, ProjectFileInstance, runtime projection, ProjectWork, "בעבודה 2", Storage Destination, MoveToProject, OpenQuoteProject, IProcessActionHandler, UpsertInstanceAsync, AccInboxLayout, AccInboxReconciliationService, SiInbox.Move.TargetAltId, focused refresh, scoped polling, initial full scan, project entry scan, rescan, alternative normalization, illegal-character cleanup, duplicate prevention, name-based linkage, alternative merge, alternative delete, maintenance action, invalid alternative name warning.

## Relevant code areas (informational)
- `ProjectWork` / `ProjectWorkView`
- `ProjectFileFilingService`
- `MoveToProjectProcessActionHandler`
- `AccInboxReconciliationService`
