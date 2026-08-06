# Email System — Approved Principles

> **Decision date:** 26.05.2026
> **Status:** Approved principles — pending code alignment.
> **Domain:** Email, Gmail, ACC Inbox, attachments, WebView2.
> **Scope:** This document is the source of truth for how the application
> identifies emails, opens them in Gmail / WebView2, classifies attachments,
> and stores them in the ACC Inbox. Older specs and PDFs under `Docs\work\`
> that conflict with this document are considered out of date; they will be
> marked `SUPERSEDED` in a separate approved round and are **not** modified
> or deleted here.

---

## 1. Purpose

Define the approved business and technical principles for the email domain so
that all future code changes (identity, ingestion, display, opening from a
task, attachment classification, ACC Inbox layout, deduplication) follow a
single, consistent rulebook.

This document does **not** introduce any code change. It captures the rules
agreed in the rounds leading up to 26.05.2026. Code alignment will happen in
follow-up approved rounds.

## 2. Email identity (business identity)

### 2.1 The business identifier of an email is the RFC822 `Message-ID`

- The business identifier (`MessageUniqueId` semantics) of an email in the
  system is the **RFC822 `Message-ID` header** of the message itself, i.e. the
  `Message-ID` value taken from the email's MIME headers (`payload.headers`).
- This identifier is **global**: the same message received by multiple users
  carries the same RFC822 `Message-ID`. It is therefore the only correct basis
  for cross-user deduplication, ACC Inbox folder identity and persistent links
  from tasks back to the originating email.
- The existing helper `MessageKeyGenerator.GetMessageUniqueId(internetMessageId, gmailMessageId)`
  already prefers RFC822 `Message-ID` when supplied and falls back to
  `gmail:{gmailMessageId}` otherwise. The intent is that production paths
  supply the RFC822 value; the fallback exists only for compatibility.

### 2.2 Gmail API `message.id` is a local, per-mailbox identifier

- `Gmail API message.id` (also surfaced as `EmailInfo.MessageId`) is **local to
  the current user's mailbox**. The same physical message received by a
  different user gets a **different** `message.id`.
- `Gmail API message.id` **must not** be used as a global business identifier.

### 2.3 Permitted uses of `Gmail API message.id`

`Gmail API message.id` is allowed only for **local** operations bound to the
current user's mailbox:

- Reading the message via Gmail API for the current user.
- Downloading attachments via Gmail API for the current user.
- Opening the message locally in the embedded Gmail WebView2.
- Locating a message that was already loaded from the current user's account
  (in-memory lookup).

### 2.4 Forbidden uses of `Gmail API message.id`

`Gmail API message.id` **must not** be used as the basis for:

- `MessageUniqueId`.
- `MessageKey`.
- ACC Inbox folder identity (`MSG_<key>` naming).
- Cross-user deduplication.
- A persistent link from a task or workflow back to the originating email.
- `AlreadyProcessed` semantics at the system level.

### 2.5 Gmail `message.id` and Gmail `threadId` are NOT persisted as business data (added 26.05.2026)

- The database stores **only global / business / system data**. It must not
  persist `Gmail message.id` or `Gmail threadId` as a source of truth or as a
  permanent identifier. Both values are **mailbox-local**: the same message and
  the same thread can have different values in different users' mailboxes.
- What the DB **does** store for an email: RFC822 `Message-ID`, `In-Reply-To`,
  `References`, business data, links to project / task / file, user decisions,
  workflow state, and links to ACC / `ProjectFile` / `ProjectAlternative` /
  `ProjectFileInstance` as needed.
- When local Gmail identifiers are required, they are **resolved on demand**
  against the current user's mailbox using `rfc822msgid:{Message-ID}`. The
  returned `message.id` / `threadId` are used only for the current local
  operation (open in WebView2, read via Gmail API, download attachments,
  display thread). They are **not** written back to the DB as persistent
  business data.
- If the message is not found in the current user's mailbox: show a message to
  the user. **Do not** create a new email record, a new ACC Inbox, or use
  another user's identifiers, and do not perform a silent fallback.

> Principle: *Gmail message.id and Gmail threadId are mailbox-local runtime
> identifiers. They are not persisted as business data in the database. The
> database stores only global email headers and business data, such as RFC822
> Message-ID, In-Reply-To, and References. When local Gmail identifiers are
> needed, they are resolved on demand against the current user's Gmail mailbox
> and used only for the current operation.*

### 2.6 RFC822 threading headers — `In-Reply-To` and `References` (added 26.05.2026)

- The global thread relationships of an email are defined by the RFC822
  headers `In-Reply-To` and `References` (in addition to `Message-ID`).
- These headers are global and stable across mailboxes, and they are the
  authoritative basis for computing a **global thread identity** (see §6.3).
- `Gmail threadId` is only a mailbox-local convenience for grouping the
  current user's view; it is **not** a business thread identifier.

## 3. Opening an email from a task

When the user opens a task that references an email, the email's persistent
identifier in the DB is the **RFC822 `Message-ID`**. The procedure to act on
the email in the current user's mailbox is:

1. Search the current user's Gmail using the query:

   ```
   rfc822msgid:{Message-ID}
   ```

2. If a match is found: take the resulting **local** `Gmail API message.id`
   and use it for any local action (open in WebView2, fetch via Gmail API,
   download attachments).
3. If no match is found: show a message to the user that the email was not
   found in their Gmail mailbox. **Do not** create a new email record, a new
   task, or a new ACC folder as a side effect of "not found".

## 4. Gmail WebView2 opening URLs

### 4.1 Local open

To open a message in the embedded Gmail WebView2 for the **current user's**
mailbox, use the local Gmail API `message.id`:

```
https://mail.google.com/mail/u/0/#all/{GmailMessageId}
```

This URL is only valid when `GmailMessageId` is the local `message.id` of the
mailbox currently loaded in the WebView2.

### 4.2 Cross-mailbox search

To open Gmail focused on a message identified by its **global** RFC822
`Message-ID`, use the search URL with URL-encoded value:

```
https://mail.google.com/mail/u/0/#search/rfc822msgid:{EncodedMessageId}
```

### 4.3 What is forbidden

- **Do not** put an RFC822 `Message-ID` value into the `#all/{id}` URL.
- **Do not** put a Gmail API `message.id` of one mailbox into the WebView2 of
  another mailbox.
- **Do not** use the legacy `#inbox/{threadId}` form as the default open URL
  for the selected email. (It remains available as a logged reference only.)

### 4.4 WebView2 display enhancements (DOM)

- WebView2 may use `ExecuteScriptAsync` to improve display, for example to
  scroll to or expand a specific message inside a Gmail thread.
- A future investigation may use attributes such as `data-legacy-message-id`
  on Gmail DOM elements to focus on a specific message.
- **The Gmail DOM is not an official API.** Selectors, classes and attributes
  may change without notice. If a DOM-based open or focus mechanism stops
  working, the most likely cause is a Gmail DOM change. DOM-derived data is
  allowed only for display improvements, never as a business source of truth.

## 5. Attachments — classification rules

### 5.1 A business attachment is not "any MIME part with a filename"

- The existence of `body.attachmentId` or a `filename` on a MIME part is
  **not sufficient** to classify the part as a real attachment.
- The classification must distinguish, at minimum, between:
  - A real attachment.
  - An inline image (embedded in the HTML body via CID).
  - A signature image, logo or tracking pixel.
  - A hidden MIME part.
  - An external download link (e.g. JumboMail, WeTransfer).
  - An image that the user explicitly chose to promote to an attachment.

### 5.2 Inline images are not attachments by default

- Inline images are **not** business attachments by default and **must not**
  be uploaded to ACC by default.
- The user must explicitly opt in for an inline image to become a real
  attachment.

### 5.3 External downloads

- Files originating from external download links (JumboMail, WeTransfer, etc.)
  are tracked separately from native Gmail attachments. They are part of the
  display list (`AllDisplayableAttachments`) but not part of the native MIME
  attachment set.

### 5.5 Archive attachments (ZIP / RAR) — target policy (added 2026-07-31)

> Implementation status: Legacy + Jumbo extract ZIP today; Native Gmail ingest
> does not yet (N5 proposed — `docs/NATIVE_EMAIL_ACC_INGEST.md`).

- Archive attachments (`.zip`, and `.rar` when supported) are **extracted** into
  an ACC Inbox subfolder under `Attachments/{archiveName}/` so contents can be
  viewed.
- **Tagging and Move treat the archive as one work unit** (one taggable inbox
  attachment row for the archive), not one tag per extracted file.
- Extracted children uploaded for viewing/filing are limited to business types:
  **DWG, PDF, and common image formats**. Fonts and other non-business types are
  not uploaded as filing candidates.
- RAR support is optional until an approved extraction library is selected.

### 5.4 Gmail DOM is not a source of truth for attachments

- The Gmail DOM **must not** be used to identify attachments or to validate
  the attachment list.
- The diagnostic class `GmailVisibleAttachmentsDomExtractor` is currently
  **DISABLED / diagnostic disabled / not used currently / candidate for
  future removal**. It must not be re-enabled without an explicit approved
  round.

## 6. ACC Inbox

### 6.1 ACC is the source of truth for uploaded files

- ACC is the authoritative source for files that have already been uploaded.
- A DB record with a stored `AccItemId` is **not** sufficient evidence that
  the file is still present in ACC. If the `AccItemId` no longer resolves in
  ACC, the file must be treated as `MissingInAcc` / `StaleAccReference`, not
  as `Uploaded`.
- Reconciliation against ACC is required to confirm physical presence; the
  DB cache must not contradict ACC.
- This physical-existence rule is **independent** of mailbox project
  association (§6.6). A SQL `ProjectId` on an inbox row does **not** prove
  either that the email is Gmail-filed or that attachments still exist in ACC.

### 6.2 ACC Inbox layout — current and target

- The ACC Inbox layout is centralised in `AccInboxLayout`.
- Message folders are named `MSG_<MessageKey>`, where `MessageKey` is derived
  by `MessageKeyGenerator.GetMessageKey(MessageUniqueId)` — and
  `MessageUniqueId` itself is derived from the RFC822 `Message-ID` (see §2).
- The per-message folder contains:
  - `00_Email.pdf` — the rendered email body.
  - `manifest.json` — the sidecar manifest for the message folder.
  - `Attachments\` — child folder for the message's attachments.
- **When a message belongs in ACC Inbox** (eligibility — Native N4.3, 2026-07-31):
  - **Has business Gmail attachments** → ingest to ACC Inbox (passive select /
    explicit upload).
  - **No attachments** → ingest **only after mailbox project association**
    (Gmail label filed — §6.6). Browsing an unfiled, attachment-less email
    must **not** create ACC folders or upload `00_Email.pdf`.
  - Layout when ingested: `00_Email.pdf` (best-effort) + `manifest.json` +
    `Attachments\` (may be empty). See `docs/NATIVE_EMAIL_ACC_INGEST.md` N4.3.
- **No re-upload when ACC already has the file:** if DB cache has a valid
  `AccItemId` for that attachment / body PDF (and short-circuit / status says
  present), do **not** print or upload again. Re-upload only when ACC presence
  is missing / stale (`MissingInAcc` / no `AccItemId`). Do not force “refresh”
  uploads on every select.
- Sidecar JSON files (e.g. `<file>.<ext>.json`) are **metadata sidecars**, not
  competing versions. They must not be reported as extension conflicts against
  the main file. (Implementation of this rule is out of scope for this
  document; it is referenced here only for completeness.)

### 6.3 ACC Inbox active layout — `_Inbox/THREAD_<ThreadKey>/MSG_<MessageKey>/` (active 26.05.2026)

The ACC Inbox structure is organised by **global email thread
identity**, not by Gmail mailbox-local `threadId`:

```
_Inbox\
  THREAD_<ThreadKey>\
    MSG_<MessageKey>\
      00_Email.pdf
      manifest.json
      Attachments\
```

- All messages that belong to the same business email thread are stored under
  the same `THREAD_<ThreadKey>` folder.
- Inside the thread folder, each message has its own `MSG_<MessageKey>`
  folder. The per-message layout (`00_Email.pdf`, `manifest.json`,
  `Attachments\`) is unchanged.
- `THREAD_<ThreadKey>` is **not** derived from Gmail `threadId`.
- `MSG_<MessageKey>` is **not** derived from Gmail `message.id`.
- Both keys are derived from **global** RFC822 identifiers.

**ThreadKey derivation (preferred order):**

1. If the message has a non-empty `References` header, the **first**
   `Message-ID` in `References` is the thread root, and `ThreadKey` is
   derived from that root `Message-ID`.
2. Else if the message has an `In-Reply-To` header, it points to the previous
   message / thread-root candidate and is used to derive `ThreadKey`.
3. Else (no `References`, no `In-Reply-To`) the message itself is the thread
   root, and `ThreadKey` is derived from its own `Message-ID`.

`MessageKey` is derived from the message's own RFC822 `Message-ID`.

This layout is the **active implementation** as of the 2026-05-26 round.
The previous flat `MSG_<MessageKey>\…` and `Year/Month\…` layouts are
**superseded**; no silent fallback to them is allowed. Legacy ACC
folders are not migrated automatically — see
[`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md)
(Gap 3 + *Cleanup / postponed items*).

> Principle: *ACC Inbox is organised by global email thread identity, not
> Gmail mailbox-local threadId. The desired structure is
> `THREAD_<ThreadKey>\MSG_<MessageKey>\`, where ThreadKey is derived from
> RFC822 threading headers (References, In-Reply-To, Message-ID) and
> MessageKey is derived from RFC822 Message-ID. The message folder keeps the
> existing 00_Email.pdf, manifest.json, and Attachments\ layout.*

### 6.4 Folder naming and identifiers — global only (added 26.05.2026)

- ACC Inbox folder names and DB-persisted message identifiers must be based on
  **global business identifiers**, never on mailbox-local Gmail identifiers.
- Any existing fallback to `gmail:{message.id}` for `MessageUniqueId` /
  `MessageKey` is treated as **legacy / compatibility only / implementation
  gap**, not as a desired principle.

### 6.5 Metadata source of truth (added 26.05.2026)

Each kind of metadata has one defined owner:

- **Gmail / RFC822 headers** — source of truth for email identity and global
  thread relationships (`Message-ID`, `In-Reply-To`, `References`).
- **Gmail project labels** — source of truth for **mailbox project
  association** (whether the message is filed to a project in the mailbox);
  see §6.6.
- **DB** — source of truth for business process: projects, tasks, workflow,
  `ProjectFile` / `ProjectAlternative` / `ProjectFileInstance`, user
  decisions, and the links between email, file, project, and task. The DB is
  **not** the source of truth for mailbox filing (§6.6) or physical ACC
  presence (§6.1).
- **Storage Destination** — source of truth for the physical existence of a
  file (see `ProjectFilesPrinciples`).
- **ACC custom attributes** — source of truth for metadata that must travel
  with the ACC item itself (e.g. originating email, `Message-ID` /
  `MessageKey` reference, Inbox source, `ProjectFile` reference written on
  the item, move-target reference). ACC attributes do **not** replace the DB
  as the business source of truth.
- **`manifest.json` / sidecar JSON** — audit snapshot of what happened at
  ingestion / upload / Inbox creation time. It does **not** replace the DB
  as business truth, ACC / Storage Destination as physical-existence proof,
  or Gmail `Message-ID` as email identity.
- **UI / DOM** — not a source of truth. Display and user actions only.

Forbidden:

- Using the DB alone as proof that a file exists physically.
- Using the DB alone (`EmailInboxMessage.ProjectId`, thread mapping,
  workflow association) as proof that a message is **mailbox-filed** to a
  project — that requires a Gmail project label (§6.6).
- Using ACC attributes as the source of truth for general workflow.
- Using `manifest.json` instead of the DB, or instead of a Storage Destination
  existence check.
- Using UI / DOM as a source of truth.
- Updating metadata in multiple places without defining which is the source
  and which is the copy.

### 6.6 Mailbox project association — Gmail labels are the source of truth (added 2026-07-20)

**Concern:** “Is this email filed / tagged to a project in the operator’s
mailbox?” (`IsFiledToProject`, File / Unfile / Move eligibility gate).

This is **not** the same concern as:

- physical presence of an attachment in ACC (§6.1), or
- ACC custom attributes for tag / move / lock on an Inbox item (§6.5), or
- business `ProjectId` links used by workflow / tasks (DB).

#### Source of truth

- The **Gmail project label** under root `פרויקטים_משרד`
  (`EmailGmailLabelNames.RootLabel` / `IsProjectLabel`: at least
  `root/location/project…`) is the **sole source of truth** for mailbox
  project association.
- UI and services expose this as `IsFiledToProject` (mapped from Gmail
  `labelNames` in `EmailListRowMapper`). Move eligibility must pass that
  Gmail-derived flag — not a SQL inference.

#### Database role

- SQL (`EmailInboxMessage.ProjectId`, thread mappings, etc.) is a
  **best-effort mirror / helper** after a successful Gmail label attach
  (`SqlEmailFilingService`: Gmail first, then SQL sync).
- If SQL sync fails after the label was attached, compensation **removes the
  Gmail label** so mailbox truth and DB do not diverge permanently.
- A non-null SQL `ProjectId` (including after `CreatePriceQuote` /
  `OpenQuoteProject` materialization) **does not** mean the message is
  mailbox-filed.

#### Allowed Gmail writes (carve-out)

- Gmail remains **not** a Storage Destination and **not** a place to store
  project file content.
- **Allowed:** Gmail label modify for (1) project filing / unfiling and
  (2) triage status labels (`OfficeSystem_*`). That write path **is** the
  mailbox association source of truth.
- **Forbidden:** treating “do not write back to Gmail” as a ban on those
  label operations, or inventing a SQL-only “effectively filed” shortcut
  (e.g. `ProjectId == currentProject`) to bypass the label.

#### Two-stage handling and Gmail read state

Mailbox triage is **two stages**:

1. **Classify:** file to a project label, or mark Personal / Irrelevant
   (`OfficeSystem_*`). Personal and Irrelevant also remove system `UNREAD`
   (handling finished). Filing alone does **not** clear `UNREAD`.
2. **Act on filed mail:** «לידיעה בלבד» (`OfficeSystem_Fyi`) or a successful
   real Workflow removes `UNREAD`. `FileOnly` / file / move alone do not.

Selecting or opening a message in the app must **not** remove `UNREAD`.
Detail: [`docs/DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md`](../../../../docs/DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md).

#### Forbidden fixes / regressions

- Inferring “משויך לפרויקט” from SQL `ProjectId`, `IsAssociatedToProject`,
  or workflow context alone.
- Auto-moving / completing FileQuoteMaterial without a successful Gmail
  project-label File.
- Reintroducing helpers named like `IsEffectivelyFiled*` that OR SQL state
  into `IsFiledToProject`.

Operational detail for New System list filing:
[`docs/EMAIL_LIST_MIGRATION.md`](../../../../docs/EMAIL_LIST_MIGRATION.md)
and the agent-facing summary
[`docs/EMAIL_ACC_SOURCE_OF_TRUTH.md`](../../../../docs/EMAIL_ACC_SOURCE_OF_TRUTH.md).

## 7. Things we deliberately do NOT do right now

- Do **not** change the production value of `MessageUniqueId` for existing rows.
- Do **not** change `MessageKeyGenerator` behaviour.
- Do **not** introduce a parallel identity field on `EmailInfo` or any schema
  change in this round.
- Do **not** re-enable `GmailVisibleAttachmentsDomExtractor`.
- Do **not** change upload, recovery, `AlreadyProcessed`, reconciliation,
  `MoveToProject`, PDF rendering or attachment classification in this round.
- Do **not** introduce a new fallback URL form or a parallel URL builder; the
  single approved property is `EmailInfo.GmailPopoutUrl`.
- Do **not** use Gmail as a Storage Destination or write email/file **content**
  back to Gmail. **Allowed carve-out (see §6.6):** Gmail label modify for
  project filing / unfiling and triage status labels — that label write **is**
  the mailbox association source of truth.
- Do **not** persist mailbox-local Gmail identifiers (`message.id` /
  `threadId`) as business data (see §2.5).
- Do **not** put business decisions (identity, workflow, filing, Storage
  Destination, PlanReview, AI) inside `SiOffice.GoogleConnector` /
  `GoogleService`; the connector / service layer provides API operations
  only (see §11).

## 8. Things that were dropped / cancelled / postponed

- Persisting `Gmail message.id` in the DB as a business identifier — **dropped**.
- Persisting `Gmail threadId` in the DB as a business identifier — **dropped**.
- ACC Inbox folder names derived from Gmail mailbox-local IDs — **dropped**.
- A flat `MSG_<MessageKey>\...` ACC Inbox layout as the **final** target —
  dropped in principle and replaced by `THREAD_<ThreadKey>\MSG_<MessageKey>\`;
  any actual implementation gap is recorded in
  [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md).
- Gmail as a write/management **Storage Destination** — **dropped**; Gmail is
  not a place to store project file content (see `ProjectFilesPrinciples`).
  Label modify for mailbox filing / triage is **allowed** and is SoT for
  association (§6.6) — that is not “Gmail as Storage Destination”.
- Metadata without a defined source of truth — **dropped** (see §6.5 / §6.6).
- Promoting RFC822 `Message-ID` to a first-class, persisted field on
  `EmailInfo` is **postponed** to a separate approved round (requires
  exposing it via `GoogleService.MapMessageToInfo` / `MapMessageToInfoMetadataOnly`
  from `payload.headers`).
- The on-demand `rfc822msgid:{Message-ID}` **resolution to a local Gmail
  `message.id`** (the §3 open-from-task procedure) is **implemented** as of
  the 2026-05-26 round: `GoogleService.ResolveLocalMessageIdByRfc822Async`
  performs the lookup and `EmailManagementViewModel.EnsureTaskEmailLoadedAsync`
  uses it to build the synthesized email's `MessageId` (and otherwise shows a
  "not found in this mailbox" message). See Gap 20.
- A `#search/rfc822msgid:{...}` open URL *inside the WebView2 itself* (§4.2)
  remains **postponed**; the implemented path resolves to a local
  `message.id` and uses the existing `#all/{message.id}` form instead.
- DOM-based "expand the specific message in a thread" via
  `data-legacy-message-id` is **postponed**; only documented here as a
  candidate, not implemented.
- The Gmail DOM extractor approach for attachment detection is **cancelled**;
  the class is kept only to avoid breaking the DI graph and call sites, and
  is a candidate for future removal.
- Reorganising `Docs\` (moving existing files into the new structure, marking
  older specs as `SUPERSEDED`, creating `Docs\Archive\`) is **postponed** to a
  separate approved round.
- Mixing Gmail and Google Drive as the same Storage Destination —
  **dropped**.
- Using Gmail as a Storage Destination / writing file content back to Gmail —
  **dropped**. Label-only filing/triage writes remain allowed (§6.6).
- Persisting Gmail mailbox-local `message.id` / `threadId` in the DB as
  business identifiers — **dropped**.
- Adding a Google Drive upload mechanism for email attachments without an
  explicit decision — **not approved**.
- Google Drive fallback when the configured Storage Destination is missing
  — **not approved**.
- Using `SiOffice.GoogleConnector` / `GoogleService` as a general business
  engine for email identity / workflow / filing / PlanReview / AI —
  **dropped**.
- Google Sheets as a general business source of truth for the email
  domain — **not approved**.

## 9. Relevant files / classes / services

- `SiNetSQL\Services\EmailIngestion\MessageKeyGenerator.cs`
  — `GetMessageUniqueId`, `GetMessageKey`, `SanitizeFileName`.
- `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`
  — current ingestion path; today does not pass an RFC822 `Message-ID`.
- `SiNetSQL\Services\EmailIngestion\IEmailIngestionService.cs`
- `SiOffice.GoogleConnector\GoogleService.cs`
  — `EmailInfo`, `EmailInfo.MessageId`, `EmailInfo.ThreadId`,
  `EmailInfo.GmailPopoutUrl`, `MapMessageToInfo`, `MapMessageToInfoMetadataOnly`,
  `EmailAttachment.IsInline`, `AllDisplayableAttachments`.
- `SiNetSQL\Models\EmailInboxMessage.cs`, `EmailInboxAttachment.cs`
  — DB models for inbox messages / attachments.
- `SiNetProjectManagerV2\WPFUserControl\WebView2Helper.cs`
  — embedded Gmail/ACC WebView2 host, `NavigateToUrlSafely`.
- `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml.cs`
  — selection → open in WebView; `[GmailOpenUrl]` diagnostic log.
- `SiNetSQL\MVVM\Components\EmailViewerViewModel.cs`
  — bridges `EmailInfo` to the viewer control.
- `SiNetProjectManagerV2\Services\GmailVisibleAttachmentsDomExtractor.cs`
  — **DISABLED**, candidate for future removal.
- ACC Inbox layout: `AccInboxLayout` (centralised constants), reconciliation
  services and `MoveToProject` handlers (out of scope for this document).

## 10. Search terms

`MessageUniqueId`, `MessageKey`, `MessageKeyGenerator`, `RFC822 Message-ID`,
`InternetMessageId`, `rfc822msgid`, `Gmail message.id`, `Gmail messageId`,
`Gmail ThreadId`, `threadId`, `Gmail WebView2`, `ExecuteScriptAsync`,
`data-legacy-message-id`, `GmailPopoutUrl`, `#all/{messageId}`,
`#inbox/{threadId}`, `#search/rfc822msgid`, `ACC Inbox`, `AccInboxLayout`,
`MSG_ folder`, `Email attachments`, `UploadableAttachments`,
`AllDisplayableAttachments`, `AlreadyProcessed`, `MissingInAcc`,
`StaleAccReference`, `JumboMail`, `WeTransfer`, `External downloads`,
`Inline images`, `Hidden MIME part`, `EmailAttachment.IsInline`,
`GmailVisibleAttachmentsDomExtractor`, `SiOffice.GoogleConnector`,
`GoogleService`, `Google Drive`, `Google Sheets`, `Google service boundary`.

## 11. Google service boundary (added 26.05.2026)

This section is the email-domain companion to the Google service boundary
documented in
[`Domains\Architecture\ServiceCatalog-2026-05-26.md`](../Architecture/ServiceCatalog-2026-05-26.md)
and
[`Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](../Architecture/ArchitecturePrinciples-2026-05-26.md).
It clarifies what `SiOffice.GoogleConnector` / `GoogleService` does **for
the email domain** and what it does not.

### 11.1 Role

- `SiOffice.GoogleConnector` / `GoogleService` is the **connector / service
  layer** for Google API access: Gmail API, Google Drive API, Google Sheets
  API, and OAuth / token handling per the existing structure.
- For email, it provides: reading messages and headers, downloading
  attachments, resolving `rfc822msgid:{Message-ID}` against the current
  user's mailbox, surfacing thread membership.

### 11.2 What the connector / service does NOT do for email

- It does **not** decide the business identity of an email beyond returning
  RFC822 headers.
- It does **not** decide `MessageUniqueId` / `MessageKey` / `ThreadKey` by
  itself — those derivations live in domain services
  (`MessageKeyGenerator` and ingestion / identity services).
- It does **not** decide workflow or tasks.
- It does **not** file files into a project.
- It does **not** decide Storage Destination.
- It does **not** create `ProjectAlternative` rows by itself; if alternative
  creation is needed it goes through the ProjectFiles services per
  [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../ProjectFiles/ProjectFilesPrinciples-2026-05-26.md).
- It does **not** decide whether AI is invoked.
- It is **not** a general business source of truth.

### 11.3 Gmail / Drive / Sheets separation

- **Gmail** — primary ingestion source for emails and RFC822 headers.
  Gmail is **not** a write Storage Destination for file content. **Allowed:**
  label modify for project filing / unfiling and triage (`OfficeSystem_*`);
  those labels are the source of truth for mailbox project association
  (§6.6).
- **Google Drive** — separate from Gmail. Drive may be a Storage
  Destination for project files (see `ProjectFilesPrinciples`), but Drive
  **upload is postponed** and Drive is **not** an email destination.
- **Google Sheets** — reporting / template integration only. Sheets is
  **not** a business source of truth for email identity, ingestion, or
  filing.

### 11.4 Forbidden across the Google boundary (email view)

- Mixing Gmail and Google Drive as the same Storage Destination.
- Persisting Gmail mailbox-local identifiers (`message.id`, `threadId`) as
  business data in the DB.
- Writing email/file **content** back to Gmail, or using Gmail as a project
  file Storage Destination. (Label filing/triage writes are allowed — §6.6.)
- Inferring mailbox “filed to project” from SQL alone (bypass of Gmail
  project labels).
- Using `SiOffice.GoogleConnector` / `GoogleService` to bypass
  `ProjectFiles` / `Workflow` / Storage Destination rules.
- Using Google Sheets as a general business source of truth without an
  explicit decision in the relevant domain.
- Adding a Google Drive "fallback" for email attachments or project files
  when the configured Storage Destination is missing.



# Task-Driven Email Filing

This document describes how the **EmailFiling** family of tasks (`TaskTypeCodes.FileInitialMaterials`, `FileCorrectedMaterials`, `TrackMissingMaterial`, etc.) integrates with the email filing UI and the central task completion pipeline. It is maintained as a single, cohesive explanation of the current system design, with inline annotations showing where and when specific design decisions were modified.

## 1. UI host

When a task with `ComponentKey = TaskComponentKeys.EmailFiling` is opened from `FloatingProjectTasksView`, the host navigates the user to **`EmailManagementView`** (the same canonical UI used by the inbox tagging flow). The legacy `EmailPreviewWindow` is **not** used for filing — it is a read-only preview reserved for the `NewProjectDialog` follow-up only.

The picker / duplicate-validation / placement-lock rules of the email tagging UI therefore apply uniformly to both:

- Inbox-originated MoveToProject.
- Task-originated MoveToProject.

## 2. Granularity: TaskLink is per email, completion is per attachment-set

`TaskLink.LinkedEntityType = EmailInboxMessage` for every EmailFiling task work-target. There is **no** per-attachment TaskLink.

A `TaskLink` is marked **Done** only when the email it points to has reached the state:

> Every relevant attachment of the email has `EmailInboxAttachment.ProjectFileInstanceId != null`.

"Relevant" starts from the eligibility filter used by `MoveToProjectAsync`: `ProjectFileId != null && AccItemId != null`. The `AccItemId` stored in SQL is cache/helper state only; MoveToProject still verifies the physical source item in ACC before filing. Regular attachments are resolved from the centralized ACC Inbox layout under `MSG_{messageKey}/Attachments`, while `00_Email.pdf` is resolved from the message folder.

## 3. Partial filing does not close the task

If a `MoveToProjectAsync` run files only part of the relevant attachments of an email (e.g. the user retried after a previous failure), the email's `TaskLink` stays open. The task stays in progress, and the user can return to finish filing on a later run.

Closure of the task as a whole is decided exclusively by `TaskCompletionCoordinator.EvaluateClosureAsync`, which closes the task only when **all** `IsWorkTarget` links are `Done` or `Skipped`.

## 4. Completion path

UI components MUST NOT close tasks, set `ProjectStatus`, or advance the workflow directly. The single completion entry point is **`ITaskCompletionCoordinator.CompleteAsync`**.

The flow on a successful `EmailManagementViewModel.MoveToProjectAsync` run:

```
EmailManagementViewModel.MoveToProjectAsync
  └─ FileAsync (per attachment) → ProjectFileInstanceId persisted
       └─ if ActiveTaskContext != null
            └─ ReportTaskCompletionAsync
                 └─ resolve filed attachments → MessageId set
                 └─ per-email gate: all relevant attachments filed?
                 └─ map qualifying MessageIds → TaskLink ids on the task
                 └─ ITaskCompletionCoordinator.CompleteAsync(
                        taskId,
                        completionEventCode: ReviewCompletionEvents.ReviewMaterialFiled,
                        taskResultCode: null,
                        completedTaskLinkIds: resolvedTaskLinkIds,
                        payload: null,
                        userId,
                        ct)
```

Behaviour rules:

- `Success = false` returned by the coordinator: logged with `AppLogger.Warn`; **MoveToProject does not fail**.
- Any exception inside `ReportTaskCompletionAsync`: caught and logged with `AppLogger.LogError`; **MoveToProject does not fail**.
- `TaskClosed = true`: the floating task list is refreshed via `EmailFilingTaskContext.OnTaskRefreshRequested` (dispatched to the UI thread).
- Inbox-originated navigation: `ActiveTaskContext` is null, the coordinator is **not** called, behaviour is identical to the historical inbox path.

## 5. Runtime context

`SiNetSQL.Services.Tasks.EmailFilingTaskContext` is a runtime-only record (no DB, no migration) carried from `FloatingProjectTasksView` into `EmailManagementViewModel.ActiveTaskContext` via the new `MainWindow.NavigateToEmail(int, EmailFilingTaskContext?)` overload.

Fields:

| Field | Source | Purpose |
|---|---|---|
| `TaskId` | `TaskNavigationRequest.TaskId` | Coordinator call target. |
| `ComponentKey` | `TaskNavigationRequest.ComponentKey` | Diagnostics only. |
| `WorkTargetEmailIds` | `TaskNavigationRequest.WorkTargetIds` (cast to `int`) | Limits completion to emails the task actually owns. |
| `PendingWorkTargetEmailIds` | `TaskNavigationRequest.PendingWorkTargetIds` | Informational only. |
| `PrimaryWorkTargetEmailId` | `TaskNavigationRequest.PrimaryWorkTargetEntityId` | Convenience for single-target tasks. |
| `OnTaskRefreshRequested` | UI callback | Refreshes the floating task list after closure. |

## 6. Non-goals / what is intentionally NOT done

- No new completion service is introduced — `ITaskCompletionCoordinator` is the single source of truth.
- No DB schema changes; no EF Core migrations.
- No `WorkflowDesigner` changes.
- No manual task closure from the UI.
- No task advancement on "email opened" or "attachment tagged" — only on durable filing (`ProjectFileInstanceId != null`).
- `ProjectAlternative` is project-scoped; alternatives are **not** filtered by `JobType` / `TypeOfProjectInProjectId`. The pre-2026 model that associated alternatives with `TypeOfProjectInProject` is retired in code; remaining references survive only in historical EF migrations.
- Auto-sync to the final project after tagging is **disabled by design** (`EmailManagementViewModel.EnableAutoSyncToProject = false`). Tagging is metadata-only; `MoveToProject` is the sole transfer path.
- ACC is the source of truth for Inbox file existence. DB-only identifiers must not be used to open or move Inbox files without ACC reconciliation/layout-aware verification.
- Mailbox “filed to project” is a **separate** concern: Gmail project labels are the source of truth (`EmailSystemPrinciples` §6.6). Do not treat SQL `ProjectId` as proof the message is filed in the mailbox.

## 7. Files

| File | Role |
|---|---|
| `SiNetSQL/Services/Tasks/EmailFilingTaskContext.cs` | Runtime context record. |
| `SiNetSQL/Services/Tasks/ITaskCompletionCoordinator.cs` | Central completion API. |
| `SiNetSQL/Services/Tasks/TaskCompletionCoordinator.cs` | Coordinator implementation; closure policy. |
| `SiNetSQL/Services/Tasks/ReviewCompletionEvents.cs` | `ReviewMaterialFiled` event constant. |
| `SiNetSQL/Services/Tasks/ReviewCompletionEventBehavior.cs` | Event-to-task-type mapping. |
| `SiNetSQL/MVVM/EmailManagementViewModel.cs` | `ActiveTaskContext`, `MoveToProjectAsync`, `ReportTaskCompletionAsync`. |
| `SiNetProjectManagerV2/App.xaml.cs` | DI registration of `ITaskCompletionCoordinator`. |
| `SiNetProjectManagerV2/MainWindow.xaml.cs` | Task-aware `NavigateToEmail` overload. |
| `SiNetProjectManagerV2/WPFUserControl/FloatingProjectTasksView.xaml.cs` | Builds `EmailFilingTaskContext` from `TaskNavigationRequest`. |

## 8. Physical Filing and Project Association
[Modified 2026-06-15 — Immediate Child Project creation]
Previously, planner-originated requests used a "Virtual Association" to link emails to the Parent Project via `TaskLink` without physical download, because child projects were not created until a municipality invitation was received.
This approach has been deprecated and removed. Child project creation (`REV.ProjectSetup`) is now **always the first stage** of the workflow, meaning the target child project folder structures in ACC are immediately available regardless of the request source. Emails and files are always physically filed directly into the child project without requiring virtual association or deferred filing.

