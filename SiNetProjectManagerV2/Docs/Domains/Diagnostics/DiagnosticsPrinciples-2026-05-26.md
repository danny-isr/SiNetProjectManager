# Diagnostics Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for logging, diagnostics, and recovery.
- **Scope:** Structured logs, diagnostic-only mechanisms, recovery / reconciliation, disabled mechanisms tracking, user-facing system status (system-level and item-level).

## Purpose
Define how the system observes itself, surfaces problems, and recovers — and what must never silently substitute for truth.

## Source of truth
- `Docs\LOGGING.md` — centralized logging conventions.
- This document for cross-cutting diagnostics principles.

## Diagnostics layers

Diagnostics in the system are organised into **four layers**. A problem
may need to be surfaced in more than one layer; logs alone are **not**
enough when the problem affects the user.

### 1. Structured logs (developer / technical channel)

- Use the central logging facility (e.g. `AppLogger`) or the existing
  logging mechanism. Do **not** add scattered `Console.WriteLine` or
  ad-hoc string-concatenated log lines.
- Prefer explicit **tags** so logs are filterable, for example:
  - `[AccInboxReconciliation]`
  - `[AttachmentUploadStatus]`
  - `[WorkflowCompletion]`
  - `[ProjectFileInstanceRefresh]`
- Include **global business identifiers** when relevant (e.g.
  `MessageUniqueId`, `ThreadKey`, project number, `ProjectFile` /
  `ProjectAlternative` name, ACC item id, task id).
- Include **Storage Destination** when relevant (`ACC`, `File Server`,
  `Google Drive`, `Gmail` for read-only ingestion only).
- Include **status / reason / failure category** — not just a free-form
  message.
- Never log secrets / OAuth tokens / personal data beyond what is
  already approved.

### 2. User-visible status (UI surfaces, not logs only)

- A `log` line is **not enough** when the problem affects the user's
  action. The problem must be surfaced in the UI in a clear way.
- Typical cases that must be visible to the user:
  - ACC upload failed.
  - File missing in ACC.
  - Metadata could not be read.
  - Email not found in the current user's Gmail mailbox.
  - No permission for a service.
  - A workflow / handler action failed.
  - File exists in the DB but is missing in its Storage Destination.
- The message presented to the user must let them understand:
  - **what happened**,
  - **what it means**,
  - **whether retry is possible**,
  - **whether a manual action is required**,
  - **whether to contact the administrator / service**.
- Vague messages such as a bare `Metadata error` without a clear
  interpretation are **not approved**.

### 3. Existing **System Status** menu (system-level health)

- The application **already has** a System Status menu / mechanism that
  shows the state of the central services and whether they are up.
- Therefore service-level / system-level health problems must use this
  **existing** mechanism. If it needs to be extended, **extend the
  existing mechanism** instead of building a parallel one.
- A new parallel System Status / notifications mechanism is **not
  approved**.
- Examples that belong in System Status:
  - `SiOffice.AccService` not available.
  - Google / Gmail connection not healthy.
  - `SiOffice.AutodeskConnector` not responding.
  - Database connection problem.
  - AI service not available (when applicable).
  - Ongoing reconciliation / recovery issue.

### 4. Local UI status (item-level)

- Some problems are tied to a **specific item** and System Status is not
  the right place for them; they need a clear local indication on the
  relevant screen, **in addition to** the log line.
- Examples:
  - A specific file upload failed.
  - A specific file is missing in ACC.
  - A specific email is not found in the user's Gmail mailbox.
  - A specific handler action failed.
  - An invalid `ProjectAlternative` name was supplied.
- Local UI status must be presented next to the relevant item / screen
  (file in the tree, email in the inbox, task in the task list,
  workflow stage row, etc.).

## Recovery / Reconciliation

- `Recovery` and `Reconciliation` **do not invent truth** and must not
  introduce silent fallbacks.
- They **check the relevant source of truth** and update status
  accordingly. Examples:
  - **ACC reconciliation** checks ACC itself, not only the DB cache
    (see `AccSystemPrinciples` and `ProjectFilesPrinciples`).
  - **Gmail resolve** locates an email by RFC822 `Message-ID` in the
    **current user's** Gmail mailbox (see `EmailSystemPrinciples`).
  - **Storage Destination validation** checks the binding Storage
    Destination of the file (see `ProjectFilesPrinciples`).
- Metadata or DB read failures alone never mark a file as missing.
- A `Completion` (task / action) is not a status-only update: it must
  record the result, invoke the handler when required, update the
  workflow when required, surface the failure to the user, and log it
  (see
  [`Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](../Workflow/WorkflowPrinciples-2026-05-26.md)
  § *Workflow / Task / Action handler boundaries*). A fallback that
  marks success when the handler failed is **not approved**.

## Disabled / postponed mechanism tracking

- Every **disabled / postponed / candidate-for-removal** mechanism must
  be **documented**. If a mechanism is not active, the documentation
  must state:
  - **why** it is not active,
  - and **which** status it has:
    - `disabled`
    - `postponed`
    - `needs review`
    - `candidate for removal`
- A disabled mechanism must **not** be re-enabled without a dedicated
  approval round.
- Each domain Principles document carries its own *Dropped / cancelled
  / postponed* section to keep this tracking local and discoverable.

## Core principles
1. **Structured logs**: use centralized logging (e.g. `AppLogger`) with
   explicit tags, business identifiers, Storage Destination, and
   status/reason/failure category. Avoid scattered `Console.WriteLine`
   or ad-hoc string concatenation for log lines.
2. **Diagnostic-only mechanisms** (e.g. legacy DOM probes) must be
   clearly marked disabled and must not be used as business sources of
   truth.
3. **Recovery / reconciliation** is the only valid way to assert
   physical existence in ACC. Metadata or DB read failures alone never
   mark a file as missing.
4. **User-visible status** must reach the UI — important errors are
   not "logs only". System-level health goes through the **existing
   System Status** menu; item-level problems go through **local UI
   status** near the relevant item.
5. **Do not create a parallel System Status mechanism**; use or extend
   the existing one.
6. **Completion must not silently mark success** when the underlying
   business execution failed.
7. **Tracking removed / cancelled / postponed mechanisms**: each domain
   Principles document includes a *Dropped / cancelled / postponed*
   section so disabled paths are not silently revived.
8. **Disabled mechanisms must stay disabled** until an explicit safety
   review re-enables them.
9. **Always propagate `CancellationToken`** in async paths to support
   clean shutdown and recovery.

## What we do not do now
- Do not treat a `log` line as a sufficient response when the problem
  affects the user's action.
- Do not create a new System Status / notifications mechanism in
  parallel to the existing one.
- Do not add a fallback that hides a failure or marks a failed handler
  as success.
- Do not use DB-only proof to assert that a physical file exists.
- Do not leave ambiguous messages such as a bare `Metadata error`
  without a clear interpretation for the user.
- Do not re-enable disabled fallbacks without explicit approval.
- Do not log sensitive secrets.
- Do not invent new diagnostic channels that bypass `AppLogger`.

## Dropped / cancelled / postponed
- DOM-based diagnostic probes as authoritative — dropped.
- DB-only "file exists" diagnostic — dropped.
- A new System Status mechanism parallel to the existing one — **not
  approved**.
- `log`-only error reporting to the user — **not approved**.
- Vague user messages such as a bare `Metadata error` without a clear
  interpretation — **not approved**.
- Silent fallback that hides a failure — **not approved**.
- Re-enabling disabled mechanisms without an approval round — **not
  approved**.
- Full structured-logging schema / dashboard — postponed.

## Relevant terms / search terms
AppLogger, LOGGING.md, structured logging, tags, `[AccInboxReconciliation]`, `[AttachmentUploadStatus]`, `[WorkflowCompletion]`, `[ProjectFileInstanceRefresh]`, System Status, system-level status, local UI status, item-level status, reconciliation, AccInboxReconciliationService, MissingInAcc, StaleAccReference, GmailVisibleAttachmentsDomExtractor (disabled), CancellationToken, disabled, postponed, needs review, candidate for removal.
