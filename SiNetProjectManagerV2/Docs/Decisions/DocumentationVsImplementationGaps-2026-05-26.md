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
