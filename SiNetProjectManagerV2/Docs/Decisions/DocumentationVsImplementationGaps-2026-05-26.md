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
- **Status:** Confirmed in code — documentation alignment only (2026-05-26 round).
- **Resolution (2026-05-26):**
  Code verification completed. Findings:
  - `SiNetSQL\FileIndex\AccItemMetadataService.cs`
    (`IAccItemMetadataService`) is the **single central facade** for ACC
    Custom Attribute read / write. All callers
    (`ProjectFileAccMetadataWriter`, `ProjectFileFilingService`,
    `AttachmentTaggingService`, `AccInboxReconciliationService`,
    `MoveToProjectProcessActionHandler`) go through it. Read / write
    failures are reported via `IAccMetadataStatusReporter` and **must
    not** be interpreted as proof the ACC file is missing.
  - `SiNetSQL\FileIndex\SidecarMetadata.cs` centralizes all attribute
    names: `AccAttributeNames` (project files: `SiLastFileName`,
    `SiSourceFileNames`, …) and `InboxAccAttributeNames` (Office Inbox:
    `SiInbox.Tag.*`, `SiInbox.Move.*`, `SiInbox.Lock.*`,
    `SiInbox.Source.*`). No hard-coded attribute names are scattered.
  - `SiNetSQL\Services\AccBootstrap\AccBootstrapService.cs`
    (`EnsureInboxCustomAttributeDefinitionsAsync`) and
    `AccProjectProvisioningService.EnsureCustomAttributeDefinitionsAsync`
    are the **only** provisioning paths for Custom Attribute
    *definitions*. `SetItemCustomAttributesAsync` does not auto-create
    definitions.
  - `manifest.json` is produced once per ingested message by
    `EmailIngestionService.CreateManifest` and is treated as an **audit
    snapshot only**. No code reads `manifest.json` to make business
    decisions; no flow depends on it for routing, tagging, or
    reconciliation.
  - DB fields `AccItemId`, `AccVersionId`, `AccFolderId` on
    `EmailInboxAttachment` and `ProjectFileInstance` are
    **cache / helper / projection** of ACC state — they are written
    *after* the ACC write succeeds (per the ACC source-of-truth
    principle in `copilot-instructions.md` §9) and are not authoritative
    for physical existence.
  - UI / ViewModels (`ProjectWorkViewModel`,
    `ProjectFolderTreeViewModel`, `EmailManagementViewModel`, …) read
    metadata through service layers and never write metadata directly to
    ACC. UI is **never** authoritative.
  - `ProjectFileInstance.StorageDestination` remains a persisted
    legacy / transitional projection and is read by
    `ProjectFileRefileService` and `EmailMoveToProjectApplicationService`
    for cleanup / classification only. Its cleanup is tracked under
    **Gap 9** and is **postponed** in this round; no schema, model, or
    migration change is made here.

  **Metadata Owner Map (added 2026-05-26):**

  | Metadata kind | Owner (source of truth) | Stored / cached in | Notes |
  | --- | --- | --- | --- |
  | Email / thread identity | RFC822 headers (`Message-ID`, `References`, `In-Reply-To`) | DB: `EmailInboxMessage.InternetMessageId`, `MessageUniqueId`, `ThreadUniqueId`, `ThreadKey` | Gmail local IDs (`GmailMessageId`, `GmailThreadId`) are adapter / runtime mirrors only (Gap 1, Gap 2, Gap 4). |
  | Physical file existence | ACC item / version / folder | DB: `AccItemId`, `AccVersionId`, `AccFolderId` on `EmailInboxAttachment` / `ProjectFileInstance` (cache / helper only) | `ProjectFileInstanceId` is **not** source of truth; do not add new mandatory dependencies on it (Gap 9). |
  | Inbox tag / move / lock state | ACC Custom Attributes `SiInbox.Tag.*` / `SiInbox.Move.*` / `SiInbox.Lock.*` | DB: cache / helper only | Read / write only via `AccItemMetadataService`; definitions provisioned via `AccBootstrapService` / `AccProjectProvisioningService`. |
  | Project file routing (slot-level) | DB: `ProjectFile.StorageDestination` (`FileStorageDestination` enum) | DB (authoritative for slot routing) | `ProjectFileInstance.StorageDestination` is legacy / projection and tracked under Gap 9 (Gap 5). |
  | Per-file metadata (notes, last name, source names, openWith) | File Server: `{fileName}.si.json` sidecar &nbsp;/&nbsp; ACC: Custom Attributes `Si*` (`SidecarMetadata.AccAttributeNames`) | Per-backend; no cross-backend authoritative copy | UI does **not** own this metadata. |
  | Email message audit trail | None — audit snapshot only | `manifest.json` next to `00_Email.pdf` in the message folder | Never read by business logic; produced once by `EmailIngestionService.CreateManifest`. |
  | UI / ViewModels | None | n/a | Never authoritative. Views render values produced by services. |

  **Write ordering rule (re-stated):** ACC Custom Attribute writes happen
  **before** updating the corresponding DB cache. If the ACC write fails,
  the DB cache is not advanced. On ACC metadata *read* failure, callers
  warn and continue (do not treat as proof of missing file) — see
  `copilot-instructions.md` §9.

- **Relevant files / classes (informational):**
  `SiNetSQL\FileIndex\AccItemMetadataService.cs` (read/write facade),
  `SiNetSQL\FileIndex\SidecarMetadata.cs` (attribute name constants and
  sidecar schema),
  `SiNetSQL\Services\AccBootstrap\AccBootstrapService.cs`,
  `SiNetSQL\Services\AccBootstrap\AccProjectProvisioningService.cs`
  (definition provisioning),
  `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`
  (`CreateManifest` — audit snapshot only),
  `SiNetSQL\Services\EmailIngestion\AccInboxReconciliationService.cs`
  (ACC-first reconciliation),
  `SiNetSQL\Services\Files\ProjectFileAccMetadataWriter.cs`
  (write-then-cache ordering),
  `SiNetSQL\Services\Files\ProjectFileFilingService.cs`
  (`SiInbox.Source.*` same-source detection from ACC),
  `SiNetSQL\Services\Inbox\AttachmentTaggingService.cs`,
  `SiNetSQL\Domain\Actions\Handlers\MoveToProjectProcessActionHandler.cs`.

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
- **Status:** Confirmed — principle only; helper not implemented; no business-decision DOM usage found (2026-05-26 round).
- **Resolution (2026-05-26):**
  Code verification completed. Findings:
  - **No active Gmail DOM focusing / expansion helper exists.** Searches
    for `FocusMessage`, `ExpandMessage`, `scrollIntoView`,
    `aria-expanded`, `data-message-id`, `data-legacy-message-id` in
    non-generated `.cs` returned no Gmail-related matches.
    `EmailManagementView.xaml.cs` contains no DOM focusing / expansion
    logic.
  - **No DOM is used as a business source of truth.** All active
    `ExecuteScriptAsync` callers are display / utility:
    - `SiNetProjectManagerV2\WPFUserControl\WebView2Helper.cs` injects
      `GmailCleanViewJs` and `CalendarCleanViewJs` (cosmetic clean view
      only; no values returned to .NET for decisions).
    - `SiNetProjectManagerV2\WPFUserControl\WebView2PdfRenderer.cs`
      uses `ExecuteScriptAsync` for PDF load-state / rendering
      utilities only.
    - `EnableScrollWheelFocus` in `WebView2Helper.cs` is WPF control
      focus (for mouse-wheel routing), **not** DOM focus.
  - `GmailVisibleAttachmentsDomExtractor` is a **disabled no-op**: its
    public `ProbeAsync` always returns `Skipped("DisabledForRound")` and
    logs `[GmailVisibleAttachmentsDom] Disabled`. The legacy probe code
    is parked behind `ProbeAsync_DisabledLegacy` and never runs. This
    file is covered by **Gap 8** and is intentionally kept as-is.
  - **Future helper constraint:** if a Gmail message focusing /
    expansion helper is ever implemented, it must be **display-only**
    (scroll to / expand the selected message inside the Gmail
    conversation, e.g. via `data-legacy-message-id`) and must **not**
    return business data (attachments, identity, status, ACC state,
    workflow state) to .NET. This rule is already stated in
    [`Domains\UI\UiPrinciples-2026-05-26.md`](../Domains/UI/UiPrinciples-2026-05-26.md)
    §5 and §6.
- **Relevant files / classes (informational):**
  `SiNetProjectManagerV2\WPFUserControl\WebView2Helper.cs`,
  `SiNetProjectManagerV2\WPFUserControl\WebView2PdfRenderer.cs`,
  `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml.cs`,
  `...\GmailVisibleAttachmentsDomExtractor.cs` (disabled — see Gap 8).

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
- **Status:** Confirmed — commented out / staged for future physical deletion (2026-05-26 round).
- **Resolution (2026-05-26):**
  Code verification + staged-removal step completed without any production
  behavior change:
  - **Static facts before this round:** the public `ProbeAsync` was already
    a no-op (`Skipped("DisabledForRound")`), the legacy probe was already
    `private` (`ProbeAsync_DisabledLegacy`), and no caller invoked the
    extractor on `_gmailDomProbe`. Build was green and tests were
    `601 passed / 0 failed / 2 skipped`.
  - **Staged removal applied (no physical deletion):**
    - `SiNetProjectManagerV2\Services\GmailVisibleAttachmentsDomExtractor.cs`
      — the entire source body (extractor class, the JS chip script,
      `GmailVisibleAttachmentsDomResult`, `GmailVisibleDomAttachment`) is
      now parked behind `#if false` with a clearly tagged
      `DISABLED LEGACY — candidate for physical deletion` header that
      explains the reason, the responsible gap, and the required
      pre-conditions for physical deletion.
    - `SiNetProjectManagerV2\App.xaml.cs` — the DI registration
      `services.AddSingleton<GmailVisibleAttachmentsDomExtractor>();` is
      commented out with a reference to Gap 8.
    - `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml.cs`
      — the `_gmailDomProbe` field, its `OnLoaded` assignment, and the
      no-op `ProbeGmailVisibleAttachmentsAsync` method are commented
      out with a reference to Gap 8.
  - **Validation:** Build succeeded. `SiNetSQL.Tests` ran with the same
    result as before the change: **601 passed / 0 failed / 2 skipped**.
    This confirms no compilation or test dependency on the legacy DOM
    extraction path.
  - **Still to do (future approved cleanup round):** physical deletion of
    the file `GmailVisibleAttachmentsDomExtractor.cs`, the commented DI
    line in `App.xaml.cs`, and the commented `_gmailDomProbe` /
    `ProbeGmailVisibleAttachmentsAsync` lines in
    `EmailManagementView.xaml.cs`. No schema, model, migration, or
    business-logic change is involved in that cleanup.
- **Relevant files / classes (informational):**
  `SiNetProjectManagerV2\Services\GmailVisibleAttachmentsDomExtractor.cs`
  (parked behind `#if false`),
  `SiNetProjectManagerV2\App.xaml.cs` (DI registration commented out),
  `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml.cs`
  (field / assignment / no-op method commented out).

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
- **Status:** Implemented (Stage 9E.4 — `ProjectFileInstance` removed from active model; schema migration `RemoveProjectFileInstanceTable` prepared / user-run database update). See *Round update (Stage 9E.5 — `ProjectFileInstance` removal closure)* below.
- **Resolution (Stage 9E.1 → 9E.4):**
  - `ProjectFileInstance` source-of-truth usage removed (read-side, write-side, and routing).
  - Runtime resolver introduced: `IProjectFileLocationResolver` / `ProjectFileLocationResolver` — in-memory **session cache only**, not a persisted replacement table.
  - Contracts cleaned: `FileProjectFileResult`, `RefileAttachmentResult`, `MoveToProjectAttachmentOutcome`, and `MoveToProjectAttachmentHandlerOutcome` no longer carry `ProjectFileInstanceId` / `OldProjectFileInstanceId` / `NewProjectFileInstanceId`.
  - Model/schema cleanup prepared: `ProjectFileInstance.cs`, `ProjectFileInstanceConfiguration.cs`, `ProjectFileUploadService.cs` physically deleted; `DbSet<ProjectFileInstance>`, `EmailInboxAttachment.ProjectFileInstanceId`, and `InspectionReportDrawing.FileInstanceId` removed from the model and its configurations.
  - Migration `RemoveProjectFileInstanceTable` created (drops `ProjectFileInstance` table, both FKs/indexes, and both columns). `Update-Database` is **user-run**.
  - Tests rewritten away from the removed entity; suite green (628 passed / 0 failed / 2 pre-existing skips, unrelated EF Core InMemory limitation).
  - **Remaining future items (postponed, not in this round):** Google Drive delete wiring in refile cleanup; optional rename-warning replacement via `FileIndex` / sidecar; any audit mechanism replacing the old supersession fields.
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
- **Status:** In progress — pilot done (`CompanyViewModel`); ~28 ViewModels remain (each a separate approved round).
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
  - Service Catalog
    ([`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Domains/Architecture/ServiceCatalog-2026-05-26.md))
    is an **initial catalog**; concept-vs-real names and overlaps are
    documented there under *Gaps / overlaps* and must be reconciled in
    follow-up rounds.

#### Round update — ViewModel → Service extraction pilot (`CompanyViewModel`)

- **Scope of this round:** a single, contained **pilot** to establish the
  canonical ViewModel → Service pattern. An audit found **29 ViewModels**
  with direct DB / connector access (top offenders:
  `EmailManagementViewModel` ~76, `FloatingInspectionViewModel` ~67). The
  full set is **not** refactored at once (Safety & Stability:
  *do not break working code*); each remaining ViewModel is a separate
  approved round.
- **What was done:**
  - New application service `ICompanyService` / `CompanyService`
    (`SiNetSQL\Services\Companies\`) now owns **all** `Company` / `Contact`
    persistence: `GetCompaniesWithContactsAsync`, `SaveCompaniesAsync`,
    `AddCompanyAsync`, `RemoveCompanyAsync` (async + `CancellationToken`,
    short-lived `DbContext` per operation via `IDbContextFactory`).
  - `CompanyViewModel` no longer touches the DB. It receives
    `ICompanyService` by DI and keeps **UI state only** (observable
    collections, filtering `ApplyFilter*`, selection). `SaveChanges()` is
    retained as a thin sync wrapper to preserve the existing
    `WindowCompany.OK_Click` call site; `Add`/`Remove` became `async`
    (no external callers).
  - DI registration added in `App.xaml.cs`
    (`AddTransient<ICompanyService, CompanyService>()`).
  - Service Catalog updated with the new service row.
- **Canonical pattern established (to be reused in follow-up rounds):**
  *ViewModel holds UI display state and calls a service; the service owns
  DB / connector access and business logic. Filtering, selection, and
  observable collections stay in the ViewModel.*
- **Validation:** Build succeeded. `SiNetSQL.Tests` =
  **628 passed / 0 failed / 2 pre-existing skips**. No schema, migration,
  ModelSnapshot, or `DbSet` change.
- **Remaining (future approved rounds):** the other ~28 ViewModels,
  prioritised by size / risk (e.g. `EmailManagementViewModel`,
  `FloatingInspectionViewModel`, `CreateProjectViewModel`,
  `TaskPanelViewModel`, `AddUserViewModel`, …).

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
- **Status:** Confirmed — Boundary intact; follow-ups documented (Round A.1, post-Stage 9E.5). See *Round update (Round A.1 — Gap 12 / Gap 13 boundary audit closure)* below.
- **Resolution (Round A audit, read-only):**
  - ACC metadata writes go through the approved surfaces only: `IAccItemMetadataService` (sole owner of `Bim360Service.SetItemCustomAttributesAsync`) and `IProjectFileAccMetadataWriter` (post-upload SiInbox/ProjectFile attributes).
  - Canonical single-file filing path is `IProjectFileFilingService.FileAsync` (`ProjectFileFilingService.FileToAccAsync`); ACC calls route through `IAccFileClient` (`EnsureFolderPathAsync`, `GetFolderItemsAsync`, `UploadFileFinalAsync`, `UploadNewVersionAsync`).
  - `AccFileStore` is the canonical ACC storage adapter (`IFileStore`); `MoveToProjectProcessActionHandler` consumes `IAccFileClient` + `IAccItemMetadataService` only.
  - `AccInboxReconciliationService` performs read-only existence/status checks via `IAccItemMetadataService`.
  - No ViewModel writes ACC metadata directly. `EmailManagementViewModel` has zero direct `Bim360Service` / `SetItemCustomAttributesAsync` calls.
  - No silent fallback between ACC ↔ File Server ↔ Google Drive: `ProjectFileFilingService` routes strictly on `ProjectFile.StorageDestination` and throws `NotSupportedException` for unrouted destinations.
  - `AccItemId` / `AccVersionId` flow only as hints/targets in result records; physical existence is established via `IAccFileClient.GetFolderItemsAsync` + `IAccItemMetadataService.ReadAttributesAsync`, never from the IDs alone.
  - Startup correctly skips local `AccUserBootstrapService.ProvisionUsersAsync` when `AccService:BaseUrl` is configured (`App.xaml.cs` `StartAccUserBootstrap`).
  - **Follow-ups documented only (not approved for action in this round):**
    1. `EmailIngestionService` (~7 sites: message folder / attachments / `00_Email.pdf` / `manifest.json` / ZIP children) uses `_bim360Service.UploadFileFinalAsync` directly for ACC Inbox uploads. Not a dangerous bypass — it stays inside the connector boundary and owns the Inbox-side upload — but it does not use the newer `IAccFileClient` abstraction. **Candidate for future unification** once a matching Inbox API on `IAccFileClient` is defined.
    2. `AccFileSyncService` is already marked **deprecated for single-file filing** in its XML doc and kept only for the batch `SyncMessageAttachmentsAsync` entry consumed by `EmailManagementViewModel.TryAutoSyncToProjectAsync`. **Candidate for staged removal** after that one consumer is migrated to `IProjectFileFilingService.FileAsync`.
    3. `ProjectWorkViewModel` and `ProjectFolderNode` resolve `AccFileStore` from `ServiceLocator.GetService<IEnumerable<IFileStore>>().OfType<AccFileStore>()` via a concrete-type cast. Not a bypass — the store is the right layer — but a low-severity boundary smell to clean up opportunistically.
- **Relevant areas (informational):**  configuration path.
  Audit-relevant call sites: `ProjectFileFilingService.FileToAccAsync`,
  `AccFileStore.UploadAsync` / `Resolve*`, `AccItemMetadataService`,
  `ProjectFileAccMetadataWriter`, `MoveToProjectProcessActionHandler`,
  `EmailIngestionService` (Inbox-side direct `Bim360Service` use —
  follow-up only), `AccFileSyncService` (deprecated batch path —
  follow-up only).
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
- **Status:** Confirmed — Boundary intact; follow-up documented (Round A.1, post-Stage 9E.5). See *Round update (Round A.1 — Gap 12 / Gap 13 boundary audit closure)* below.
- **Resolution (Round A audit, read-only):**
  - **Gmail** is used as an ingestion source only. `GoogleService` exposes read-only Gmail / RFC822 surfaces (`Get*EmailsAsync`, `LoadFullEmailBodyAsync`, label management). `EmailManagementViewModel`'s 52 `_googleService` call sites are all read / label / auth — no Drive uploads, no filing decisions, no Storage-Destination decisions.
  - **Gmail is not** registered or used as a Storage Destination anywhere.
  - **Google Drive is an active Storage Destination** routed only through `GoogleDriveStore` (`IFileStore`) backed by `IGoogleDriveServiceProvider` / `GoogleAuthService`; DI wires it exactly once in `App.xaml.cs`.
  - **Routing is strictly by `ProjectFile.StorageDestination`** (`ProjectFileFilingService` `switch`, with `NotSupportedException` for unrouted destinations and explicit "Use ProjectFileUploadService for Drive-targeted ProjectFiles" for `GoogleDrive`). Drive uploads go through `ProjectFileUploadService` → `GoogleDriveStore`.
  - **Duplicate filename in the target Drive folder is a conflict** — `GoogleDriveStore.UploadAsync` throws `FileStoreConflictException` for data file and sidecar duplicates; no auto-pick, no silent fallback. `GoogleDriveStore` upload failure is re-thrown (“Do NOT swallow as a silent fallback”).
  - **Sidecar metadata** (`SidecarMetadata`) is consumed symmetrically by `AccFileStore` and `GoogleDriveStore`; sidecar write failure is logged as warn, never auto-retried via another store.
  - **Google Drive delete wiring in refile cleanup is still postponed and explicitly marked**: `ProjectFileRefileService` returns a clear skip reason for `FileStorageDestination.GoogleDrive` rather than silently dropping the artifact. Status remains *postponed*.
  - **No routing-decision-in-wrong-layer found:** no `_googleService.*Drive*Upload*` call sites outside the connector, and no `UploadToGoogleDrive` callers anywhere. Drive `Files.*` direct use in reporting / templates / inspection-screenshot services is **not** a Storage-Destination path and is allowed by the Service Catalog.
  - **Follow-up documented only (not approved for action in this round):**
    1. `EmailAttachment` DTO inside `SiOffice.GoogleConnector\GoogleService.cs` carries business-shaped fields (`ProjectFileId`, `ProjectAlternativeId`, `IsFilingInProgress`, `IsLocked`, `CanEditTarget`). The connector still does not make filing / routing decisions — it just hosts a DTO that knows about project-file IDs. **Medium-severity boundary smell, not a runtime bypass.** Candidate for relocation if and when a future connector-cleanup round is opened. **Postponed.**
- **Relevant areas (informational):**  any Google Drive / Sheets helpers within the connector.
  Audit-relevant call sites: `GoogleDriveStore` (upload / conflict /
  re-throw), `IGoogleDriveServiceProvider` / `GoogleAuthService`,
  `ProjectFileFilingService` (routing switch), `ProjectFileRefileService`
  (Drive cleanup skip), `EmailManagementViewModel` (Gmail-only `_googleService`
  usage), reporting / template / screenshot services using `driveService.Files.*`
  for non-Storage-Destination purposes.
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
- **Status:** Partially resolved / Verified — repeated Autodesk authorization
  during email attachment preview is fixed and confirmed by user manual
  verification (see *Round update — Gap 18F / 18G closure (preview
  authorization)* below). Broader auth cleanup (TokenProvider singleton /
  DI lifetime, AccService metadata-read endpoint, full auth-flow review,
  log-hygiene audit) remains **Needs security/auth review / Needs code
  verification**.
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
  *removed (Stage 9E.1 → 9E.4)*. The runtime resolver
  `IProjectFileLocationResolver` (session cache only) replaced any
  remaining use of `ProjectFileInstanceId` as a placement / filing-state
  signal. The entity, the `DbSet`, and both FK columns
  (`EmailInboxAttachment.ProjectFileInstanceId`,
  `InspectionReportDrawing.FileInstanceId`) were removed from the model;
  migration `RemoveProjectFileInstanceTable` is prepared (user-run
  `Update-Database`).
- **Silent fallback to Gmail IDs as business identity** — *not
  approved*. RFC822 `InternetMessageId` is the business identifier;
  `gmail:{message.id}` remains only as a legacy / compatibility
  fallback inside `MessageKeyGenerator`.
- **Friendly handling for external upload missing `ThreadKey`** —
  *candidate*. The current behavior throws; the UX should be improved to
  guide the user instead of surfacing a bare exception.
- **Google Drive upload activation** — *activated (round below)*.
  `GoogleDrive` is now an active value in `FileStorageDestination` and
  routes through `ProjectFileUploadService` →
  `SiNetSQL.FileIndex.Stores.GoogleDriveStore`. Duplicate filename in
  the target Drive folder is treated as a **conflict**
  (`FileStoreConflictException`), never an auto-pick. No silent
  fallback to or from Drive is allowed. **Delete wiring in refile
  cleanup** remains *postponed* to a future approved round.
- **Gmail as a Storage Destination** — *not approved / corrected*.
  Gmail is a read-only ingestion source represented by
  `EmailInboxMessage` / `EmailInboxAttachment`. It is not, and must not
  be added as, a value in `FileStorageDestination` (Gap 5).
- **`ProjectFileInstance.StorageDestination` cleanup** — *removed
  (Stage 9E.4)*. The column was removed together with the rest of the
  `ProjectFileInstance` table via migration
  `RemoveProjectFileInstanceTable`. Routing is now driven exclusively by
  `ProjectFile.StorageDestination` in `ProjectFileFilingService` /
  `ProjectFileUploadService` / `IFileOpenService`. No legacy reads from
  `ProjectFileInstance.StorageDestination` remain in
  `ProjectFileRefileService` or `EmailMoveToProjectApplicationService`.
- **Gmail DOM attachment extraction** — *commented out / not active*
  (Gap 8). The extractor source, DI registration, and
  `EmailManagementView` field / no-op method are commented out and the
  extractor body is parked behind `#if false`. Build and tests pass
  unchanged. **Physical deletion is postponed** to a future approved
  cleanup round.
- **`GmailVisibleAttachmentsDomExtractor` re-enable** — *not approved*.
  Gmail DOM is not a source of truth for attachments; re-enabling
  requires an explicit approved round (Gap 8).

---

## Round update (Gap 9 closure + Google Drive activation)

This round implements the approved plan for Gap 9 (`ProjectFileInstance`
dependency reduction) and the first real activation of Google Drive as a
Storage Destination. **No DB schema, migration, or ModelSnapshot change
was made.**

### `ProjectFileInstance` dependency reduction
- `MoveToProjectProcessActionHandler.IsAttachmentFiledAsync` now reads
  ACC custom-attribute state first (`MoveMovedToProject`). When that
  attribute is present, ACC is authoritative. When it is absent (or
  the ACC read failed), the code falls back to `ProjectFileInstanceId`
  presence — every such branch is marked
  `LEGACY-INSTANCE-FALLBACK` and scoped to "until Move/Lock metadata
  backfill is complete".
- `MoveToProjectProcessActionHandler.TryLoadTargetAccInfoAsync` is now
  explicitly marked `LEGACY-INSTANCE-FALLBACK` (post-filing metadata
  enrichment only).
- `EmailMoveToProjectApplicationService` "already filed" pre-handler
  hint marked `LEGACY-INSTANCE-FALLBACK`. Authoritative decision lives
  in the handler.
- `ProjectFileRefileService` precondition (`att.ProjectFileInstanceId
  is not int oldInstanceId`) marked `LEGACY-INSTANCE-FALLBACK` with
  documented future direction (key on `att.AccItemId` / FS sidecar /
  Drive file id). Old-storage cleanup snapshot annotated as a
  *last-known projection*, not a routing source.

### Storage routing
- `ProjectFileFilingService.FileAsync` now switches strictly on
  `ProjectFile.StorageDestination`. Unknown destinations and the
  `GoogleDrive` slot (which is wired through `ProjectFileUploadService`,
  not the filing service) **fail explicitly**. No silent fallback to a
  different backend.
- `ProjectFileInstance.StorageDestination` reads kept only as
  *last-known cache / projection* (refile cleanup, VM display). Not a
  routing source.

### Google Drive activation
- `SiOffice.GoogleConnector.GoogleDriveService` extended with a general
  `IGoogleDriveService` surface (`FindFilesByNameAsync`,
  `ListFilesAsync`, `UploadFileAsync`, `UploadStringAsync`,
  `DownloadFileAsync`, `TrashFileAsync`, `GetFileInfoAsync`,
  `CheckWriteAccessAsync`) — no parallel connector created.
- `SiNetSQL.FileIndex.Stores.GoogleDriveStore` rewritten end-to-end:
  - Resolves folder handles by walking `ProjectFolder` hierarchy and
    calling `EnsureFolderPathAsync` under the configured
    `ProjectsRootFolderId`.
  - `ListFilesAsync` filters sidecar JSONs and **skips duplicate
    filenames** (no auto-pick).
  - `UploadAsync` refuses to upload when `FindFilesByNameAsync` returns
    any existing file or sidecar with the same name in the target
    folder — raises `FileStoreConflictException` with all candidates.
  - Writes the data file then a `*.si.json` sidecar that reuses the
    existing `SidecarMetadata` schema (no schema drift).
  - Open path downloads by Drive `File.Id` to a temp path and hands off
    to `FileHelpers.OpenFile`.
  - No silent fallback: missing config / auth surfaces as
    `InvalidOperationException` on write paths and empty results on
    read paths.
- `ProjectFileUploadService.UploadToGoogleDriveAsync` replaced the
  `"העלאה ל-Google Drive טרם מיושמת"` stub with a real call to
  `GoogleDriveStore`, surfacing duplicate-name conflicts as failures
  rather than picking arbitrarily.
- New DI wiring in `App.xaml.cs`: `GoogleDriveSettings`,
  `IGoogleDriveServiceProvider` →
  `SiNetProjectManagerV2.Services.GoogleDriveServiceProvider`,
  `GoogleDriveStore` (already exposed as `IFileStore`).
- New `AppConfiguration` accessors `GoogleDriveSharedDriveId` /
  `GoogleDriveProjectsRootFolderId` reading the `GoogleDrive` section.

### Config required to activate Google Drive
The destination is treated as **unavailable** unless both
appsettings keys are set:

```json
"GoogleDrive": {
  "SharedDriveId": "<Shared Drive id>",
  "ProjectsRootFolderId": "<folder id inside the Shared Drive>"
}
```

If either is missing, every Drive operation fails explicitly — there is
no fallback to ACC or the file server. Auth reuses the existing
`GoogleAuthService` singleton (no new OAuth flow).

### Tests
- New file `SiNetSQL.Tests\FileIndex\GoogleDriveStoreTests.cs` covers:
  destination identity, `IsConfigured` when settings are missing,
  explicit failure on missing provider for both upload and open,
  duplicate-data-file conflict, duplicate-sidecar conflict, happy-path
  upload writes data + sidecar, and listing filters sidecars + skips
  duplicates.
- Full xUnit run: **611 tests, 609 passed, 0 failed, 2 pre-existing
  skips** (EF Core InMemory limitation, unrelated).

### Still postponed / not approved in this round
- Physical deletion of `ProjectFileInstance` rows or columns.
- Physical removal of any `LEGACY-INSTANCE-FALLBACK` branch.
- Filing-service direct Google Drive path (currently routed through
  `ProjectFileUploadService`).
- Any DB schema, migration, or ModelSnapshot change.
- Re-enabling `GmailVisibleAttachmentsDomExtractor` (Gap 8).

---

## Round update (Stage 9E.5 — `ProjectFileInstance` removal closure)

This round is **documentation-only**. No production code, DB schema,
migration, `Update-Database`, or test change was made. It closes the
documentation gap left by Stages 9E.1 → 9E.4, which physically removed
`ProjectFileInstance` from the active model after the user verified
runtime behavior manually (app starts, Gmail works, attachment
detection works).

### What changed in the implementation (already done in 9E.1 → 9E.4)
- `ProjectFileInstance` is **no longer a persisted entity**. The
  `ProjectFileInstance.cs` model, `ProjectFileInstanceConfiguration.cs`,
  the `DbSet<ProjectFileInstance>`, and the legacy
  `ProjectFileUploadService.cs` were physically deleted.
- `EmailInboxAttachment.ProjectFileInstanceId` and
  `InspectionReportDrawing.FileInstanceId` were removed from the model
  and its configurations.
- Migration `RemoveProjectFileInstanceTable`
  (`SiNetSQL/Migrations/20260528153051_RemoveProjectFileInstanceTable.cs`)
  drops the `ProjectFileInstance` table, both FKs
  (`FK_EmailInboxAttachment_ProjectFileInstance`,
  `FK_InspectionReportDrawing_FileInstance`), both indexes, and both
  columns. **`Update-Database` is user-run** and is not executed by
  this round.

### Source-of-truth model after removal
- **No source of truth lives in the DB instance row.** There is no
  longer a per-instance DB entity. DB holds business slot data
  (`ProjectFile`, `ProjectAlternative`) and email/attachment business
  metadata, **not** physical-file truth.
- **Physical existence is the actual storage state**, per Storage
  Destination:
  - **ACC** — actual ACC item / folder / version / custom-attribute
    state.
  - **File Server** — actual file path on disk + `*.si.json` sidecar.
  - **Google Drive** — actual Drive file / folder + `*.si.json`
    sidecar.
- **Routing is by `ProjectFile.StorageDestination`** only, dispatched
  in `ProjectFileFilingService` / `ProjectFileUploadService` /
  `IFileOpenService`. There is no silent fallback between destinations.

### Runtime resolver / session cache
- Runtime resolution is performed via `IProjectFileLocationResolver`
  (implementation `ProjectFileLocationResolver`).
- The resolver is a **runtime / session resolver only**. It holds a
  short-lived **in-memory cache** scoped to the current session — it is
  **not** a persisted replacement table and **not** a source of truth.
- The `IFileStore` implementations (`FileServerStore`, `AccFileStore`,
  `GoogleDriveStore`) are the **uniform path** to actual storage state.
  The resolver consults them; it does not duplicate their truth.
- `FileIndexService` (sidecar / index helper) is a helper layer over
  the stores and does not itself own placement truth.

### Google Drive as an active Storage Destination
- `FileStorageDestination.GoogleDrive` is an **active** destination.
- Routing goes through `ProjectFileUploadService` →
  `SiNetSQL.FileIndex.Stores.GoogleDriveStore` (see Round update —
  *Gap 9 closure + Google Drive activation* above).
- **Duplicate filename in the target Drive folder is a conflict**
  (`FileStoreConflictException`); the system never auto-picks among
  duplicates and never falls back silently to another destination.

### Dropped / cancelled (now confirmed by removal)
- `ProjectFileInstance` as a source of truth — **removed**.
- `ProjectFileInstanceId` as file placement / filer state — **removed**.
- `ProjectFileInstance.StorageDestination` as a routing source —
  **removed** (routing is `ProjectFile.StorageDestination` only).
- PFI supersession chain (`OldInstanceId` / `NewInstanceId` /
  similar) — **removed**.
- PFI-based rename warning (`CheckRenamedFileInstance` and its call
  site) — **removed**.
- `ProjectFileUploadService` PFI-era dead code — **removed** (file
  deleted).

### Postponed (not in this round; require explicit future approval)
- Google Drive **delete** wiring in refile cleanup.
- Future rename-warning replacement built on `FileIndex` / sidecar
  rather than the removed PFI signal.
- Any audit mechanism replacing the old PFI supersession fields.
- Minor email-flow / UI bugs observed during manual verification —
  intentionally **not** addressed in this documentation round.

### Not done in this round (explicit)
- **No** production code changes.
- **No** DB schema changes.
- **No** migration created.
- **No** `Update-Database` executed.
- **No** test changes.
- **No** new gap opened.

---

## Round update (Round A.1 — Gap 12 / Gap 13 boundary audit closure)

This round is **documentation-only**. No production code, DB schema,
migration, `Update-Database`, or test change was made. It records the
result of the read-only Round A audit of the ACC (Gap 12) and Google /
Gmail / Drive (Gap 13) service boundaries performed after Stage 9E.5.

### Outcome
- **Gap 12 status: Confirmed — ACC boundary intact; follow-ups documented.**
  No metadata-corruption risk found, no silent fallback between Storage
  Destinations, no ViewModel writes ACC metadata directly, and
  `AccItemId` / `AccVersionId` are not used as DB-side source of truth.
- **Gap 13 status: Confirmed — Google / Gmail / Drive boundary intact;
  follow-up documented.** Gmail stays ingestion-only, Google Drive is
  routed only through `GoogleDriveStore` / `IFileStore` with duplicate
  filename = conflict and no silent fallback, routing is decided strictly
  by `ProjectFile.StorageDestination`, and Drive delete wiring in refile
  cleanup remains explicitly *postponed* (clear skip reason, not silent).

### Documented follow-ups (no action approved in this round)
1. **`EmailIngestionService` direct `Bim360Service` use for ACC Inbox
   uploads** (message folder / attachments / `00_Email.pdf` /
   `manifest.json` / ZIP children, ~7 sites). Stays inside the connector
   boundary and owns the Inbox-side upload; does not use the newer
   `IAccFileClient` abstraction. **Candidate for future unification**
   once a matching Inbox API on `IAccFileClient` is defined.
2. **`AccFileSyncService` batch path** — already marked *deprecated for
   single-file filing* in its XML doc; kept only for
   `EmailManagementViewModel.TryAutoSyncToProjectAsync`. **Candidate for
   staged removal** after that one consumer is migrated to
   `IProjectFileFilingService.FileAsync`.
3. **`ProjectWorkViewModel` / `ProjectFolderNode` resolve `AccFileStore`
   via `ServiceLocator` + concrete-type cast** (`OfType<AccFileStore>()`).
   Not a bypass — the store is the right layer — low-severity boundary
   smell to clean up opportunistically.
4. **`EmailAttachment` DTO lives inside
   `SiOffice.GoogleConnector\GoogleService.cs`** with business-shaped
   fields (`ProjectFileId`, `ProjectAlternativeId`,
   `IsFilingInProgress`, `IsLocked`, `CanEditTarget`). Medium-severity
   boundary smell, **not** a runtime bypass — the connector still does
   not make filing / routing decisions. Candidate for relocation if and
   when a future connector-cleanup round is opened.

### Dropped / cancelled / postponed (this round)
- ACC boundary refactor — **not approved** in this round.
- Google connector DTO refactor (`EmailAttachment` relocation) —
  **postponed**.
- `AccFileSyncService` staged removal — **postponed**.
- `ServiceLocator` + concrete `AccFileStore` cast cleanup in
  `ProjectWorkViewModel` / `ProjectFolderNode` — **postponed**.
- Google Drive delete wiring in refile cleanup — remains **postponed**
  (already marked).
- Replacing `Bim360Service` direct usage in `EmailIngestionService` with
  `IAccFileClient` — **postponed** pending an Inbox-shaped API.

### Not done in this round (explicit)
- **No** production code changes.
- **No** DB schema changes.
- **No** migration created.
- **No** `Update-Database` executed.
- **No** test changes.
- **No** new gap opened.
- **No** parallel mechanism / service / DTO introduced — only existing
  surfaces (`IAccItemMetadataService`, `IProjectFileAccMetadataWriter`,
  `IAccFileClient`, `IFileStore`, `GoogleDriveStore`,
  `IGoogleDriveServiceProvider`, `ProjectFileFilingService`,
  `ProjectFileRefileService`, `MoveToProjectProcessActionHandler`) were
  referenced.

---

## Round update (Gap 18F / 18G closure — preview authorization)

This round closes the **specific** symptom under Gap 18 where the Autodesk
"Authorize application" window appeared on every click on an email
attachment that was already in ACC. The broader Gap 18 (secrets /
credentials / token storage alignment) remains open and unchanged.

### Symptom that was fixed

- Clicking an attachment in the email screen to preview/open it in ACC
  triggered the Autodesk OAuth "Authorize application" browser window
  on every click, even though upload / filing worked silently.
- Logs showed, per click, repeated `[TokenProvider] Authorization URL: …`
  lines, `Starting localhost listener on port 8080`, occasional
  `HttpListener FAILED`, and `Browser authorization timed out after 180 seconds`
  surfaced from `AccItemMetadata ReadAttributes`.

### Real root cause (two parts)

1. **WebView2 profile split (Gap 18F).** Before the round, the app had
   distinct WebView2 `UserDataFolder`s:
   - Gmail / Calendar profile: `%LOCALAPPDATA%\SiNetProjectManagerV2\WebView2UserData\{user_at_domain}`
   - ACC viewer profile: `%LOCALAPPDATA%\SiNetProjectManagerV2\WebView2UserData\acc_viewer`
   - `ExternalBrowserWindow` (email attachment ACC preview) used the
     Gmail profile, and could fall back to a per-instance temp profile
     when `CurrentUserEmail` was not yet set.
   These three profiles each had their own Autodesk cookie jar, so an
   Autodesk login in one window did not carry over to the others.
2. **Interactive OAuth during metadata read (Gap 18G).** The preview
   path executed:
   `EmailManagementViewModel.ShowAttachmentInAccAsync` →
   `IAccInboxReconciliationService.ReconcileByMessageIdAsync` →
   `AccInboxReconciliationService.ReadAttributesOrEmptyAsync` →
   `AccItemMetadataService.ReadAttributesAsync` →
   `Bim360Service.GetItemCustomAttributesAsync` →
   `TokenProvider.GetThreeLeggedAdminTokenAsync`. When the cached
   access token was expired and refresh failed, that path went straight
   into `PerformBrowserAuthorizationAsync` → built the Autodesk
   `authorize?...` URL → started `HttpListener` on port 8080 → blocked
   for up to 180 seconds, on every preview click. Concurrent previews
   could even race two `HttpListener` binds on port 8080.

### What changed (production code)

- **Gap 18F — unified WebView2 profile.** All in-app WebView2 windows
  now share a single persistent profile via the existing
  `WebView2Helper` infrastructure:
  - New `WebView2Helper.CreateSharedEnvironmentAsync(string source)`
    rooted at `{AppConfiguration.WebView2UserDataBasePath}\Default`.
  - `CreateUserEnvironmentAsync()` and the legacy
    `CreateAccEnvironmentAsync()` now delegate to it (kept for source
    compatibility; `CreateAccEnvironmentAsync` is **staged removal** in
    docs — see Postponed below).
  - `WebView2Helper.InitializeCoreWebView2Async` and
    `ExternalBrowserWindow.OnLoaded` use the shared environment.
  - The `IsAccViewer` attached property is retained for
    navigation / new-window behavior; it **no longer** drives profile
    selection (documented inline).
  - A minimal one-line log per environment creation
    (`[WebView2][SharedProfile] source=… kind=Unified folder=…`).
  - No new configuration key, no new path concept (reused
    `AppConfiguration.WebView2UserDataBasePath`).
- **Gap 18G — suppress interactive OAuth during preview/metadata read.**
  Surgical additions, no auth-flow rewrite:
  - `TokenProvider` adds an `AsyncLocal<bool>`-backed scope:
    `TokenProvider.SuppressInteractiveBrowserAuthScope()` and a new
    `TokenProvider.InteractiveAuthSuppressedException`. When the scope
    is active and no valid cached/refresh token is available,
    `GetThreeLeggedAdminTokenAsync` throws the suppression exception
    instead of opening the browser / `HttpListener`.
  - `TokenProvider` adds a process-wide `SemaphoreSlim _browserAuthGate`
    around `PerformBrowserAuthorizationAsync` so concurrent callers do
    not both pop the browser or both bind `HttpListener` on port 8080;
    callers that wake up after the winner reuse the freshly cached /
    refreshed token.
  - `AccItemMetadataService.ReadAttributesAsync` wraps its work in
    `using TokenProvider.SuppressInteractiveBrowserAuthScope()`,
    converts `InteractiveAuthSuppressedException` into the existing
    failure result (`http=401`), and logs
    `[AccItemMetadata][Auth] ReadAttributes interactive-auth=SUPPRESSED`.
    The existing `MetadataReadFailed=true` path is unchanged; the
    viewer still opens from the verified
    `AccItemId / OpenAccFolderId / OpenAccItemId`.
  - `EmailManagementViewModel.ShowAttachmentInAccAsync` also opens the
    suppression scope around `ReconcileByMessageIdAsync` as a
    belt-and-braces guarantee and adds a `[ShowInACC][Auth]` log line.
  - The write path (`WriteAttributesAsync`) was **not** changed —
    explicit user actions (filing, project actions) may still trigger
    Autodesk authorization where it is acceptable.

### User verification — passed

- The user manually clicked multiple email attachments that already
  exist in ACC.
- The ACC preview opened.
- The Autodesk "Authorize application" window **no longer appears**
  on each click.
- This confirms both Gap 18F (shared WebView2 session) and Gap 18G
  (no interactive OAuth on the preview/metadata-read path).

### Follow-ups / postponed (no action approved in this round)

- **AccService metadata-read endpoint** — `SiOffice.AccService`
  currently has no endpoint for ACC item custom-attribute reads. A
  future round can add one so the client offloads metadata reads and
  never touches Autodesk OAuth in preview paths. Out of scope here.
- **`TokenProvider` lifetime cleanup** — make `ITokenProvider` a
  singleton in DI and replace direct `new TokenProvider(...)`
  call sites (notably `AccItemMetadataService.CreateBim360Service`).
  Postponed.
- **Full Autodesk auth architecture review** — not approved.
- **WebView2 legacy profile cleanup** —
  `WebView2Helper.CreateAccEnvironmentAsync` and the on-disk
  `…\WebView2UserData\acc_viewer` folder are **staged removal**
  candidates after a testing period; not deleted in this round.
- **Auth log hygiene hardening** — verify that no code path prints the
  full Autodesk `client_id`, OAuth tokens, authorization codes, or
  cookies. Existing diagnostics use `clientIdTail` only; a follow-up
  pass should sweep all `[TokenProvider]` / `[AccService]` /
  `[Bim360Service]` log call sites.

### Not done in this round (explicit)

- **No** DB / schema changes.
- **No** migration created.
- **No** `Update-Database` executed.
- **No** test changes.
- **No** new gap opened.
- **No** new mechanism / endpoint / service / DTO introduced —
  changes reused existing surfaces (`TokenProvider`,
  `IAccItemMetadataService`, `IAccInboxReconciliationService`,
  `WebView2Helper`, `AppConfiguration.WebView2UserDataBasePath`).
- **No** change to `TokenProvider` token storage path, `ClientId`,
  `ClientSecret`, scopes, redirect URI, callback port, refresh logic.
- **No** change to upload / filing flow.
- **No** physical deletion of `CreateAccEnvironmentAsync` or the
  `acc_viewer` folder.
- **No** new fallback added (failures still flow through the existing
  `MetadataReadFailed` path).

### Recommended next round — Application functional testing

Pause cleanup / refactor and run a real-usage validation sweep:

- App startup.
- Gmail load.
- Email attachment detection.
- Open attachment preview from email (verify no repeated Autodesk
  authorization).
- File attachment to project / MoveToProject.
- Verify file appears in the project tree / list.
- Open filed project file.
- ACC storage flow.
- File Server storage flow.
- Google Drive storage flow (where configured).
- Status indicators / metadata status.
- Confirm no `ProjectFileInstance` dependency regression.
- Confirm no repeated Autodesk authorization during preview.

---



- Did **not** change code.
- Did **not** change DB / schema / migrations / ModelSnapshot.
- Did **not** create a new mechanism, service, handler, or storage path.
- Did **not** re-enable any disabled mechanism.
- Did **not** edit EF migration files, Designer.cs, or ModelSnapshot.

---

## Round update (Gap 18H closure — ACC Inbox identity widening)

**Status proposed:** *Resolved / Verified for ACC Inbox folder key length —
16-char keys implemented; migration prepared; existing folders not
migrated.*

This round closes the **specific** symptom under Gap 18H where ACC Inbox
`THREAD_*` / `MSG_*` folder names were derived from only 8 hex chars of
SHA256, raising a long-term collision-risk concern. The broader Gap 18
(secrets / credentials / token storage alignment) remains open.

### What changed (production code)

- **`MessageKeyGenerator.GetMessageKey`** — output widened from
  `fullHash[..8]` to `fullHash[..16]` (64 bits of uniqueness).
- **`MessageKeyGenerator.GetThreadKey`** — output widened from
  `fullHash[..8]` to `fullHash[..16]` (64 bits of uniqueness).
- **`EmailInboxMessageConfiguration.ThreadKey`** — EF column configuration
  changed from `HasMaxLength(8)` to `HasMaxLength(16)`. `IsRequired()`,
  `IsUnicode(false)`, and the existing `IX_EmailInboxMessage_ThreadKey`
  index were preserved.
- **`EmailIngestionService.CreateManifest`** — `manifest.json` now
  includes the full identity surface:
  - `internetMessageId`
  - `messageUniqueId`
  - `threadUniqueId` *(newly added)*
  - `messageKey`
  - `threadKey` *(newly added)*
  - `gmailMessageId`
  - `gmailThreadId`

  Existing field names were not renamed or removed; only the missing
  identity fields were added.

### Identity / fallback rules — unchanged

- `MessageKeyGenerator.GetMessageUniqueId` still prefers the RFC 2822
  `Message-ID` and only falls back to `gmail:{gmailMessageId}` when no
  `InternetMessageId` exists.
- `MessageKeyGenerator.GetThreadUniqueId` still derives from
  `References` / `In-Reply-To` / `InternetMessageId` and explicitly
  avoids using Gmail `threadId` as the business identity.
- No new identity mechanism was introduced and no new fallback was added.

### Migration

- Migration **created and reviewed**:
  `20260528210621_WidenEmailInboxThreadKeyTo16` (context
  `SiNetSQLDbContext`, project `SiNetSQL`).
- Migration content verified clean — only an `AlterColumn` on
  `EmailInboxMessage.ThreadKey` from `varchar(8)` to `varchar(16)`. The
  existing `IX_EmailInboxMessage_ThreadKey` index is preserved.
- No other column / table / index / seed changes were introduced.
- `Update-Database` is **not** part of this documentation round; it will
  be executed manually by the user against the relevant environments.

### Existing ACC folders — explicitly not touched

- Existing `THREAD_<8 hex>` / `MSG_<8 hex>` folders in ACC were **not**
  renamed, **not** moved, and **not** deleted by code.
- The user confirmed that the old content is not important and may be
  removed manually or in a separate, dedicated cleanup round.
- Legacy 8-char prefix detection in `AccInboxLayout`
  (`IsThreadFolderName`, `IsLegacyMessageFolderName`) was kept in place
  so old folders continue to be recognized.

### What was not done in this round (explicit)

- Did **not** rename / move / delete existing ACC folders from code.
- Did **not** create a new identity mechanism or add any fallback.
- Did **not** change `AccInboxLayout` naming logic (it consumes the
  longer keys transparently).
- Did **not** touch `ProjectFileInstance`, refile, MoveToProject
  service, ACC metadata write path, `SetItemCustomAttributesAsync`, or
  `TokenProvider`.
- Did **not** edit `ModelSnapshot.cs` manually; the migration
  regenerated it through the standard EF workflow.

---

## Round closure (ProjectFileInstance / Auth Preview / ACC Inbox Identity)

This documentation-only round closes the three implementation lines that
have been completed and verified, and explicitly hands off to an
application functional testing round. No production code, schema,
migration, or test changes were made in this round.

### Gaps closed / updated in this round

- **`ProjectFileInstance` removal** — already implemented and documented
  in *Round update (Stage 9E.5 — `ProjectFileInstance` removal closure)*
  above. Confirmed as the source-of-truth state going forward; runtime
  resolver / session cache remains the only projection path.
- **Gap 18F / 18G — Autodesk authorization during ACC preview** —
  already implemented and user-verified; see *Round update (Gap 18F /
  18G closure — preview authorization)* above. Unified WebView2 profile
  and interactive-OAuth suppression during preview metadata reads
  remain in effect.
- **Gap 18H — ACC Inbox key widening to 16 chars + manifest identity
  fields** — implemented this round and documented in the section
  immediately above.

### Final status of Gap 18 after this round

**Status proposed:** *Partially resolved / usage blockers fixed; broader
auth cleanup postponed.*

The usage-blocking symptoms under Gap 18 are resolved and verified:

- Repeated Autodesk authorization during ACC preview — **fixed and
  manually verified**.
- WebView2 split profile — **resolved** via the unified profile.
- Interactive OAuth during preview metadata read — **suppressed**.
- ACC Inbox short-key collision risk — **mitigated** by 16-char keys and
  manifest identity hardening.

The broader Gap 18 (secrets / credentials / token storage alignment and
the full Autodesk auth architecture) remains open as a tracked, lower-
urgency cleanup. Nothing in the current application flow is blocked on
it.

### Follow-ups / postponed (no action approved; tracked only)

- `AccService` metadata-read endpoint (move client-side metadata reads
  behind the service boundary).
- `TokenProvider` singleton / DI cleanup; replace direct
  `new TokenProvider(...)` instantiations.
- Full Autodesk auth architecture review.
- Auth log hygiene sweep (centralize and trim `[Auth]` logs).
- `CreateAccEnvironmentAsync` / `acc_viewer` physical cleanup —
  postponed until after functional testing confirms the unified profile
  is stable.
- Google Drive delete wiring in refile cleanup.
- Cleanup of old ACC `_Inbox` `THREAD_<8>` / `MSG_<8>` folders —
  **manual / separate cleanup only**, not from code in this round.

### Recommended next round — Application Functional Testing

Pause cleanup / refactor and run a real-usage validation sweep before
opening any new gap:

- Application startup.
- DB connection.
- Gmail login / session restore.
- Gmail load.
- Email attachment detection.
- Open attachment preview from email.
- Verify **no repeated Autodesk authorization** during preview.
- MoveToProject / file attachment to project.
- Verify file appears in the project tree / list.
- Open filed project file from the project.
- ACC storage flow.
- File Server storage flow.
- Google Drive storage flow (where configured).
- Metadata / status indicators.
- System status / notifications.
- Verify no `ProjectFileInstance` dependency / regression.
- Verify new ACC Inbox folders use 16-char `THREAD_*` / `MSG_*` names.
- Verify the new `manifest.json` includes the full identity fields
  (`internetMessageId`, `messageUniqueId`, `threadUniqueId`,
  `messageKey`, `threadKey`, `gmailMessageId`, `gmailThreadId`).

### What was not done in this round (explicit)

- Did **not** change production code.
- Did **not** change DB / schema.
- Did **not** create a migration.
- Did **not** run `Update-Database`.
- Did **not** change tests.
- Did **not** change auth flow.
- Did **not** delete old ACC folders from code.
- Did **not** open a new gap or start a new refactor.

### Gap 19 — `GmailPopoutUrl` used a live `#inbox/{ThreadId}` fallback (violates EmailSystemPrinciples §4.3)

- **Date identified:** 26.05.2026
- **Domain:** Email, UI.
- **Desired principle:**
  The Gmail WebView2 local-open URL is `#all/{message.id}`. The legacy
  `#inbox/{threadId}` form **must not** be used as the default open URL; it
  remains available as a **logged reference only**. See
  [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Domains/Email/EmailSystemPrinciples-2026-05-26.md) §4.1 and §4.3.
- **Current known / suspected state (before fix):**
  `EmailInfo.GmailPopoutUrl` preferred `#all/{MessageId}` but **fell back**
  to a live `#inbox/{ThreadId}` open URL when `MessageId` was empty. That
  fallback is an active open path, not a logged reference, contradicting
  §4.3.
- **Impact / risk:**
  When the local Gmail `message.id` is unavailable, the UI would silently
  open a thread-level URL derived from the mailbox-local `ThreadId`,
  blurring the message-level / global-identity contract of the email
  domain.
- **Status:** Fixed — code aligned (2026-05-26 round).
- **Resolution (2026-05-26):**
  `EmailInfo.GmailPopoutUrl` now returns `#all/{MessageId}` when a local
  `MessageId` is present and `null` otherwise. The `#inbox/{ThreadId}`
  fallback was removed from the open path; the legacy thread URL form is
  still emitted **only** as a `[GmailOpenUrl]` diagnostic reference by
  `EmailManagementView.LogGmailOpenUrlOptions`. Consumers already guard on
  `string.IsNullOrEmpty(GmailPopoutUrl)`, so the `null` result is handled
  safely. Build succeeded; no schema, model, migration, or test change was
  required.
- **Relevant files / classes (informational):**
  `SiOffice.GoogleConnector\GoogleService.cs` (`EmailInfo.GmailPopoutUrl`),
  `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml.cs`
  (`LogGmailOpenUrlOptions` — logged reference only),
  `SiNetSQL\MVVM\EmailManagementViewModel.cs` (consumer, null-guarded).

### Gap 20 — Opening an email from a task did not resolve `rfc822msgid` to a local `message.id`

- **Date identified:** 26.05.2026
- **Domain:** Email, UI.
- **Desired principle:**
  When opening an email from a task, the DB-persisted identifier is the global
  RFC 2822 `Message-ID`. The system searches the current user's Gmail with
  `rfc822msgid:{Message-ID}` to obtain the **mailbox-local** `message.id` for
  local actions (open in WebView2 via `#all/{message.id}`, read, download). If
  no match is found, show the user a message — do not create records or
  fabricate identifiers. See
  [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Domains/Email/EmailSystemPrinciples-2026-05-26.md) §3 and §4.2.
- **Current known / suspected state (before fix):**
  When the email was not on the current page/filter,
  `EmailManagementViewModel.EnsureTaskEmailLoadedAsync` synthesized an
  `EmailInfo` shell via `BuildSynthesizedTaskEmail`, which set
  `EmailInfo.MessageId` to the **global** `MessageUniqueId` (RFC 2822
  Message-ID). That value then fed `GmailPopoutUrl` as `#all/{global-id}`,
  producing a broken open URL and violating §4.3 (no RFC 2822 id in `#all/`).
  No `rfc822msgid` resolution existed.
- **Impact / risk:**
  Opening a task-linked email that was not already loaded produced an invalid
  Gmail open URL, and cross-mailbox opening (different user / machine) could
  not work because no global→local translation was performed.
- **Status:** Fixed — code aligned + tests (2026-05-26 round).
- **Resolution (2026-05-26):**
  - Added connector primitive
    `GoogleService.ResolveLocalMessageIdByRfc822Async(internetMessageId, ct)`
    — runs `Users.Messages.List` with `q = rfc822msgid:{cleaned-id}`
    (throttled), returns the first local `message.id` or `null`. It performs
    only the Gmail API lookup and makes no business decision (per §11.2); it
    does not throw on "not found" and does not fabricate an id.
  - `BuildSynthesizedTaskEmail` now takes an optional `localGmailMessageId`,
    sets `EmailInfo.MessageId` **only** from the resolved local id (never the
    global Message-ID), and carries the RFC 2822 identity on
    `EmailInfo.InternetMessageId`. When unresolved, `MessageId` stays empty so
    `GmailPopoutUrl` is `null` (consistent with Gap 19).
  - `EnsureTaskEmailLoadedAsync` checks the in-memory list first (cheap), then
    resolves `rfc822msgid` against the current mailbox only when needed, and
    shows a "not found in this mailbox" `StatusMessage` when there is no match.
  - The `#search/rfc822msgid` open URL *inside the WebView2* remains postponed;
    the implemented path resolves to a local `message.id` and uses the existing
    `#all/{message.id}` form. Build succeeded; the
    `EmailManagementViewModel_TaskFilingTests` suite passed (20/20), including a
    new test asserting the global Message-ID never feeds `MessageId`. No schema,
    model, or migration change was required.
- **Relevant files / classes (informational):**
  `SiOffice.GoogleConnector\GoogleService.cs`
  (`ResolveLocalMessageIdByRfc822Async`),
  `SiNetSQL\MVVM\EmailManagementViewModel.cs`
  (`EnsureTaskEmailLoadedAsync`, `BuildSynthesizedTaskEmail`),
  `SiNetProjectManagerV2\MainWindow.xaml.cs` (`NavigateToEmailAsync` caller),
  `SiNetSQL.Tests\MVVM\EmailManagementViewModel_TaskFilingTests.cs`.

---

## Round update — Authorization Alignment Round 1 (2026-06-18)

**Scope:** Close critical internal authorization gaps per [`AuthorizationPrinciples-2026-06-18.md`](../Domains/Authorization/AuthorizationPrinciples-2026-06-18.md).

### Closed gaps

| ID | Gap | Fix |
|----|-----|-----|
| AUTH-01 | `CurrentUserContext.Initialize()` did not check `IsActive` or `Role == Unauthorized`. Inactive/unauthorized users could gain `HasAccess == true`. | Added `IsActive` and `Role != Unauthorized` checks in `Initialize()`. Blocked users get `_currentUser = null`, `HasAccess = false`. |
| AUTH-02 | `RequireAdminAccess` helper in `MainWindow` checked `IsFullAccess` (= Management) instead of `IsAdmin`. 11 of ~15 admin/management handlers had no authorization check at all. | Fixed helper to check `IsAdmin`. Added `RequireManagementAccess` helper. Added guards to all 11 unprotected handlers. |
| AUTH-03 | `UserService.UpdateUsersAsync` had no admin check (unlike `AddUserAsync`). No self-protection against admin self-deactivation or self-demotion. | Added `RequireAdmin()` check. Added self-protection (`InvalidOperationException` on self-deactivate/self-demote). Added audit logging for Role, IsActive, AccUserType changes. |
| AUTH-04 | `ActionPermission` model XML docs stated "open access when no rows" — contradicting deny-by-default principle. No centralized `ActionPermissionService` existed. | Updated XML docs. Created `IActionPermissionService` / `ActionPermissionService` with deny-by-default, admin bypass, structured logging. Registered in DI. |
| AUTH-05 | `ActionPermissionWindow` had no admin guard. `AssignActionDialog` showed all employees when no permission rows existed (open access). | Added admin guards to `ActionPermissionWindow` (constructor + Save). Changed `AssignActionDialog` to deny-by-default (empty list when no rows, admin override). |

### Open authorization gaps (candidates for future rounds)

| ID | Gap | Domain |
|----|-----|--------|
| AUTH-O1 | `ProjectService` / project creation has no service-level authorization check. | Authorization / Projects |
| AUTH-O2 | `StatusMappingService` has no admin check on save operations. | Authorization / StatusMapping |
| AUTH-O3 | Task ownership/permission — no per-task authorization model beyond role-level. | Authorization / Tasks |
| AUTH-O4 | `ActionExecutor` / `ProcessActionDispatcher` does not enforce `ActionPermission` before dispatching. | Authorization / Workflow |
| AUTH-O5 | Email/File action handlers do not check `ActionPermission` at execution time. | Authorization / Email |
| AUTH-O6 | Full audit model — no centralized audit log for authorization decisions. | Authorization / Diagnostics |
| AUTH-O7 | Project-level permissions — deferred per scope constraints. | Authorization / Projects |

---

## Round update — Documentation Alignment Round (2026-06-26)

**Scope:** Documentation-vs-code alignment review. No code changes.

### Gap 21 — Personal Work Queues by Task Size: design documented, not implemented

- **Date identified:** 26.06.2026
- **Domain:** ProjectWork / Task queue.
- **Desired principle:**
  Each employee has three personal work queues — Quick / Medium / Long — with
  task priority scoped by `AssignedToId + WorkQueueBucket`. See
  [`Domains\ProjectWork\PersonalWorkQueuesByTaskSize-2026-06-23.md`](../Domains/ProjectWork/PersonalWorkQueuesByTaskSize-2026-06-23.md).
- **Current known state:**
  The design document is complete and accurate in its description of the
  existing mechanism (TaskPriorityEngine, WorkPriorityService, TaskService,
  TaskFactory, UI). However, **zero implementation exists**:
  - `ProjectAssignment.WorkQueueBucket` — does not exist in the model.
  - `TaskType.DefaultWorkQueueBucket` — does not exist in the model.
  - `TaskPriorityEngine` scopes by `AssignedToId` only (no bucket parameter).
  - `WorkPriorityService` — all methods accept `employeeId` only.
  - `TaskService.ReorderTask` — scopes per employee, not per bucket.
  - UI shows a single flat queue per employee (no three-tab layout).
  - No `BucketChange` event type in `TaskEventTypes`.
- **Impact / risk:**
  Low — this is a planned future feature, not a regression. The document
  explicitly marks itself as "Proposed / Documentation Design" (Status line)
  and "Documentation-only" (line 7).
- **Status:** Open — future implementation. Do not implement now.

### Gap 22 — Migration Design document stale after user decisions

- **Date identified:** 26.06.2026
- **Domain:** Migration.
- **Desired principle:**
  The migration design document should reflect the latest approved direction:
  user-selected target template from dropdown, not JSON-derived sections.
- **Current known state:**
  `GoogleSheetReviewMigrationDesign-2026-06-21.md` contained multiple stale
  claims including: building `TemplateSyncRow` from JSON section codes,
  calling `TemplateSyncService.SyncAsync` with JSON-derived data,
  requiring `ProjectAlternative` in Phase 2, calling `MarkReportAsSentAsync`
  in the first Phase 2 slice, and missing the Template Compatibility Preview
  section entirely.
- **Impact / risk:**
  High — if followed literally, the stale design would produce the wrong
  implementation (deriving template structure from partial JSON instead of
  using the user-selected template).
- **Status:** Fixed — documentation updated 26.06.2026.
- **Resolution (26.06.2026):**
  Decision 3 rewritten. §6 mechanisms table updated. §9.1 commit sequence
  revised. §10.2 section creation revised. §8.5 Template Compatibility
  Preview section added. §15 technical checks updated. §17 dropped items
  expanded. All stale JSON-derived section patterns removed.

### Gap 23 — Phase 2 Plan stale after user decisions

- **Date identified:** 26.06.2026
- **Domain:** Migration.
- **Desired principle:**
  The Phase 2 plan should use user-selected target template for section
  structure, not JSON-derived sections.
- **Current known state:**
  `GoogleSheetReviewMigrationPhase2Plan-2026-06-22.md` had its entire
  data flow (§3 Step C) built around the forbidden JSON-to-sections pattern.
  §6 had a standalone `ProjectAlternative` fallback. `MarkReportAsSentAsync`
  was in the first slice.
- **Impact / risk:**
  High — same as Gap 22.
- **Status:** Fixed — documentation updated 26.06.2026.
- **Resolution (26.06.2026):**
  Data flow Step C rewritten. Alternative §6 revised. MarkReportAsSentAsync
  postponed. Template Compatibility prerequisite added. First slice scoping
  added. Note import rule clarified (only matched sections).

### Gap 24 — Template Compatibility Preview implemented but not documented in migration design

- **Date identified:** 26.06.2026
- **Domain:** Migration.
- **Desired principle:**
  All implemented mechanisms should be documented in the relevant design docs.
- **Current known state:**
  Template Compatibility Preview was implemented (ComboBox dropdown,
  `TemplateCompatibilityResult`, `ValidateTemplateCompatibility()`,
  per-section compatibility in detail window) but was not mentioned in any
  of the three migration documents.
- **Impact / risk:**
  Medium — undocumented mechanism creates risk of future misalignment.
- **Status:** Fixed — documentation updated 26.06.2026.
- **Resolution (26.06.2026):**
  §8.5 added to Migration Design doc. Phase 2 Plan updated with template
  prerequisites. Readiness doc updated with template compatibility criteria.

### Gap 25 — SystemHealthGoogleDiagnosticsIntegration missing metadata header

- **Date identified:** 26.06.2026
- **Domain:** Configuration / Diagnostics.
- **Desired principle:**
  Per project rule #4, every document must start with Title, Date, Status,
  and Scope.
- **Current known state:**
  `SystemHealthGoogleDiagnosticsIntegration-2026-06-19.md` starts directly
  with `## 1. Purpose` — missing the required Date, Status, and Scope
  metadata header.
- **Impact / risk:**
  Low — metadata violation only. Code and content are otherwise aligned.
- **Status:** Fixed — metadata header added 26.06.2026.

### Gap 26 — TaskService.ReassignTask does not update WorkPriority

- **Date identified:** 26.06.2026 (previously documented in Migration Design
  §7 Gap 1 and PersonalWorkQueuesByTaskSize §10.5, but not in this register).
- **Domain:** ProjectWork / Task queue.
- **Desired principle:**
  When a task is reassigned to a new employee, `WorkPriority` should be
  updated: compact the old assignee's queue and insert the task at the end
  of the new assignee's queue.
- **Current known state:**
  `TaskService.ReassignTask()` (in `SiNetSQL\Services\TaskService.cs`)
  changes only `AssignedToId`, `Modified`, and `EditorId`. It does **not**
  call `CompactAfterRemoval` for the old assignee or `GetNextPriority` /
  `InsertInQueue` for the new assignee. The task retains its old
  `WorkPriority` value after reassignment.
- **Impact / risk:**
  Medium — creates queue ordering inconsistency. After reassignment,
  the new assignee may have duplicate `WorkPriority` values or gaps.
  This gap widens in scope once Personal Work Queues (Gap 21) are
  implemented, because compact/insert must then be bucket-scoped.
- **Status:** Open — postponed / future task. Not part of current round.
  Referenced in:
  - `GoogleSheetReviewMigrationDesign §7 Gap 1`
  - `PersonalWorkQueuesByTaskSize §10.5`

### Gap 27 — Phase 2 first slice implemented (selected rows only)

- **Added:** 26.06.2026
- **Status:** Implemented (first slice) — remaining items documented below
- **Category:** Migration
- **Impact:** Phase 2 first slice imports selected preview rows into DB using existing services only.

**What the first slice implements:**
- `ReportImportService` as thin coordinator (no direct DB logic)
- `ImportReportsSelectedButton` in Tab 3 of `MigrationPocWindow`
- EnsureSeries via `TemplateSyncService.EnsureSeriesAsync`
- Template sync via `TemplateSyncService.SyncAsync` (once per series)
- Duplicate guard via `(SeriesId, ReportNumber)` EF query
- Report creation via `InspectionReportService.CreateReportAsync`
- Note filling via `SaveNotesAsync` / `AddNoteAsync` / `SaveImportedPlannerResponsesAsync`
- Template-shaped import: only sections passing `IsImportEligible(parentSectionCode)` are imported
- Status mapping: StatusKey → `InspectionNoteStatus.StatusId` (best-effort, nullable fallback)
- Gap notes: empty placeholder creation via `AddNoteAsync` if safe
- Inspector fields: mapped reviewer from preview row (`MappedReviewerUserId`), not current user
- Confirmation dialog before import

**What remains postponed:**
- Import all rows (broad import button)
- GeneralFields / Chapter 0 import (fuzzy matching deferred)
- `MarkReportAsSentAsync` / historical version locking
- `ProjectAlternative` creation
- Workflow reconstruction (Phase 3)
- Task creation (Phase 3)
- Google Sheets / Index Sheet writeback
- Automatic rollback

**Files:**
- `Services\Migration\ReportImportService.cs` (new)
- `Services\Migration\Models\ReportImportResult.cs` (new)
- `WPF Window\MigrationPocWindow.xaml` (modified)
- `WPF Window\MigrationPocWindow.xaml.cs` (modified)

### Gap 28 — Phase 2 skipCarryOver, duplicate prevention, validation defaults

- **Added:** 26.06.2026
- **Status:** Implemented
- **Category:** Migration
- **Impact:** Fixes duplicate key crash, prevents carry-over in migration mode, adds conditional validation defaults.

**Changes:**
- `IInspectionReportService.CreateReportAsync` extended with `bool skipCarryOver = false`
- `InspectionReportService.CreateReportAsync` wraps carry-over in `if (!skipCarryOver)` guard
- `ReportImportService` passes `skipCarryOver: true` — skips CarryOver, CopyGeneralFields, CopyReviewedFiles
- Duplicate prevention via `(SectionId, NoteSubIndex)` lookup before every AddNoteAsync
- Validation defaults: only when `IsLatestVersion==false` AND both NoteText and StatusKey are empty
- "Passed" NOT assigned when note has text but missing status — logged as warning instead
- Latest/active reports keep validation gaps visible

**Postponed:**
- Sheet status based validation classification
- GeneralFields / Chapter 0 import
- MarkReportAsSentAsync

**Files:**
- `SiNetSQL\Services\InspectionSync\IInspectionReportService.cs` (modified)
- `SiNetSQL\Services\InspectionSync\InspectionReportService.cs` (modified)
- `Services\Migration\ReportImportService.cs` (modified)

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
- [`Docs\Domains\Authorization\AuthorizationPrinciples-2026-06-18.md`](../Domains/Authorization/AuthorizationPrinciples-2026-06-18.md)
