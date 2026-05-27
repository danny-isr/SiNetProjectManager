# Documentation vs Implementation — Gap Register

- **Created:** 26.05.2026
- **Status:** Active backlog / gap register — **not** a task list for immediate execution.
- **Scope:** Centralised, append-only record of known or suspected gaps between
  the approved Principles documents under `Docs\Domains\` and the current
  application implementation.

> This document **does not introduce any code change**. No code, DB, schema,
> migration, ModelSnapshot, or disabled mechanism is touched as part of
> creating or updating this register. Each entry is a candidate for a future
> approved round.

## How to read / update this file

- Each gap is a numbered entry with the following fields:
  - **Date identified** — when the gap was recorded.
  - **Domain** — which Principles document the gap belongs to.
  - **Desired principle** — the approved rule (with link / pointer).
  - **Current known / suspected state** — what the code is believed to do today.
  - **Impact / risk** — why it matters.
  - **Status** — one of:
	`Open` · `Needs code verification` · `Needs migration decision` · `Postponed` · `Fixed` · `Not applicable`.
  - **Relevant files / classes** — informational only, when known.
- New gaps are **appended** at the end. Existing entries are updated in place;
  do not delete entries — change `Status` instead.
- Resolving a gap requires a separate approved code round; closing it here is
  documentation-only and must reference the round that fixed it.

## Authoring rule

Before adding a new gap, check whether an equivalent entry, mechanism, or
document already exists. Prefer extending an existing entry over creating a
parallel one. Do not duplicate gaps already covered by an existing decision
or principles document.

---

## Initial gaps (26.05.2026)

### Gap 1 — Gmail local identifiers may still be persisted / used as business data

- **Date identified:** 26.05.2026
- **Domain:** Email, Database Identity.
- **Desired principle:**
  The DB does **not** persist `Gmail message.id` or `Gmail threadId` as
  business identifiers. See
  [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Domains/Email/EmailSystemPrinciples-2026-05-26.md) §2.5 and
  [`Domains\DatabaseIdentity\DatabaseIdentityPrinciples-2026-05-26.md`](../Domains/DatabaseIdentity/DatabaseIdentityPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Fields and types such as `EmailInfo.MessageId`, `EmailInfo.ThreadId`,
  `GmailThreadId`, and `MessageUniqueId` may still carry / persist
  mailbox-local Gmail identifiers, or may be used as if they were stable
  business identities. The persistence and read paths have not been audited
  against the new principle in this round.
- **Impact / risk:**
  Cross-user inconsistency: the same physical email can be represented by
  different identifiers per mailbox. Risk of mis-deduplication, mis-linking
  of tasks to emails, and incorrect ACC Inbox folder naming.
- **Status:** Needs code verification.
- **Relevant files / classes (informational):**
  `SiOffice.GoogleConnector\GoogleService.cs` (`EmailInfo`, `MapMessageToInfo`,
  `MapMessageToInfoMetadataOnly`),
  `SiNetSQL\Models\EmailInboxMessage.cs`,
  `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`.

### Gap 2 — `MessageUniqueId` may still fall back to `gmail:{message.id}`

- **Date identified:** 26.05.2026
- **Domain:** Email, Database Identity.
- **Desired principle:**
  `MessageUniqueId` is derived from RFC822 `Message-ID`. Any
  `gmail:{message.id}` form is legacy / compatibility only, not the desired
  production path.
- **Current known / suspected state:**
  `MessageKeyGenerator.GetMessageUniqueId(internetMessageId, gmailMessageId)`
  prefers the RFC822 value when supplied and falls back to
  `gmail:{gmailMessageId}` otherwise. It has not been verified whether the
  production ingestion path always supplies the RFC822 value.
- **Impact / risk:**
  When the fallback is used in practice, the resulting identifier (and any
  derived `MessageKey` / ACC Inbox folder name) becomes mailbox-local in
  effect, undermining the cross-user identity guarantee.
- **Status:** Needs code verification.
- **Relevant files / classes (informational):**
  `SiNetSQL\Services\EmailIngestion\MessageKeyGenerator.cs`,
  `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`.

### Gap 3 — ACC Inbox layout is currently message-only (no `THREAD_<ThreadKey>` parent)

- **Date identified:** 26.05.2026
- **Domain:** ACC, Email.
- **Desired principle:**
  ACC Inbox is organised as `THREAD_<ThreadKey>\MSG_<MessageKey>\…`. See
  [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Domains/Email/EmailSystemPrinciples-2026-05-26.md) §6.3 and
  [`Domains\ACC\AccSystemPrinciples-2026-05-26.md`](../Domains/ACC/AccSystemPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  The current layout in `AccInboxLayout` is believed to be message-only
  (`MSG_<MessageKey>\…`) without a `THREAD_<ThreadKey>` parent. Not yet
  verified against the latest code in this round.
- **Impact / risk:**
  Messages belonging to the same business thread are not co-located in ACC,
  hindering thread-based navigation, audit, and future thread-level
  operations. Migration of existing folders will require a separate decision.
- **Status:** Open — Needs migration decision.
- **Relevant files / classes (informational):**
  `AccInboxLayout`, `AccInboxReconciliationService`,
  `MoveToProjectProcessActionHandler`.

### Gap 4 — Global `ThreadKey` derivation is not implemented

- **Date identified:** 26.05.2026
- **Domain:** Email, Database Identity.
- **Desired principle:**
  `ThreadKey` is derived from RFC822 threading headers in this priority:
  first `Message-ID` in `References`, else `In-Reply-To`, else the message's
  own `Message-ID`.
- **Current known / suspected state:**
  No such derivation helper is known to exist. `Gmail threadId` may be used
  for grouping in places where a global `ThreadKey` should be used.
- **Impact / risk:**
  Without a global `ThreadKey`, the target ACC Inbox layout (Gap 3) cannot be
  implemented, and cross-user thread identity cannot be guaranteed.
- **Status:** Open.
- **Relevant files / classes (informational):**
  Future helper alongside `MessageKeyGenerator` (no new mechanism created in
  this round).

### Gap 5 — Storage Destination model: documentation vs code alignment

- **Date identified:** 26.05.2026
- **Domain:** Project Files, Database Identity, ACC.
- **Desired principle:**
  Every managed file has one binding Storage Destination value: `ACC`,
  `File Server`, `Google Drive`, or `Gmail` (read-only ingestion only).
  See [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../Domains/ProjectFiles/ProjectFilesPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Where Storage Destination is represented in the DB / code (table / column /
  enum), and whether all four values are correctly modelled with Gmail as
  read-only, has not been verified in this round. Google Drive upload is
  postponed.
- **Impact / risk:**
  Without a single, explicit Storage Destination per file, the system may
  rely on heuristics ("where we found a copy") instead of authoritative
  configuration, leading to ambiguous "source of truth" for physical files.
- **Status:** Needs code verification.
- **Relevant files / classes (informational):**
  `ProjectFileFilingService`, `ProjectFileInstance` model, ACC upload paths.

### Gap 6 — Metadata source-of-truth alignment

- **Date identified:** 26.05.2026
- **Domain:** Email, ACC, Database Identity, Project Files.
- **Desired principle:**
  Each metadata kind has one owner: Gmail/RFC822 headers, DB, Storage
  Destination, ACC custom attributes, `manifest.json` (audit snapshot only),
  UI (never authoritative).
- **Current known / suspected state:**
  Metadata is believed to be written in several places (DB, ACC custom
  attributes, `manifest.json` sidecars). The mapping of "owner vs copy" has
  not been audited and may include duplicated truth or unclear ownership.
- **Impact / risk:**
  Conflicting metadata between DB / ACC / `manifest.json`; risk of code
  treating an audit snapshot or a UI value as authoritative.
- **Status:** Needs code verification.
- **Relevant files / classes (informational):**
  ACC custom attribute write paths (`SetItemCustomAttributesAsync`),
  `manifest.json` writers under `AccInboxLayout`-aware services, DB models
  involved in email/file linking.

### Gap 7 — Gmail WebView2 DOM focusing / expansion is principle only

- **Date identified:** 26.05.2026
- **Domain:** UI, Email.
- **Desired principle:**
  DOM via `ExecuteScriptAsync` may be used **only** as a display helper
  (scroll to / expand a specific message), never as a business source of
  truth. See [`Domains\UI\UiPrinciples-2026-05-26.md`](../Domains/UI/UiPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  A DOM-based focusing / expansion helper may not yet exist, or may exist
  only partially. No business logic should depend on the DOM.
- **Impact / risk:**
  Low — display only. Risk would arise if DOM-derived data were promoted to
  a business decision source.
- **Status:** Open.
- **Relevant files / classes (informational):**
  `SiNetProjectManagerV2\WPFUserControl\WebView2Helper.cs`,
  `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml.cs`.

### Gap 8 — `GmailVisibleAttachmentsDomExtractor` must remain disabled

- **Date identified:** 26.05.2026
- **Domain:** Email.
- **Desired principle:**
  `GmailVisibleAttachmentsDomExtractor` is **DISABLED / diagnostic disabled /
  not used currently / candidate for future removal**. The Gmail DOM is not
  a source of truth for attachments.
- **Current known / suspected state:**
  Believed to be disabled and not active in any production path. Should be
  re-verified that no call site has been re-enabled.
- **Impact / risk:**
  Re-enabling would reintroduce DOM-based "truth" for attachments, in
  violation of the Email principles.
- **Status:** Needs code verification.
- **Relevant files / classes (informational):**
  `SiNetProjectManagerV2\Services\GmailVisibleAttachmentsDomExtractor.cs`.

### Gap 9 — `ProjectAlternative` lifecycle and `ProjectFileInstance` runtime-projection alignment

- **Date identified:** 26.05.2026 (extended same date to cover `ProjectAlternative` lifecycle and initial-full-scan-on-project-entry semantics).
- **Domain:** Project Files, Database Identity, UI (`ProjectWork`).
- **Desired principle:**
  - `ProjectFile` is a relatively stable DB entity defining file type /
    target / template.
  - `ProjectAlternative` is a **persisted DB entity but dynamic per
    project**. It **may** be auto-created from a real file name or a
    filing action after **normalization + duplicate prevention**, but it
    is **not** auto-removed when files disappear. Removal, merge, or
    consolidation of alternatives is a **dedicated maintenance action**
    only.
  - `ProjectFileInstance` is a **runtime projection** built for the
    selected project from DB definitions (`Project`, `ProjectFolder`,
    `ProjectFile`, `ProjectAlternative`, `Storage Destination`) plus the
    current Storage Destination state (ACC / File Server / Google Drive).
    It is not a permanent DB entity per file instance and not a source of
    truth.
  - When entering a project, the system performs a **full scan scoped to
    that project only** to build the projection. After the initial scan,
    updates come from internal events and **focused / targeted refresh**;
    a **full rescan** is performed only on **explicit user request** or a
    **dedicated maintenance / system action**.
  - See
    [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../Domains/ProjectFiles/ProjectFilesPrinciples-2026-05-26.md)
    and
    [`Domains\DatabaseIdentity\DatabaseIdentityPrinciples-2026-05-26.md`](../Domains/DatabaseIdentity/DatabaseIdentityPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  - Older documentation and parts of the code treat `ProjectFileInstance`
    as a persisted model / DB cache helper (with `UpsertInstanceAsync`,
    `ProjectFileInstanceId`, model/table/FK layout). Not verified in this
    round whether `ProjectWork` already behaves as a runtime projection or
    still depends on persisted instance rows.
  - It has **not been verified** whether `ProjectAlternative` creation in
    code applies consistent normalization / trimming / duplicate
    prevention, nor whether any path auto-deletes or auto-merges
    alternatives. **Needs code verification.**
  - It has **not been verified** whether project-entry triggers a single
    scoped full scan, whether any recurring automatic full rescan exists
    on an open project, and whether refresh paths are properly scoped.
    **Needs code verification.**
- **Impact / risk:**
  - Creating too many persistent file-instance rows in the DB.
  - Stale project file state when storage changes outside the app.
  - **Duplicate `ProjectAlternative` rows** under trivial formatting
    differences.
  - **Accidental deletion / merge of `ProjectAlternative` rows** based on
    transient absence of files.
  - Unclear source of truth between the runtime projection and DB
    business data.
  - Risk of unscoped or recurring full scans hurting performance and
    blurring the runtime-projection boundary.
- **Status:** Needs code verification / Needs architecture alignment.
- **Relevant areas (informational):**
  `ProjectWork` / `ProjectWorkView`, `ProjectFileFiling`,
  `ProjectFile`, `ProjectAlternative` (creation / normalization /
  duplicate prevention / maintenance paths),
  `ProjectFileInstance` (model and consumers), `ProjectFileFilingService`,
  `MoveToProject` / `MoveToProjectProcessActionHandler`,
  `UpsertInstanceAsync`, Storage Destination services, project-entry /
  initial-scan paths.
- **Notes:**
  - A future, **focused / scoped** refresh mechanism (current project,
    open folders, active work area) may be needed for storage sources
    without reliable events (e.g. Google Drive in its current state, a
    File Server without a reliable watcher, or ACC when explicit
    reconciliation is required).
  - **No new refresh / polling mechanism is added in this round.**
  - Broad, system-wide polling is **not** approved.
  - Automatic recurring full rescan of an already-open project is **not**
    approved.
  - Auto-removal / auto-merge of `ProjectAlternative` is **not** approved
    (maintenance action only).

### Gap 10 — `ProjectAlternative` maintenance: merge/delete requires dedicated flow and full project scan

- **Date identified:** 26.05.2026
- **Domain:** Project Files, Database Identity.
- **Desired principle:**
  `ProjectAlternative` is created automatically from real file names /
  filing actions after **normalization** (trim leading / trailing
  whitespace), **illegal-character cleanup** per existing alternative-name
  rules, and **duplicate prevention** (no duplicate after normalization /
  cleanup). Invalid or suspicious names must surface a **user-visible
  warning**, not be ignored silently. `ProjectAlternative` is **not**
  deleted automatically; deletion requires a **dedicated maintenance
  action** and a **full project scan** verifying no file still uses that
  alternative name. **Merge** is a dedicated user-driven operation with
  user-selected **source** and **target** alternatives, **remap** of usages,
  and **filename rename/remap** where required for consistency; the source
  alternative may be removed only after it has no remaining file usage.
  Alternative linkage is **name-based**, not a hard ID relationship, so
  file names must be considered when merging or deleting alternatives.
  See
  [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../Domains/ProjectFiles/ProjectFilesPrinciples-2026-05-26.md)
  (sections "ProjectAlternative — persisted but dynamic per project" and
  "Project entry — initial full scan").
- **Current known / suspected state:**
  - It has **not been verified** whether `ProjectAlternative` creation in
    code consistently applies normalization, illegal-character cleanup,
    and duplicate prevention.
  - It has **not been verified** whether invalid / suspicious alternative
    names surface a user-visible warning rather than being silently
    accepted.
  - It has **not been verified** whether any path auto-deletes
    `ProjectAlternative` rows.
  - **No dedicated maintenance flow** for deleting or merging
    `ProjectAlternative` is known to exist today; in particular, no
    user-driven merge UI with source/target selection and filename
    rename/remap, and no delete flow guarded by a verified full project
    scan, is known to be implemented.
- **Impact / risk:**
  - Duplicate `ProjectAlternative` rows under trivial formatting
    differences.
  - Invalid alternative names silently accepted into the DB.
  - Accidental recreation of an alternative after deletion, because files
    still carry its name (name-based linkage).
  - Deleting a DB row while files still reference the alternative name.
  - Unclear / inconsistent merge behavior when an admin attempts to
    consolidate alternatives.
- **Status:** Needs code verification / Needs maintenance-flow design.
- **Relevant areas (informational):**
  `ProjectFileFilingService`, `MoveToProject` /
  `MoveToProjectProcessActionHandler`, external/uploaded file ingestion
  paths, `ProjectAlternative` create/normalize/cleanup helpers,
  project-entry / initial-scan paths, future `ProjectAlternative`
  maintenance UI (not in this round).
- **Notes:**
  - **No new merge / delete mechanism is created in this round.** This
    entry only documents the desired principle and the gap.
  - Auto-removal / auto-merge of `ProjectAlternative` is **not** approved.
  - Silent acceptance of invalid alternative names is **not** approved.

### Gap 11 — Service boundaries, ViewModel logic, and Service Catalog alignment

- **Date identified:** 26.05.2026
- **Domain:** Architecture.
- **Desired principle:**
  Meaningful business logic lives in Services / Use Cases / Application
  Services / Domain Services, not in `ViewModel`s. Before adding a new
  service, the
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md)
  must be checked and an existing service reused or extended where
  possible. `ProjectFileInstance` as a *persisted placement tracker* is
  **superseded** by the runtime-projection principle and must not be
  revived. See
  [`Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](../Domains/Architecture/ArchitecturePrinciples-2026-05-26.md)
  § *Service / Use Case boundaries*, § *Business anchors*, § *Workflow vs
  Task*, § *Documentation alignment rule*, and § *Manual migration rule*.
- **Current known / suspected state:**
  - Several `ViewModel`s may still hold non-trivial business decisions
    (filing routing, identity derivation, workflow shortcuts). **Needs
    code verification.**
  - The real code name behind the *concept name* `ProjectWorkService` (the
    builder of the `ProjectFileInstance` runtime projection for the
    selected project) has not been verified in this round. The
    responsibility may be split across multiple services or partially
    inside a `ViewModel`.
  - ACC operations are reachable from at least three places
    (`SiOffice.AccService`, `SiOffice.AutodeskConnector`,
    `AccInboxReconciliationService`); it is not verified whether all
    callers go through the boundary defined in `ArchitecturePrinciples`
    §3 (service mode when `AccService:BaseUrl` is configured).
  - Any remaining `UpsertInstanceAsync` / `ProjectFileInstanceId` paths
    that imply persisted-placement-tracker semantics are legacy and
    contradict the active runtime-projection principle (also tracked in
    Gap 9 / 10).
- **Impact / risk:**
  - Business rules drift into the UI and become hard to test or reuse.
  - Parallel / duplicate services appear because the Service Catalog is
    not consulted.
  - Reviving the old `ProjectFileInstance` placement-tracker semantics by
    accident, contradicting the active principle.
  - ACC operations bypass the service boundary in service mode.
- **Status:** Needs code verification / Needs architecture alignment.
- **Relevant areas (informational):**
  `SiNetProjectManagerV2` ViewModels (filing / workflow / ACC / email
  surfaces), `ProjectWork` / `ProjectWorkView` and its backing service(s),
  `SiOffice.AccService`, `SiOffice.AutodeskConnector`,
  `SiOffice.GoogleConnector` / `GoogleService`, `EmailIngestionService`,
  `AccInboxReconciliationService`, `ProjectFileFilingService`,
  `MoveToProjectProcessActionHandler`, `IProcessActionHandler` dispatcher
  and handlers, workflow / task services, `PlanReview` services,
  `AppLogger`.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, or service
    creation change is made in this round.**
  - Service Catalog
    ([`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md))
    is an **initial catalog**; concept-vs-real names and overlaps are
    documented there under *Gaps / overlaps* and must be reconciled in
    follow-up rounds.

### Gap 12 — ACC service boundary overlap and bypass risk

- **Date identified:** 26.05.2026
- **Domain:** ACC, Architecture.
- **Desired principle:**
  `SiOffice.AccService` handles privileged / service-mode ACC operations
  (including remote service-mode usage when `AccService:BaseUrl` is
  configured). `SiOffice.AutodeskConnector` is a **technical API
  connector** only and must not host business decisions, workflow,
  source-of-truth decisions, UI logic, or filing policy.
  `AccInboxReconciliationService` verifies ACC Inbox existence / status
  (`MissingInAcc`, `StaleAccReference`) and merges DB cache + ACC info
  for status only; it must not perform upload, project filing, ACC
  project creation, workflow decisions, or DB-only fallbacks. UI / WPF
  must **not** bypass these boundaries to make business decisions; UI
  goes through Application Services / ViewModels which in turn call the
  appropriate service. See
  [`Domains\ACC\AccSystemPrinciples-2026-05-26.md`](../Domains/ACC/AccSystemPrinciples-2026-05-26.md)
  § *ACC service boundaries* and
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md).
- **Current known / suspected state:**
  Needs code verification. The Service Catalog identifies possible
  overlap between `SiOffice.AccService`, `SiOffice.AutodeskConnector`,
  and `AccInboxReconciliationService`. It has not been verified in this
  round whether:
  - All remote WPF callers honour `AccService:BaseUrl` and route
    privileged ACC operations through `SiOffice.AccService`.
  - No business decisions exist inside `SiOffice.AutodeskConnector`.
  - `AccInboxReconciliationService` is used strictly for verification /
    status (no upload / filing / project creation).
  - No DB-only fallback path is used to open or validate an ACC file.
  - UI / ViewModels never call `SiOffice.AutodeskConnector` directly for
    business decisions.
- **Impact / risk:**
  Duplicate ACC paths; DB-only fallbacks; business logic embedded in a
  technical connector; WPF clients bypassing service mode; inconsistent
  ACC status; mixing of upload / reconciliation / filing under a single
  ambiguous service.
- **Status:** Needs code verification / Needs architecture alignment.
- **Relevant areas (informational):**
  `SiOffice.AccService`, `SiOffice.AutodeskConnector`,
  `AccInboxReconciliationService`,
  `MoveToProjectProcessActionHandler`, `ShowAttachmentInAccAsync`,
  ACC-related ViewModels and Application Services, `AccService:BaseUrl`
  configuration path.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, or service
    creation change is made in this round.**
  - This entry refines the ACC portion of Gap 11; Gap 11 remains the
    broader architecture/service-boundary entry.
  - No parallel ACC service is approved; no bypass of
    `SiOffice.AccService` in service mode is approved.

---

## What we deliberately did NOT do while creating this register

- Did **not** change code.
- Did **not** change DB / schema / migrations / ModelSnapshot.
- Did **not** create a new mechanism, service, handler, or storage path.
- Did **not** re-enable any disabled mechanism.
- Did **not** edit EF migration files, Designer.cs, or ModelSnapshot.

## Pointers

- [`Docs\README.md`](../README.md) — documentation index.
- [`Docs\Decisions\README.md`](README.md) — decisions container.
- [`Docs\Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Domains/Email/EmailSystemPrinciples-2026-05-26.md)
- [`Docs\Domains\DatabaseIdentity\DatabaseIdentityPrinciples-2026-05-26.md`](../Domains/DatabaseIdentity/DatabaseIdentityPrinciples-2026-05-26.md)
- [`Docs\Domains\ACC\AccSystemPrinciples-2026-05-26.md`](../Domains/ACC/AccSystemPrinciples-2026-05-26.md)
- [`Docs\Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../Domains/ProjectFiles/ProjectFilesPrinciples-2026-05-26.md)
- [`Docs\Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](../Domains/Architecture/ArchitecturePrinciples-2026-05-26.md)
- [`Docs\Domains\UI\UiPrinciples-2026-05-26.md`](../Domains/UI/UiPrinciples-2026-05-26.md)
