# Database Identity Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for cross-system identity.
- **Scope:** Identifiers and their authoritative meaning across DB, ACC, Gmail, and Tasks.

## Purpose
Define which identifier is authoritative for each entity, where it lives, and how systems are linked without ambiguity.

## Source of truth (per identity)
| Concept | Authoritative system | Identifier |
| --- | --- | --- |
| Email (business identity) | Gmail / RFC822 headers | **RFC822 `Message-ID`** |
| Email (mailbox-local, runtime only) | Gmail | Gmail `message.id` (mailbox-scoped; **not persisted as business data**) |
| Email thread (mailbox-local, runtime only) | Gmail | Gmail `threadId` (mailbox-scoped; **not persisted as business data**) |
| Email thread (global / business) | Derived from RFC822 headers | `ThreadKey` derived from `References` / `In-Reply-To` / `Message-ID` |
| Internal email row | DB | `MessageUniqueId` / `MessageKey` (derived; helper) |
| Project file (logical) | DB | `ProjectFile` (stable definition of file type / target / template) |
| Project alternative | DB | `ProjectAlternative` (persisted, **dynamic per project**, **name-based linkage** to files: may be auto-created from real file names / filing actions after normalization + illegal-character cleanup + duplicate prevention; **not** auto-removed; delete/merge only via dedicated maintenance action with full project scan and filename rename/remap as needed) |
| Project file location (runtime view) | Built at runtime by services | `IProjectFileLocationResolver` (**runtime / session resolver only** with in-memory cache; **no `ProjectFileInstance` entity / DB row** — removed in Stage 9E.4 / Gap 9; actual storage state in ACC / File Server / Google Drive is the source of truth) |
| File (after upload) | ACC | ACC item URN + custom attributes |
| Task | DB | Task identity (workflow domain) |

## Core principles
1. **RFC822 `Message-ID` is the business identity** of an email. Gmail `message.id` is mailbox-local and must not be used as a stable cross-system identifier.
2. `MessageKey` / `MessageUniqueId` are derived helpers; centralized formatting must remain the single source of truth (`MessageKeyGenerator`-style helpers).
3. After upload, the **ACC item identity (URN) is authoritative** for the file (when ACC is the configured Storage Destination). There is **no `ProjectFileInstance` row** \u2014 the entity was removed in Stage 9E.4 (Gap 9); runtime location is resolved by `IProjectFileLocationResolver` (session cache only) over the `IFileStore` implementations. See `ProjectFilesPrinciples`.
4. DB never alone proves a file still exists in ACC (see ACC principles).
5. Deduplication on import is based on RFC822 `Message-ID` + canonical key, never on Gmail `message.id` alone.
6. `Version` segment in the file naming convention is **not** a version tracker. New files always get `Version = 1`. ACC manages version history natively.
7. Identity values must be set via the canonical creation paths; do not invent identity values in fallback / recovery code.

## DB persistence rules for Gmail identifiers (added 26.05.2026)
- The DB stores **only global / business / system data**.
- The DB **must not** persist as a source of truth or as a permanent identifier:
  - `Gmail message.id`
  - `Gmail threadId`
  - any other Gmail identifier that is scoped to a specific user's mailbox.
- Reason: `Gmail message.id` and `Gmail threadId` are **mailbox-local**. The
  same physical message / thread can carry different values in different
  users' mailboxes.
- The DB **does** persist (per email):
  - RFC822 `Message-ID`
  - `In-Reply-To`
  - `References`
  - Email business data
  - Links to project / task / file
  - User decisions and workflow state
  - Links to ACC / `ProjectFile` / `ProjectAlternative` (no `ProjectFileInstance` link — entity removed in Stage 9E.4 / Gap 9)
- When a local Gmail identifier is required for an action (open in WebView2,
  read via Gmail API, download attachments, display thread), it is
  **resolved on demand** against the current user's mailbox via
  `rfc822msgid:{Message-ID}` and used **only for the current operation**. It
  is not written back to the DB.
- If the message is not found in the current user's mailbox: surface a
  user-visible error; do **not** create a new email row, a new ACC Inbox, or
  fall back to another user's identifiers.

## Folder names and derived identifiers (added 26.05.2026)
- DB records and ACC Inbox folder names must be based on **global business
  identifiers**, not on mailbox-local Gmail identifiers.
- `MessageUniqueId` is derived from RFC822 `Message-ID`.
- `MessageKey` is derived from `MessageUniqueId`.
- Any existing fallback to `gmail:{message.id}` is **legacy / compatibility
  only / implementation gap**, not a desired principle.

## Global thread identity (added 26.05.2026)
- A global thread identity (`ThreadKey`) is derived from the RFC822 threading
  headers `References`, `In-Reply-To`, and `Message-ID`, in that priority
  order (see Email principles §6.3).
- `Gmail threadId` is **not** the thread identity and must not be persisted
  as one.

## Metadata source of truth (added 26.05.2026)
Each kind of metadata has one defined owner; the DB is one of several owners,
not the universal owner:
- **Gmail / RFC822 headers** — email identity and global thread relations.
- **DB** — business process: projects, tasks, workflow, `ProjectFile` /
  `ProjectAlternative`, user decisions, links. **No `ProjectFileInstance`**
  (entity removed in Stage 9E.4 / Gap 9). The DB does **not** store
  physical-file-existence truth.
- **Storage Destination** — physical-existence proof of a file (see
  `ProjectFilesPrinciples`).
- **ACC custom attributes** — metadata that must travel with the ACC item
  (origin email, `Message-ID` / `MessageKey` reference, Inbox source,
  `ProjectFile` reference, move target).
- **`manifest.json` / sidecar JSON** — audit snapshot only; not a substitute
  for the DB, Storage Destination, or Gmail headers.
- **UI / DOM** — never a source of truth.

## What we do not do now
- Do not treat Gmail `message.id` as a permanent business identifier.
- Do not treat Gmail `threadId` as a business thread identifier.
- Do not persist mailbox-local Gmail identifiers in the DB as business data.
- Do not derive ACC viewer URLs from DB identifiers.
- Do not infer file existence from any DB row (there is no `ProjectFileInstance` to consult; physical existence is established by the actual Storage Destination backend via `IFileStore`).
- Do not reintroduce a `ProjectFileInstance` entity / `DbSet` / table (removed in Stage 9E.4 / Gap 9).
- Do not persist the `IProjectFileLocationResolver` session cache to the DB.
- Do not manually edit EF migration files, Designer.cs, or ModelSnapshot.

## Dropped / cancelled / postponed
- Gmail `message.id` as canonical business identity — dropped.
- Gmail `threadId` as canonical business thread identity — dropped.
- Persisting any mailbox-local Gmail identifier as DB business data — dropped.
- New version tracker on filename — dropped (ACC manages versions).
- `ProjectFileInstance` as a persisted DB entity — **removed** (Stage 9E.4 / Gap 9). Replaced at runtime by `IProjectFileLocationResolver` (session cache only); physical existence is the actual storage state in ACC / File Server / Google Drive.
- `EmailInboxAttachment.ProjectFileInstanceId` and `InspectionReportDrawing.FileInstanceId` columns — **removed** (migration `RemoveProjectFileInstanceTable`; `Update-Database` is user-run).
- Cross-domain identity refactor — postponed.

## Relevant terms / search terms
RFC822, Message-ID, In-Reply-To, References, rfc822msgid, MessageUniqueId, MessageKey, MessageKeyGenerator, ThreadKey, ThreadId, ProjectFile, ProjectAlternative, ProjectFileInstance, ACC URN, dedup, mailbox-local, Storage Destination, metadata source of truth.
