# ACC System Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for the ACC domain.
- **Scope:** Autodesk Construction Cloud (ACC) integration: ACC Inbox, project folders, files, custom attributes, viewer URLs, service boundary.

## Purpose
Define the binding principles for how the application interacts with ACC, what is authoritative, and what must not be done.

## Source of truth
- **ACC is the source of truth** for files that have been uploaded.
- **DB is a cache / helper** and never proof on its own that a file still exists in ACC.
- Custom attribute values on ACC items are the source of truth for ACC-side metadata. The DB mirror is a helper only.

## Core principles
1. Before any operation that depends on a file existing in ACC, verify existence via ACC reconciliation. Do not trust DB-only state.
2. A failed metadata read from ACC is **not** proof that the file is missing. Reconciliation must confirm physical existence before marking `MissingInAcc` / `StaleAccReference`.
3. Never build an ACC Viewer URL from DB identifiers as a fallback. Viewer / opening data must come from ACC reconciliation.
4. ACC Inbox layout is centralized in `AccInboxLayout`:
   - **Active layout (2026-05-26 round):** `_Inbox/THREAD_<ThreadKey>/MSG_<MessageKey>/`,
     organised by global email thread identity. `ThreadKey` is derived from
     RFC822 threading headers (`References`, `In-Reply-To`, `Message-ID`) and
     `MessageKey` is derived from RFC822 `Message-ID`. **Folder names must
     not be derived from Gmail mailbox-local `message.id` or `threadId`.**
   - Inside each message folder: `00_Email.pdf`, `manifest.json`, and an
     `Attachments/` child folder for regular attachments.
   - The previous flat `MSG_<MessageKey>/…` layout (without the
     `THREAD_<ThreadKey>` parent) is **superseded**. The previous
     `Year/Month/...` partitioning is also **superseded**; no silent
     fallback to it is allowed. Legacy ACC folders are not migrated
     automatically \u2014 see
     [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md)
     (Gap 3 + *Cleanup / postponed items*).
5. A sidecar `.json` (e.g. `<file>.pdf.json`) is **not** an extension conflict and must not be treated as such.
6. Custom attribute definitions (`SiInbox.*`) are provisioned only in the dedicated ACC Inbox project / folder via the existing `Bim360Service.EnsureCustomAttributeDefinitionsAsync`. Definitions are not auto-created from `SetItemCustomAttributesAsync`.
7. The move-target alternative attribute name is `SiInbox.Move.TargetAltId`. The legacy long form `SiInbox.Move.TargetProjectAlternativeId` must not be reintroduced.
8. Service boundary:
   - `SiOffice.AccService` is the central service for ACC operations in service mode (privileged operations, server-side / two-legged paths if any, ensure of projects / folders / resources per policy).
   - When `AccService:BaseUrl` is configured, remote WPF clients call the service instead of running local privileged ACC orchestration (no local `AccUserBootstrapService.ProvisionUsersAsync`).
   - `SiOffice.AutodeskConnector` is the **technical connector layer** (REST / API wrappers, token / API plumbing) used by the service / privileged paths. It is **not** a business engine.
   - `AccInboxReconciliationService` is the **application/domain service** that verifies ACC Inbox reality (`MissingInAcc` / `StaleAccReference` decisions, merging DB + ACC info for status). It does **not** perform upload, project filing, ACC project creation, workflow decisions, or DB-only fallbacks.
   - **UI / WPF must not bypass these boundaries** for business decisions. UI calls Application Services / ViewModels, which then call the appropriate service. UI does not call `SiOffice.AutodeskConnector` directly for business decisions.
   - See [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Architecture/ServiceCatalog-2026-05-26.md) for the full service map.
9. `OpenQuoteProject` does not have to create an ACC project. `MoveToProject` performs the ACC ensure at move time, and only required folders are created.
10. Legacy DB-only open / move fallbacks are disabled or clearly marked; they are not re-enabled without explicit safety review.

## ACC service boundaries (added 26.05.2026)

This section is the authoritative split of responsibilities between the
ACC-related services. It expands core principle §8 and is the ACC-domain
companion to
[`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Architecture/ServiceCatalog-2026-05-26.md).

### `SiOffice.AccService`

- **Role:** central service for ACC operations that require **elevated
  privileges**, run in **service mode**, or should not run directly on a
  user's machine.
- **Responsibility:**
  - Privileged ACC operations.
  - Server-side / two-legged operations, where they exist.
  - `ensure` of projects / folders / resources per policy.
  - Serving remote WPF clients when `AccService:BaseUrl` is configured.
- **Service-mode rule:** when `AccService:BaseUrl` is configured, a remote
  WPF client **must not** run local privileged ACC orchestration; it calls
  `SiOffice.AccService` instead.
- **Does not do:** UI logic; business workflow decisions; Gmail decisions;
  attachment identification; building or owning the `ProjectFileInstance`
  runtime projection.

### `SiOffice.AutodeskConnector`

- **Role:** **technical connector** to Autodesk / ACC APIs. A tool, not a
  business engine.
- **Responsibility:**
  - REST / API calls to Autodesk.
  - Technical wrappers around Autodesk endpoints.
  - Token / API plumbing within the defined boundaries.
- **Does not do:** business decisions; source-of-truth decisions; workflow;
  UI; filing policy.

### `AccInboxReconciliationService`

- **Role:** **application / domain service** that verifies ACC Inbox
  reality.
- **Responsibility:**
  - Verify whether a file actually exists in ACC.
  - Decide `MissingInAcc` / `StaleAccReference` based on the ACC result.
  - Merge DB cache and ACC information into a coherent status.
  - Return that status to the email layer / UI.
- **Does not do:** new uploads; project filing; ACC project creation;
  workflow decisions; DB-only fallback for existence or open.

### UI / WPF boundary

- The UI **does not** call `SiOffice.AutodeskConnector` directly for
  business decisions.
- The UI calls **Application Services / ViewModels**, and those call the
  appropriate service (`SiOffice.AccService`,
  `AccInboxReconciliationService`, `MoveToProject*` handler, etc.).
- The split is:
  - **UI** displays and triggers.
  - **Services** decide and execute.
  - **`SiOffice.AutodeskConnector`** performs technical API calls.
  - **`SiOffice.AccService`** performs privileged / remote service-mode
    operations.

### Service-mode rule (recap)

- `AccService:BaseUrl` configured → remote WPF client routes ACC privileged
  work to `SiOffice.AccService`. No parallel local privileged path.
- `AccService:BaseUrl` not configured → the allowed local path must be
  explicit; **no new parallel path is created without an explicit
  decision**.

### Forbidden across these boundaries

- Creating another parallel ACC service.
- Bypassing `SiOffice.AccService` from the UI when in service mode.
- Putting business decisions inside `SiOffice.AutodeskConnector`.
- DB-only fallback to open or validate an ACC file.
- Mixing **upload / reconciliation / filing** as if they were one service.
- Using `AccInboxReconciliationService` to perform upload or filing.

## Storage Destination and ACC (added 26.05.2026)
- ACC is one valid **Storage Destination** value for a managed file (see
  `ProjectFilesPrinciples`). When a file's Storage Destination is ACC, ACC is
  its physical source of truth.
- The system does not infer Storage Destination from "where a copy was
  found". It reads the configured Storage Destination.
- ACC custom attributes do not change a file's Storage Destination
  automatically.

## Metadata source of truth (added 26.05.2026)
- **ACC custom attributes** are the source of truth for metadata that must
  travel with the ACC item itself: originating email, `Message-ID` /
  `MessageKey` reference, Inbox source, `ProjectFile` reference written on
  the item, move-target reference (`SiInbox.Move.TargetAltId`).
- ACC custom attributes are **not** a substitute for the DB as the business
  source of truth.
- `manifest.json` written into ACC Inbox folders is an **audit snapshot**
  only — it does not replace the DB, Storage Destination existence checks,
  or Gmail RFC822 headers.

## What we do not do now
- Do not derive Viewer URLs from cached DB identifiers.
- Do not auto-provision custom attribute definitions from generic write paths.
- Do not create another parallel ACC service.
- Do not bypass `SiOffice.AccService` from the UI when in service mode.
- Do not put business decisions inside `SiOffice.AutodeskConnector`.
- Do not use a DB-only fallback to open or validate an ACC file.
- Do not mix upload / reconciliation / filing under one ambiguous service.
- Do not use `AccInboxReconciliationService` to perform upload or filing.
- Do not change schema, migrations, ModelSnapshot, `ProjectFileInstance`, `Refile`, `MoveToProject` service, ACC metadata write path, `SetItemCustomAttributesAsync`, or `TokenProvider` as part of ACC documentation/principles work.
- Do not introduce new ACC bootstrap behavior at startup.

## Dropped / cancelled / postponed
- DOM / scraped UI of ACC web as a source of truth — dropped.
- DB-only file existence proof — dropped.
- Reintroduction of `SiInbox.Move.TargetProjectAlternativeId` — dropped.
- ACC Inbox folder names derived from Gmail mailbox-local `message.id` /
  `threadId` — dropped.
- An additional parallel ACC service alongside `SiOffice.AccService` — **dropped** (not approved).
- Bypassing `SiOffice.AccService` from the UI in service mode — **dropped**.
- Business decisions inside `SiOffice.AutodeskConnector` — **dropped**.
- DB-only fallback for opening / validating an ACC file — **dropped**.
- Mixing upload / reconciliation / filing into one ambiguous service — **dropped**.
- Flat `MSG_<MessageKey>\…` ACC Inbox layout as the **final** target —
  replaced by the active `_Inbox/THREAD_<ThreadKey>/MSG_<MessageKey>/`
  layout (2026-05-26 round). `Year/Month` partitioning is also
  superseded. Legacy ACC folders are not migrated automatically; see
  [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md)
  (Gap 3 + *Cleanup / postponed items*).
- Deep documentation of every ACC API call — postponed.

## Relevant terms / search terms
ACC, BIM 360, AccInboxLayout, AccInboxReconciliationService, Bim360Service, EnsureCustomAttributeDefinitionsAsync, SetItemCustomAttributesAsync, SiInbox.Move.TargetAltId, ProjectFileInstance, ShowAttachmentInAccAsync, MoveToProjectProcessActionHandler, MissingInAcc, StaleAccReference, AccService, AccService:BaseUrl, service mode, SiOffice.AccService, SiOffice.AutodeskConnector, ACC service boundary, technical connector, privileged ACC, TokenProvider, two-legged, three-legged.

## Relevant code areas (informational)
- `SiOffice.AccService`
- `SiOffice.AutodeskConnector`
- `Bim360Service`
- `AccInboxLayout`, `AccInboxReconciliationService`
- `MoveToProjectProcessActionHandler`
