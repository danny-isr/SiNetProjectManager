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
   - Message folders: `MSG_<MessageKey>`
   - Message folder files: `00_Email.pdf`, `manifest.json`
   - Regular attachments under the `Attachments` child folder
   - **Target layout (added 26.05.2026):** `THREAD_<ThreadKey>\MSG_<MessageKey>\…`,
     organised by global email thread identity. `ThreadKey` is derived from
     RFC822 threading headers (`References`, `In-Reply-To`, `Message-ID`) and
     `MessageKey` is derived from RFC822 `Message-ID`. **Folder names must
     not be derived from Gmail mailbox-local `message.id` or `threadId`.** If
     the current implementation is still message-only, no code change is made
     in this round — the gap is logged in
     [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md).
5. A sidecar `.json` (e.g. `<file>.pdf.json`) is **not** an extension conflict and must not be treated as such.
6. Custom attribute definitions (`SiInbox.*`) are provisioned only in the dedicated ACC Inbox project / folder via the existing `Bim360Service.EnsureCustomAttributeDefinitionsAsync`. Definitions are not auto-created from `SetItemCustomAttributesAsync`.
7. The move-target alternative attribute name is `SiInbox.Move.TargetAltId`. The legacy long form `SiInbox.Move.TargetProjectAlternativeId` must not be reintroduced.
8. Service boundary:
   - `SiOffice.AccService` is the central service for ACC operations in service mode.
   - When `AccService:BaseUrl` is configured, remote WPF clients call the service instead of running local privileged ACC orchestration (no local `AccUserBootstrapService.ProvisionUsersAsync`).
   - `SiOffice.AutodeskConnector` is the connector layer used by the service / privileged paths.
9. `OpenQuoteProject` does not have to create an ACC project. `MoveToProject` performs the ACC ensure at move time, and only required folders are created.
10. Legacy DB-only open / move fallbacks are disabled or clearly marked; they are not re-enabled without explicit safety review.

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
- Do not change schema, migrations, ModelSnapshot, `ProjectFileInstance`, `Refile`, `MoveToProject` service, ACC metadata write path, `SetItemCustomAttributesAsync`, or `TokenProvider` as part of ACC documentation/principles work.
- Do not introduce new ACC bootstrap behavior at startup.

## Dropped / cancelled / postponed
- DOM / scraped UI of ACC web as a source of truth — dropped.
- DB-only file existence proof — dropped.
- Reintroduction of `SiInbox.Move.TargetProjectAlternativeId` — dropped.
- ACC Inbox folder names derived from Gmail mailbox-local `message.id` /
  `threadId` — dropped.
- Flat `MSG_<MessageKey>\…` ACC Inbox layout as the **final** target —
  replaced in principle by `THREAD_<ThreadKey>\MSG_<MessageKey>\…`. Any
  current implementation difference is logged in
  [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md).
- Deep documentation of every ACC API call — postponed.

## Relevant terms / search terms
ACC, BIM 360, AccInboxLayout, AccInboxReconciliationService, Bim360Service, EnsureCustomAttributeDefinitionsAsync, SetItemCustomAttributesAsync, SiInbox.Move.TargetAltId, ProjectFileInstance, ShowAttachmentInAccAsync, MoveToProjectProcessActionHandler, MissingInAcc, StaleAccReference, AccService, TokenProvider, two-legged, three-legged.

## Relevant code areas (informational)
- `SiOffice.AccService`
- `SiOffice.AutodeskConnector`
- `Bim360Service`
- `AccInboxLayout`, `AccInboxReconciliationService`
- `MoveToProjectProcessActionHandler`
