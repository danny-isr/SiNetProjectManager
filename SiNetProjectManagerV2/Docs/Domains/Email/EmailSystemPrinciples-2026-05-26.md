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

### 6.2 ACC Inbox layout

- The ACC Inbox layout is centralised in `AccInboxLayout`.
- Message folders are named `MSG_<MessageKey>`, where `MessageKey` is derived
  by `MessageKeyGenerator.GetMessageKey(MessageUniqueId)` — and
  `MessageUniqueId` itself is derived from the RFC822 `Message-ID` (see §2).
- The per-message folder contains:
  - `00_Email.pdf` — the rendered email body.
  - `manifest.json` — the sidecar manifest for the message folder.
  - `Attachments\` — child folder for the message's attachments.
- Sidecar JSON files (e.g. `<file>.<ext>.json`) are **metadata sidecars**, not
  competing versions. They must not be reported as extension conflicts against
  the main file. (Implementation of this rule is out of scope for this
  document; it is referenced here only for completeness.)

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

## 8. Things that were dropped / cancelled / postponed

- Promoting RFC822 `Message-ID` to a first-class, persisted field on
  `EmailInfo` is **postponed** to a separate approved round (requires
  exposing it via `GoogleService.MapMessageToInfo` / `MapMessageToInfoMetadataOnly`
  from `payload.headers`).
- A `#search/rfc822msgid:{...}` open path in the WebView2 is **postponed**
  until §2's identity work is in place.
- DOM-based "expand the specific message in a thread" via
  `data-legacy-message-id` is **postponed**; only documented here as a
  candidate, not implemented.
- The Gmail DOM extractor approach for attachment detection is **cancelled**;
  the class is kept only to avoid breaking the DI graph and call sites, and
  is a candidate for future removal.
- Reorganising `Docs\` (moving existing files into the new structure, marking
  older specs as `SUPERSEDED`, creating `Docs\Archive\`) is **postponed** to a
  separate approved round.

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
`GmailVisibleAttachmentsDomExtractor`.
