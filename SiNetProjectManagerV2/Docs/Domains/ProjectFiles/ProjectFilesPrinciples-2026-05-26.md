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
5. `Version` segment in the filename is not a version tracker.
7. External files and uploaded attachments share the same filing pipeline (`IProcessActionHandler` dispatcher), not parallel ad-hoc paths.
8. Refile and MoveToProject paths are protected:

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
  implementation (for example Google Drive in its current state), a future
  **focused refresh / targeted polling** mechanism will be required.
- Any future refresh must be **scoped**:
  - to the **current project**,
  - to the **open folders**,
  - to the **active work area**.
- Broad, system-wide polling across all projects is **not** allowed.
- No new refresh / polling mechanism is added in this documentation round.
  It is described here as a principle for a future approved round.

**Forbidden:**

- Creating a dedicated DB table that holds one row per every possible
  `ProjectFileInstance`.
- Treating `ProjectFileInstance` as a source of truth.
- Using `ProjectFileInstance` instead of a Storage Destination existence
  check.
- Persisting runtime projection state as if it were permanent business data.
- Wide, system-wide polling.
- Adding a new refresh mechanism in this documentation round.

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
- Do not change `ProjectFileInstance` model, table, or FK layout in this round.
- Do not add a new persistent DB table for `ProjectFileInstance` rows.
- Do not change refile flows or `UpsertInstanceAsync`.
- Do not add a refresh / polling mechanism in this round.
- Do not add startup-time browser authorization or unrelated ACC bootstrap behavior for filing.
- Do not write metadata that references missing definition IDs.

## Dropped / cancelled / postponed
- `ProjectFileInstance` as a permanent DB entity / cache per every possible file instance — **dropped** as a principle (replaced by the runtime projection model above).
- Persisting runtime projection state as if it were stable business data — **dropped**.
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

## Relevant terms / search terms
ProjectFile, ProjectAlternative, ProjectFileInstance, runtime projection, ProjectWork, "בעבודה 2", Storage Destination, MoveToProject, OpenQuoteProject, IProcessActionHandler, UpsertInstanceAsync, AccInboxLayout, AccInboxReconciliationService, SiInbox.Move.TargetAltId, focused refresh, scoped polling.

## Relevant code areas (informational)
- `ProjectWork` / `ProjectWorkView`
- `ProjectFileFilingService`
- `MoveToProjectProcessActionHandler`
- `AccInboxReconciliationService`
