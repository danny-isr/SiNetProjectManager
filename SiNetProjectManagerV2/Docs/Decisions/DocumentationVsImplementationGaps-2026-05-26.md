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
- **Status:** Fixed — code, DB schema, and tests updated (2026-05-26 round).
- **Resolution (2026-05-26):**
  RFC822 `InternetMessageId` is now persisted as the business identifier on
  `EmailInboxMessage` with `NOT NULL` + `UNIQUE` and no empty default. The
  Gmail local `message.id` is no longer used as a business identity in any
  normal ingestion path; `GetMessageUniqueId(null, …)` no longer appears on
  the regular work paths. Tests were updated to verify the principle, not
  just compilation. No parallel mechanism was introduced.
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
- **Status:** Fixed — policy enforced (2026-05-26 round).
- **Resolution (2026-05-26):**
  `MessageKeyGenerator` is RFC822-first. `gmail:{message.id}` remains only as
  a legacy / compatibility fallback inside `MessageKeyGenerator` and is not
  the normal production path. No new fallback was added; no silent fallback
  to Gmail IDs is approved.
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
- **Status:** Fixed — new layout active (2026-05-26 round).
- **Resolution (2026-05-26):**
  ACC Inbox layout is now `_Inbox/THREAD_<ThreadKey>/MSG_<MessageKey>/` with
  `00_Email.pdf`, `manifest.json`, and an `Attachments/` child folder inside
  each `MSG_` folder. `AccInboxLayout` is the single source of truth for the
  layout. `InboxAccFolderId` is the `MSG_` folder id. The previous
  `Year/Month` layout is superseded; no silent fallback to it remains. No
  automatic migration of legacy ACC Inbox folders was performed and no
  legacy folders were deleted — see *Cleanup / postponed items* below.
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
- **Status:** Fixed — thread identity implemented (2026-05-26 round).
- **Resolution (2026-05-26):**
  `EmailInboxMessage` now carries `ThreadUniqueId` and `ThreadKey`.
  `ThreadUniqueId` is derived from RFC822 threading headers in priority
  `References[0]` → `In-Reply-To` → the message's own `InternetMessageId`.
  `ThreadKey` is a short deterministic hash. `GmailThreadId` is no longer a
  business source of truth for thread identity. `ThreadStatusMapping` was
  restructured: `Id` is a surrogate technical primary key, `ThreadUniqueId`
  is the business unique key, `ThreadId` is a nullable Gmail adapter /
  runtime mirror, and `GmailLabelId` is an adapter / runtime field. The
  migration and `Update-Database` were run by the user. A leftover default
  constraint with empty `""` on `ThreadStatusMapping.ThreadUniqueId` is
  acknowledged as a candidate for future cleanup before production — see
  *Cleanup / postponed items* below.
- **Relevant files / classes (informational):**
  Helper alongside `MessageKeyGenerator` (extended in place — no parallel
  mechanism created).

### Gap 5 — Storage Destination model: documentation vs code alignment

- **Date identified:** 26.05.2026
- **Domain:** Project Files, Database Identity, ACC.
- **Desired principle:**
  Every managed file has one binding Storage Destination value from the
  `FileStorageDestination` enum: `FileServer` (default), `Acc`, or
  `GoogleDrive` (reserved / postponed). Gmail is **not** a Storage
  Destination — it is a read-only ingestion source only.
  See [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../Domains/ProjectFiles/ProjectFilesPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Verified in 2026-05-26 round. The DB / code model uses
  `ProjectFile.StorageDestination` (slot-level) as the source of truth and
  the `FileStorageDestination` enum already excludes Gmail. The previous
  documentation wording that listed Gmail as a fourth enum value has been
  corrected. Google Drive upload remains postponed.
- **Impact / risk:**
  Without a single, explicit Storage Destination per file, the system may
  rely on heuristics ("where we found a copy") instead of authoritative
  configuration, leading to ambiguous "source of truth" for physical files.
- **Status:** Partially confirmed — documentation alignment needed; `ProjectFileInstance.StorageDestination` follow-up tracked under Gap 9 (2026-05-26 round).
- **Resolution (2026-05-26):**
  Code verification completed. Findings:
  - `ProjectFile.StorageDestination` (column on `ProjectFile`, typed
    `FileStorageDestination`) is the **slot-level source of truth** for
    routing. `ProjectFileFilingService`, `ProjectFileUploadService`, and
    `IFileOpenService` all dispatch on it explicitly; no destination is
    inferred from "where we found a copy".
  - The enum `SiNetSQL\Models\FileStorageDestination.cs` contains
    `FileServer = 0` (default), `Acc = 1`, `GoogleDrive = 2`.
    **`Gmail` is not, and must not be, a value of the enum.** Gmail is a
    read-only ingestion source represented separately by
    `EmailInboxMessage` / `EmailInboxAttachment` and the
    `EmailIngestionService` path; the system does not write back to
    Gmail.
  - **`GoogleDrive` is reserved / postponed.** It exists in the enum and
    in the `ProjectFileUploadService` switch, but the
    `UploadToGoogleDriveAsync` method returns an explicit failure
    (`"העלאה ל-Google Drive טרם מיושמת"`). There is no silent fallback
    from ACC or File Server to Google Drive. Activating Drive upload
    requires an explicit approval round.
  - **No silent fallback** between destinations was found. Default
    `switch` branches return explicit failures
    (e.g. `"סוג אחסון לא נתמך"`, `Unsupported StorageDestination`).
  - `ProjectFileInstance.StorageDestination` is still a persisted column
    on the instance row and is read by `ProjectFileRefileService` and
    `EmailMoveToProjectApplicationService` for cleanup / classification.
    Per the ACC source-of-truth principle and the
    runtime-projection contract, this column is **legacy / transitional
    persisted projection** and **not** the architectural source of truth
    for physical existence. Its cleanup is tracked under **Gap 9** and is
    **postponed** in this round; no schema, model, or migration change is
    made here.
  - The previous documentation listed "Gmail" as a fourth Storage
    Destination value. That wording has been corrected in
    `Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`
    (Gmail removed from the enum-value list, marked explicitly as
    *not a Storage Destination*; `GoogleDrive` marked as
    *reserved / postponed*; the `ProjectFile.StorageDestination` vs
    `ProjectFileInstance.StorageDestination` distinction made explicit).
- **Relevant files / classes (informational):**
  `SiNetSQL\Models\FileStorageDestination.cs`,
  `SiNetSQL\Models\ProjectFile.cs`,
  `SiNetSQL\Models\ProjectFileInstance.cs`,
  `SiNetSQL\Services\Files\ProjectFileFilingService.cs`,
  `SiNetSQL\Services\Files\ProjectFileRefileService.cs`,
  `SiNetSQL\Services\Coordinators\ProjectFileUploadService.cs`,
  `SiNetSQL\Services\Coordinators\AccFileSyncService.cs`,
  `SiNetSQL\Services\MoveToProject\EmailMoveToProjectApplicationService.cs`,
  `SiNetSQL\Services\FileOpen\IFileOpenService.cs`,
  `SiOffice.GoogleConnector\Reports\GoogleDriveService.cs` (used by
  Reports only, not by managed-file filing).

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

### Gap 13 — Google service boundary and Storage Destination separation

- **Date identified:** 26.05.2026
- **Domain:** Email, Project Files, Architecture.
- **Desired principle:**
  `SiOffice.GoogleConnector` / `GoogleService` provides Google API access
  (Gmail, Google Drive, Google Sheets) and OAuth / token handling only.
  **Gmail** is **read-only ingestion** plus an authoritative RFC822 header
  source; mailbox-local `message.id` / `threadId` are runtime-only and not
  persisted as business data. **Google Drive** is a **possible Storage
  Destination separate from Gmail**, but Drive **upload is postponed** and
  no new Drive upload or Drive fallback may be added without an explicit
  decision; Drive does **not** replace DB as a business source of truth.
  **Google Sheets** is an integration / reporting / template surface only,
  **not** a general business source of truth. Business decisions
  (identity, workflow, filing, Storage Destination, PlanReview, AI) remain
  in domain services. See
  [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Domains/Email/EmailSystemPrinciples-2026-05-26.md)
  §11,
  [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../Domains/ProjectFiles/ProjectFilesPrinciples-2026-05-26.md)
  § *Google service boundary for project files*, and
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md).
- **Current known / suspected state:**
  Needs code verification. `GoogleService` may contain mixed
  responsibilities across Gmail / Drive / Sheets, and may be called
  directly by UI or non-domain code. It has not been verified in this
  round whether:
  - All Gmail business identity (`MessageUniqueId`, `MessageKey`,
    `ThreadKey`) is derived in domain services rather than inside
    `GoogleService`.
  - No code persists Gmail mailbox-local `message.id` / `threadId` as
    business identifiers (related to Gaps 1, 2).
  - No code path writes back to Gmail.
  - No active Google Drive upload mechanism / Drive fallback exists
    beyond the postponed status.
  - Google Sheets is not used as a business source of truth in any
    domain without an explicit decision.
  - UI / ViewModels do not bypass domain services to call `GoogleService`
    for business decisions.
- **Impact / risk:**
  Business rules embedded in a connector; confusion between Gmail and
  Google Drive as destinations; mailbox-local Gmail IDs leaking into the
  DB as business identifiers; unsanctioned Google Drive upload / Drive
  fallback paths; Google Sheets becoming an accidental business source of
  truth; UI bypass of domain services through the Google connector.
- **Status:** Needs code verification / Needs architecture alignment.
- **Relevant areas (informational):**
  `SiOffice.GoogleConnector\GoogleService.cs`,
  `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`,
  `MessageKeyGenerator`, `EmailManagementView`,
  `ProjectFileFilingService`, Storage Destination configuration paths,
  any Google Drive / Sheets helpers within the connector.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, or service
    creation change is made in this round.**
  - This entry complements Gaps 1, 2, 5, 6, and 11. It does not replace
    them; it focuses the Google connector / service boundary aspect.
  - No new Google Drive upload mechanism, no Google Drive fallback, and
    no general use of Google Sheets as a business source of truth are
    approved in this round.

### Gap 14 — Workflow / Task / Action handler boundary alignment

- **Date identified:** 26.05.2026
- **Domain:** Workflow, Architecture, UI.
- **Desired principle:**
  `Workflow` is the long-running business process (stages, transitions,
  policy by project type, controlled advancement). `Task` is an
  executable unit of work; it can be part of a `Workflow` but does not
  replace it. `Action Handler`s execute business operations through the
  approved dispatcher (e.g. `IProcessActionHandler`); existing handlers
  must be reused or extended instead of creating parallel ad-hoc chains.
  `RuntimeAction` is an action description / state, **not** a parallel
  workflow engine, and must not bypass `Workflow` / `Task` /
  `Action Handler` / `Completion`. `Completion` records the result,
  invokes the handler when required, updates the workflow when required,
  surfaces and logs failure, and must not silently mark success when the
  handler failed. UI / `ViewModel`s must not bypass these boundaries:
  they call Service / Dispatcher / Handler / Use Case and surface the
  result to the user. See
  [`Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](../Domains/Workflow/WorkflowPrinciples-2026-05-26.md)
  § *Workflow / Task / Action handler boundaries*,
  [`Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](../Domains/Architecture/ArchitecturePrinciples-2026-05-26.md)
  § *Workflow vs Task*,
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md),
  and
  [`Domains\UI\UiPrinciples-2026-05-26.md`](../Domains/UI/UiPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Needs code verification. It has not been verified in this round
  whether:
  - Any `ViewModel` executes business actions
    (`MoveToProject`, `ReviewTask`, `FileQuoteMaterial`,
    `AddMaterialToProject`, `TaskCompletion`, `RuntimeAction`-related
    operations) directly instead of going through a Service /
    Dispatcher / Handler / Use Case.
  - Any `ViewModel` changes `WorkflowStage` or `ProjectStatus` directly.
  - Task completion always goes through the agreed completion / handler
    path (records result, invokes handler, updates workflow, surfaces
    and logs failure) rather than being a status-only shortcut.
  - Any `RuntimeAction` path acts as a parallel workflow engine that
    bypasses `Workflow` / `Task` / `Action Handler` / `Completion`.
  - Any completion / handler path contains a fallback that signals
    success even when the underlying business execution failed.
  - New action chains have been added in parallel to an existing
    handler that already covers the case (instead of extending the
    existing handler).
- **Impact / risk:**
  Workflow stage changes outside the workflow engine; task closure
  without business execution; duplicated action paths; hidden failures;
  inconsistent UI status; `RuntimeAction` used as a second workflow
  engine; `ProjectStatus` misused as a `WorkflowStage`.
- **Status:** Needs code verification / Needs architecture alignment.
- **Relevant areas (informational):**
  `SiNetSQL` workflow services / `WorkflowEngine`,
  `IProcessActionHandler` dispatcher and handlers
  (`MoveToProjectProcessActionHandler`, `ReviewTask*`,
  `FileQuoteMaterial*`, `AddMaterialToProject*`, `TaskCompletion*`,
  `RuntimeAction`-related handlers), task services, completion paths,
  `RuntimeAction` consumers, `SiNetProjectManagerV2` ViewModels for
  workflow / tasks / actions, `WorkflowManagementWindow`.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, or service
    creation change is made in this round.**
  - This entry complements Gap 11 (service boundaries / ViewModel
    logic) and focuses the workflow / task / action-handler /
    runtime-action / completion aspect.
  - Parallel action flow outside the agreed dispatcher, direct `Task`
    closure that bypasses the agreed completion / service / handler
    path, direct workflow stage changes from the UI / `ViewModel`,
    `ProjectStatus` used as a `WorkflowStage`, `RuntimeAction` as an
    additional workflow engine, and fallbacks that signal success
    despite a failed handler are all **not approved**.

### Gap 15 — Diagnostics, logging, and user-visible status alignment

- **Date identified:** 26.05.2026
- **Domain:** Diagnostics, UI, Architecture.
- **Desired principle:**
  Logs are for diagnostics, but **user-impacting problems must be
  visible in the UI**. The application already has a **System Status**
  menu / mechanism for service / system-level health; it must be
  **reused or extended**, not duplicated by a parallel mechanism.
  **Item-specific** problems (a specific file upload, a specific email
  not found in Gmail, a specific handler failure, an invalid
  alternative name) must appear as **local UI status** near the
  relevant item, in addition to the structured log. `Recovery` and
  `Reconciliation` check the relevant source of truth (ACC, current
  user's Gmail mailbox, Storage Destination) and never introduce silent
  fallbacks. **Disabled / postponed / candidate-for-removal** mechanisms
  must remain documented with a stated status and reason, and must not
  be silently revived. See
  [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Domains/Diagnostics/DiagnosticsPrinciples-2026-05-26.md),
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md),
  [`Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](../Domains/Architecture/ArchitecturePrinciples-2026-05-26.md)
  § *Diagnostics / user-visible status*, and
  [`Domains\UI\UiPrinciples-2026-05-26.md`](../Domains/UI/UiPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Needs code verification. Some failures may currently be **log-only**
  and not visible to the user; some user-facing errors may be ambiguous
  (e.g. a bare `Metadata error`) without explaining what happened,
  whether retry is possible, or whether manual action is required. The
  existing System Status menu exists and should be reused / extended
  rather than duplicated. It has not been verified in this round
  whether all structured log lines use central logging (e.g.
  `AppLogger`) with explicit tags (`[AccInboxReconciliation]`,
  `[AttachmentUploadStatus]`, `[WorkflowCompletion]`,
  `[ProjectFileInstanceRefresh]`), business identifiers, Storage
  Destination, and status / reason / failure category, or whether any
  silent fallback still marks a failed operation as success.
- **Impact / risk:**
  Users cannot understand the current state of their actions; support
  and debugging rely only on logs; failures are hidden by silent
  fallbacks; a parallel System Status mechanism is created instead of
  extending the existing one; disabled / postponed mechanisms are
  revived by accident; UI shows ambiguous messages that do not lead to
  a clear next step.
- **Status:** Needs code verification / Needs UX alignment.
- **Notes — Testing policy (added 2026-05-26 round):**
  Tests are expected to be as comprehensive as possible and must verify
  that the system actually upholds the principles documented in the
  approved Domains documents. Whenever a material change is made to
  identity, DB schema, workflow, storage layout, service boundaries, or
  fallback policy, the corresponding tests must be updated or added.
  Build-only validation is not sufficient. In the 2026-05-26 round the
  full `SiNetSQL.Tests` suite was brought back to green (601 passed, 0
  failed, 2 skipped, 603 total) by updating tests to reflect the new
  approved principles — no test was deleted and no failure was marked as
  unrelated. Remaining cleanup candidates (see *Cleanup / postponed
  items* below) should be surfaced before production:
  remove the default constraint on `ThreadStatusMapping.ThreadUniqueId`,
  consider an optional legacy ACC Inbox folder migration tool, and add
  user-friendly handling for an external upload missing `ThreadKey`.
- **Relevant areas (informational):**
  `AppLogger`, structured-log call sites
  (`AccInboxReconciliationService`, attachment upload paths,
  `IProcessActionHandler` handlers, `ProjectFileInstance` refresh
  paths), existing System Status menu / window in
  `SiNetProjectManagerV2`, error / status surfaces in `ViewModel`s and
  views (`EmailManagementView`, `ProjectWorkView`,
  `WorkflowManagementWindow`), recovery / reconciliation services.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, service
    creation, or UI mechanism creation is made in this round.**
  - A new System Status mechanism parallel to the existing one is
    **not approved**.
  - `log`-only error reporting when the failure affects the user is
    **not approved**.
  - Vague user messages such as a bare `Metadata error` without a
    clear interpretation are **not approved**.
  - Silent fallback that hides a failure (including marking success
    when a handler failed) is **not approved**.
  - Re-enabling disabled mechanisms without an explicit approval round
    is **not approved**.
  - This entry complements Gap 11 (service boundaries / ViewModel
    logic) and Gap 14 (workflow / task / action handler boundary) and
    focuses the diagnostics / user-visible status aspect.

### Gap 16 — PlanReview / Inspection / Review / AI boundary alignment

- **Date identified:** 26.05.2026
- **Domain:** PlanReview, Workflow, AI, UI, Architecture.
- **Desired principle:**
  `PlanReview` is a **business `Workflow`** for reviewing plans and uses
  the regular `Workflow` / `Task` / `Action Handler` mechanisms; it must
  **not** become a parallel workflow engine and must **not** run as a
  silent side effect of file filing. `Inspection` / `Review` is a
  **reusable work component inside a `Workflow`** with a **dedicated UI
  window**; inside `PlanReview` it may be a **stage**, a **`Task`**
  (when assignment / tracking / completion are required), or an
  **`Action`** (when a concrete operation is required via an agreed
  `Action Handler` / `Service`). The dedicated UI is **not** a source
  of truth and **not** a workflow engine. **`AI` is advisory only**: it
  must not approve, reject, close a `Task`, advance a `Workflow` stage,
  file a file, change business metadata, or write back to DB / ACC /
  Storage Destination without **explicit user confirmation**, an
  **agreed `Action Handler`**, or an **approved `Workflow` / `Task`
  path**. DB is the source of truth for the business process; Storage
  Destination is the source of truth for the physical existence of the
  file under review; AI and UI are **not** sources of truth. See
  [`Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`](../Domains/PlanReview/PlanReviewPrinciples-2026-05-26.md)
  § *PlanReview / Inspection / Review / AI boundaries*,
  [`Domains\AI\AiSystemPrinciples-2026-05-26.md`](../Domains/AI/AiSystemPrinciples-2026-05-26.md)
  § *AI boundary inside business workflows*,
  [`Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](../Domains/Workflow/WorkflowPrinciples-2026-05-26.md)
  § *Workflow / Task / Action handler boundaries*,
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md),
  and
  [`Domains\UI\UiPrinciples-2026-05-26.md`](../Domains/UI/UiPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Needs code verification. The current implementation may mix Review UI,
  Inspection logic, PlanReview flow, Tasks, Actions, and AI-related
  ideas without clear boundaries. It has not been verified in this
  round whether:
  - `PlanReview` uses the regular `Workflow` / `Task` / `Action Handler`
    mechanisms (and not a parallel engine).
  - File filing avoids silently triggering plan review; any trigger
    routes through `Task` / `Workflow` / `Action Handler`.
  - The Inspection / Review UI window invokes Services / Handlers and
    does not change `Review` / `Workflow` / `Task` state directly.
  - Any AI integration is wired as advisory only, with no auto-approve /
    auto-reject / auto-complete / auto-advance / autonomous state
    writes.
- **Impact / risk:**
  Parallel workflow paths; UI-driven business state changes; AI
  becoming an accidental decision maker; `Inspection` / `Review` logic
  bypassing the `Workflow` / `Task` / `Action Handler` mechanisms;
  silent side-effects from file filing; duplicate or shadow review
  data stores.
- **Status:** Needs code verification / Needs architecture alignment.
- **Relevant areas (informational):**
  PlanReview services and workflows, Inspection / Review dedicated UI
  window, `IProcessActionHandler` handlers related to review / task
  completion, AI integration points, `ProjectFileFilingService` and
  `MoveToProjectProcessActionHandler` for the filing-vs-review boundary,
  `WorkflowEngine`, task services, `EmailManagementView` /
  `ProjectWorkView` / `WorkflowManagementWindow` surfaces.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, service
    creation, or UI mechanism creation is made in this round.**
  - `Inspection` / `Review` as a stand-alone workflow engine is **not
    approved**.
  - `PlanReview` as a silent side effect of file filing is **not
    approved**.
  - UI that changes `Review` / `Workflow` / `Task` state directly is
    **not approved**.
  - `AI` as an autonomous decision maker is **not approved**.
  - `AI` auto-approve / auto-reject / auto-complete / auto-advance is
    **not approved**.
  - A parallel mechanism alongside `Workflow` / `Task` /
    `Action Handler` for `PlanReview` / `Inspection` / `Review` is
    **not approved**.
  - Storing `AI` output as business truth without explicit user
    confirmation is **not approved**.
  - Sending secrets / tokens / credentials / sensitive data to AI
    without an explicit decision is **not approved**.
  - This entry complements Gap 11 (service boundaries / ViewModel
    logic) and Gap 14 (workflow / task / action handler boundary) and
    focuses the PlanReview / Inspection / Review / AI aspect.

### Gap 17 — Deployment script and centralized authorization alignment

- **Date identified:** 26.05.2026
- **Domain:** Deployment, Architecture, UI, Diagnostics, ACC, Email.
- **Desired principle:**
  Deployment is documented from the **actual existing deployment
  scripts / process** in this repository, not from a hypothetical one.
  The four publish channels and the server install path are explicit:
  [`publish-all.ps1`](../../../publish-all.ps1),
  [`SiNetProjectManagerV2\publish-desktop.ps1`](../../publish-desktop.ps1),
  [`SiOffice.AccService\publish-service.ps1`](../../../SiOffice.AccService/publish-service.ps1),
  [`MasterPlan.SyncEngine\publish-console.ps1`](../../../MasterPlan.SyncEngine/publish-console.ps1),
  [`SiNet.SecretImport\publish-tool.ps1`](../../../SiNet.SecretImport/publish-tool.ps1),
  and the unified server installer
  [`SiOffice.AccService\Install-OnServer.ps1`](../../../SiOffice.AccService/Install-OnServer.ps1).
  Dependencies (ACC Service, DB, File Server, Google / Gmail / Drive /
  Sheets, WebView2 Runtime, configuration), authorization stores
  (per-user Windows Credential Manager / DPAPI vault on the server seeded
  by `SiNet.SecretImport`; secure platform storage on the client),
  WebView2 `UserDataFolder` / profile policy, and System Status health
  coverage are explicit. **Autodesk and Google authorization are
  centralized and reused** across windows; repeated per-window
  authorization prompts and per-window token / `UserDataFolder` stores
  are not allowed. If authorization is missing or expired, a **single
  central flow** is invoked, **System Status** is updated, and a clear
  message is shown to the user. At startup, only **lightweight
  availability checks** and **`System Status`** updates are allowed; no
  global scan, no migrations, no ACC project creation, no wide
  provisioning, no automatic uploads, no workflow changes, and no silent
  fallback from `AccService` to a local privileged path. See
  [`Domains\Deployment\DeploymentPrinciples-2026-05-26.md`](../Domains/Deployment/DeploymentPrinciples-2026-05-26.md),
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md),
  [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Domains/Diagnostics/DiagnosticsPrinciples-2026-05-26.md),
  and
  [`Domains\UI\UiPrinciples-2026-05-26.md`](../Domains/UI/UiPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Needs code verification. The deployment scripts exist and are
  documented in
  [`DEPLOYMENT.md`](../../../DEPLOYMENT.md) and the per-project
  `DEPLOYMENT.md` files, but the application side has not been audited
  in this round to confirm:
  - Autodesk authorization is triggered from a **single central
    flow** rather than from multiple windows / `ViewModel`s.
  - Google authorization (Gmail / Drive / Sheets scopes) is triggered
    from a **single central flow** rather than from multiple windows /
    `ViewModel`s.
  - All WebView2 hosts that need to share a login use the **same
    `UserDataFolder` / profile policy**; any deliberate per-window
    isolation is explicitly documented.
  - `System Status` reflects auth / service health for ACC, Autodesk
    auth, Google auth, DB, File Server, Gmail, WebView2 Runtime, and
    AI (where applicable), and no parallel System Status mechanism has
    been introduced.
  - Startup performs only lightweight availability checks + System
    Status updates (no global scan, no migrations, no wide
    provisioning, no automatic uploads, no workflow changes, no silent
    `AccService`→local fallback).
  - The WPF client, in service mode (`AccService:BaseUrl` configured),
    routes privileged ACC operations through `SiOffice.AccService` and
    does **not** duplicate the service-side two-legged auth locally.
- **Impact / risk:**
  Office rollout breaks because dependencies and prerequisites are
  unclear; repeated OAuth prompts from multiple windows; multiple token
  stores; WebView2 sessions not shared correctly across windows that
  should share a login (or accidentally shared across windows that
  should be isolated); WPF bypassing service mode; parallel `System
  Status` surfaces; startup performing forbidden wide work (global
  scan / migrations / provisioning / uploads); silent fallback hiding
  a missing dependency; support and debugging become hard because the
  real deployment process and auth model are not documented in one
  place.
- **Status:** Needs deployment review / Needs auth architecture
  verification.
- **Relevant areas (informational):**
  Deployment scripts and docs listed in the desired principle above;
  `SiOffice.AccService`, `SiOffice.AutodeskConnector`,
  `SiOffice.GoogleConnector` / `GoogleService`, `TokenProvider`,
  `Bim360Service`, `AccUserBootstrapService`, WPF startup
  (`App.xaml.cs`), Autodesk / Google login surfaces and any WebView2
  hosts, existing System Status menu / window, `appsettings.*.json` /
  `AccService:BaseUrl`, per-user Windows Credential Manager / DPAPI
  vault on the server, `SiNet.SecretImport` + `Install-OnServer.ps1`.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, service
    creation, script change, authorization-mechanism change, new
    WebView2 profile, or re-enabling of disabled mechanisms is made
    in this round.**
  - Per-window Autodesk / Google authorization is **not approved**.
  - Per-window token / `UserDataFolder` without a documented reason
    is **not approved**.
  - A parallel `System Status` mechanism is **not approved**.
  - Startup-time global scan / migrations / ACC project creation /
    wide provisioning / automatic uploads / workflow changes are
    **not approved**.
  - Silent fallback from `AccService` to a local privileged path is
    **not approved**.
  - Creating a new deployment script in this round is **not in this
    round**.
  - Fixing authorization code in this round is **not in this round**.
  - This entry complements Gap 11 (service boundaries / ViewModel
    logic), Gap 12 (ACC service boundary), Gap 13 (Google service
    boundary), and Gap 15 (diagnostics / System Status) and focuses
    the deployment / centralized authorization aspect.

### Gap 18 — Secrets / credentials / token storage alignment

- **Date identified:** 26.05.2026
- **Domain:** Deployment, Architecture, Diagnostics, ACC, Email.
- **Desired principle:**
  **Service secrets**, **user OAuth tokens**, and **WebView2
  session / profile** are **three separate concerns** with **central
  ownership**. Service secrets are imported / stored through the
  official secure vault path
  (`CredentialVaultService` → Windows Credential Manager DPAPI, keyed
  by `SecretKeys`, provisioned via `SecretSetupWindow` on the client and
  `SiNet.SecretImport` + `Install-OnServer.ps1` on the server using the
  encrypted `SiNet.secrets` artefact) and are **not** stored in `git`,
  in `appsettings*.json`, or in `*.ps1` scripts. Autodesk and Google
  **user authorization are centralized and reused across windows**;
  Autodesk refresh tokens live in
  `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` (via
  `TokenProvider`), Google user tokens live in the `FileDataStore`
  under `GoogleReports:TokenStorePath` (default
  `%APPDATA%\SiNet\GoogleTokens`, via
  `GoogleAuthService` / `GoogleWebAuthorizationBroker`), and these
  stores must **not** be mixed with service secrets. WebView2
  `UserDataFolder` / profile policy is explicit
  (`AppConfiguration.WebView2UserDataBasePath`, per-Google-account
  subfolder via `WebView2Helper.CreateUserEnvironmentAsync()`, isolated
  `acc_viewer` subfolder via `WebView2Helper.CreateAccEnvironmentAsync()`);
  no random per-window `UserDataFolder` is created. `System Status`
  reports auth / service health (configured / missing / expired /
  unreachable) **without exposing secret values**. Logs never include
  secrets, tokens, passwords, authorization codes, or cookies. See
  [`Domains\Deployment\DeploymentPrinciples-2026-05-26.md`](../Domains/Deployment/DeploymentPrinciples-2026-05-26.md)
  § *Secrets / credentials / token storage*,
  [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md)
  row *Secrets / credentials / token storage stack*, and
  [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Domains/Diagnostics/DiagnosticsPrinciples-2026-05-26.md).
- **Current known / suspected state:**
  Based on a **read-only scan** of the repository:
  - **Service secrets — clear and centralized.** `CredentialVaultService`
    (`SiNetSQL\Services\CredentialVaultService.cs`) is the single
    read/write API to Windows Credential Manager via DPAPI;
    `SecretKeys` is the single central key list;
    `SecretProvisioningService` produces / consumes the encrypted
    `SiNet.secrets` artefact (AES-256-CBC + PBKDF2, custom `SNET`
    header); `SecretSetupWindow` provisions the vault on end-user
    workstations; `SiNet.SecretImport.exe` +
    `SiOffice.AccService\Install-OnServer.ps1` provision the per-user
    DPAPI vault of the service account on the server.
  - **No service secrets observed in `git`.**
    `SiNetProjectManagerV2\appsettings.json` and
    `SiOffice.AccService\appsettings.json` carry only non-secret
    configuration plus explicit notes that secrets live in the vault
    (`AccService:ApiKey` and `AccService:Certificate:Password` are
    empty in source; `ConnectionStrings:SiNetDatabase` is empty in
    source). Not exhaustively audited — Needs code verification across
    all configuration / script files.
  - **Autodesk user authorization** is implemented in
    `SiOffice.AutodeskConnector\TokenProvider.cs`. `client_id` /
    `client_secret` come from the vault; the **refresh token** is
    stored at `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` per
    Windows user. **Whether all windows / `ViewModel`s share a single
    `TokenProvider` instance / central flow** rather than instantiating
    their own — **Needs code verification**.
  - **Google user authorization** is implemented in
    `SiOffice.GoogleConnector\Reports\GoogleAuthService.cs` via
    `GoogleWebAuthorizationBroker` + `FileDataStore`, with the
    token-store path configured by `GoogleReports:TokenStorePath`
    (default `%APPDATA%\SiNet\GoogleTokens`). Drive and Sheets share
    the same `GoogleAuthService` credential (same scopes, same store);
    Gmail access used by the email surfaces has not been confirmed in
    this round to share the same credential store rather than running
    its own flow — **Needs code verification**.
  - **WebView2 policy** is implemented in
    `SiNetProjectManagerV2\WPFUserControl\WebView2Helper.cs` and
    `SiNetProjectManagerV2\Services\AppConfiguration.cs`:
    `WebView2UserDataBasePath` (configurable via
    `WebView2:UserDataBasePath`), per-Google-account subfolder under
    that base, and a deliberate `acc_viewer` subfolder for the ACC
    document viewer. Whether **all** WebView2 hosts in the application
    route through these helpers (no ad-hoc `CoreWebView2Environment`
    creation with a random / default `UserDataFolder`) — **Needs code
    verification**.
  - **System Status** today is known to surface service-level health;
    it has **not been verified in this round** that it also surfaces
    Autodesk authorization status, Google authorization status, vault
    status, WebView2 Runtime status, and File Server availability in a
    single place, and that it does so **without** ever displaying
    secret values.
  - **Log hygiene** has not been audited in this round; it must be
    verified that no code path logs secret values, OAuth tokens,
    refresh tokens, authorization codes, passwords, or cookies.
- **Impact / risk:**
  Repeated authorization prompts from multiple windows; multiple token
  stores; leaked secrets in config / scripts / logs; confusion between
  service secrets and user OAuth tokens; broken office rollout when the
  per-user DPAPI vault is not provisioned on the right account; WebView2
  session inconsistency between windows that should share login or
  between windows that should be isolated; `System Status` accidentally
  exposing secret values.
- **Status:** Needs security/auth review / Needs code verification.
- **Relevant areas (informational):**
  `SiNetSQL\Services\CredentialVaultService.cs`,
  `SiNetSQL\Services\SecretKeys.cs`,
  `SiNetProjectManagerV2\Services\SecretProvisioningService.cs`,
  `SiNetProjectManagerV2\WPF Window\SecretSetupWindow.xaml.cs`,
  `SiNetProjectManagerV2\Services\AppConfiguration.cs`
  (`GetGoogleClientSecretsPath`, `GoogleTokenStorePath`,
  `WebView2UserDataBasePath`),
  `SiNetProjectManagerV2\WPFUserControl\WebView2Helper.cs`,
  `SiOffice.AutodeskConnector\TokenProvider.cs`,
  `SiOffice.AutodeskConnector\ITokenProvider.cs`,
  `SiOffice.GoogleConnector\Reports\GoogleAuthService.cs`,
  `SiNet.SecretImport\Program.cs`,
  `SiOffice.AccService\Install-OnServer.ps1`,
  `SiOffice.AccService\publish-service.ps1`,
  `SiNetProjectManagerV2\appsettings.json`,
  `SiOffice.AccService\appsettings.json`,
  existing `System Status` menu / window, `AppLogger` and all
  structured-log call sites.
- **Notes:**
  - **No code, DB, schema, migration, ModelSnapshot, DI, service
    creation, script change, authorization-mechanism change, secret
    movement, vault creation, or new WebView2 profile is made in this
    round.**
  - Per-window Autodesk / Google authorization flows are **not
    approved**.
  - Duplicate token stores without an explicit decision are **not
    approved**.
  - Storing secrets / tokens / passwords / authorization codes / cookies
    in `git`, in `appsettings*.json`, in `*.ps1` scripts, or in any
    plain-text location is **not approved**.
  - Logging secrets / tokens / passwords / authorization codes /
    cookies is **not approved**.
  - Mixing service secrets with user OAuth tokens in a single store is
    **not approved**.
  - Showing secret values in `System Status` is **not approved**.
  - Creating a per-window WebView2 `UserDataFolder` without a documented
    isolation reason is **not approved**.
  - Re-enabling disabled secret-handling mechanisms without an
    approval round is **not approved**.
  - This entry complements Gap 11 (service boundaries), Gap 12 (ACC
    service boundary), Gap 13 (Google service boundary), Gap 15
    (diagnostics / System Status), and Gap 17 (deployment / centralized
    authorization), and focuses the secrets / credentials / token
    storage aspect.

---

## Cleanup / postponed items (2026-05-26 round)

The following items were intentionally **not** addressed in the
identity / thread / ACC layout / tests rounds and are recorded here as
candidates for future approved rounds. They are append-only; do not
remove entries — change status in place if they are resolved.

- **`Year/Month` ACC Inbox layout** — *superseded* by
  `_Inbox/THREAD_<ThreadKey>/MSG_<MessageKey>/`. No automatic legacy
  folder migration is approved in this round; no silent fallback to the
  old layout is approved.
- **Legacy ACC Inbox folders migration tool** — *postponed / candidate*.
  No existing ACC folders were moved or deleted. Any future migration
  must be a dedicated, opt-in, audited tool — not an automatic startup
  side effect.
- **Default constraint on `ThreadStatusMapping.ThreadUniqueId`** —
  *candidate for cleanup before production*. The column carries a
  leftover default value of `""`. The business unique key should not
  have an empty-string default; this must be removed in a dedicated
  schema round before production.
- **Prefix partition under `_Inbox`** (for example
  `_Inbox/T4/THREAD_<ThreadKey>/...`) — *not approved / postponed*. The
  approved layout is flat `_Inbox/THREAD_<ThreadKey>/MSG_<MessageKey>/`.
  Any future partitioning requires its own approval round.
- **`ProjectFileInstanceId` as a required source of truth** —
  *superseded / not approved*. ACC item / version / folder is the source
  of truth for the physical existence of a file in MoveToProject and
  similar flows. `ProjectFileInstanceId` is a runtime projection /
  legacy fallback only. No new mandatory dependency on it may be added.
- **Silent fallback to Gmail IDs as business identity** — *not
  approved*. RFC822 `InternetMessageId` is the business identifier;
  `gmail:{message.id}` remains only as a legacy / compatibility
  fallback inside `MessageKeyGenerator`.
- **Friendly handling for external upload missing `ThreadKey`** —
  *candidate*. The current behavior throws; the UX should be improved to
  guide the user instead of surfacing a bare exception.
- **Google Drive upload activation** — *postponed*. `GoogleDrive` exists
  in `FileStorageDestination` and in the `ProjectFileUploadService`
  dispatch switch, but `UploadToGoogleDriveAsync` returns an explicit
  failure. No silent fallback to Drive from ACC or File Server is
  approved. Activation requires its own approval round (Gap 5).
- **Gmail as a Storage Destination** — *not approved / corrected*.
  Gmail is a read-only ingestion source represented by
  `EmailInboxMessage` / `EmailInboxAttachment`. It is not, and must not
  be added as, a value in `FileStorageDestination` (Gap 5).
- **`ProjectFileInstance.StorageDestination` cleanup** — *postponed to
  Gap 9*. The column remains persisted and is still read by
  `ProjectFileRefileService` and `EmailMoveToProjectApplicationService`
  for cleanup / classification, but it is legacy / transitional
  projection state and not the architectural source of truth for
  physical existence. No schema, model, or migration change is approved
  in this round (Gap 5 / Gap 9).

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
- [`Docs\Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](../Domains/Workflow/WorkflowPrinciples-2026-05-26.md)
- [`Docs\Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md)
- [`Docs\Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Domains/Diagnostics/DiagnosticsPrinciples-2026-05-26.md)
- [`Docs\Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`](../Domains/PlanReview/PlanReviewPrinciples-2026-05-26.md)
- [`Docs\Domains\AI\AiSystemPrinciples-2026-05-26.md`](../Domains/AI/AiSystemPrinciples-2026-05-26.md)
