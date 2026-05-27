# UI Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for UI conventions.
- **Scope:** WPF desktop UI, WebView2 hosts (Gmail, ACC), separation between view and business logic.

## Purpose
Define how the desktop UI is built and where its responsibility ends.

## Source of truth
- This document for principles.
- `Docs\UI-Consistency-System.md` for style/consistency details (still active).

## Core principles
1. **MVVM** is mandatory. ViewModels expose state; views bind to it.
2. **Never block the UI thread.** All I/O and heavy work use `async/await`; CPU-bound work uses `Task.Run`.
3. **WebView2** is used to host Gmail and ACC views. Navigation safety helpers live in `WebView2Helper.cs`.
4. Gmail WebView opens locally by **Gmail API `message.id`** using `https://mail.google.com/mail/u/0/#all/{GmailMessageId}`. This `GmailMessageId` is **mailbox-local** and must **not** be used as a business identifier. For task-based opening, the app first resolves the stored **RFC822 Message-ID** against the current user's Gmail mailbox, obtains that user's local `GmailMessageId`, and only then opens `#all/{GmailMessageId}`. Thread-based URLs are **not** the preferred path and should be used only when explicitly approved or when documenting existing legacy behavior.
5. **DOM is for display improvement only** (e.g. `ExecuteScriptAsync` cosmetic fixes). DOM is **never** a business source of truth (attachments, identity, status).
6. **Message focusing / expansion in WebView2 (display helper).** After Gmail is loaded in WebView2, the UI may use `ExecuteScriptAsync` to improve display by locating the selected message inside the Gmail conversation, scrolling to it, and expanding it if Gmail opened the thread on a different message. This is a **display-only helper**. It may use Gmail DOM attributes such as `data-legacy-message-id` when available, but those selectors are **not a stable public API** and may require maintenance if Gmail changes its DOM. This DOM-based focusing / expansion must **not** be used for business decisions, attachment detection, identity, upload state, ACC state, or workflow state.
7. Status visible to the user must come through proper UI surfaces, not just logs.
8. Folder icons in the project tree are GREEN if any descendant subtree contains at least one file, GRAY if the entire subtree is empty (recursive).
9. When an ACC file is selected, the ACC viewer URL must be exposed (e.g. copyable) so pasting it into a browser opens the file in ACC.
10. Dependency Injection is mandatory for services and settings used from the UI.

## What we do not do now
- Do not use Gmail DOM to determine attachments or any business state.
- Do not build ACC Viewer URLs from DB identifiers as a UI fallback.
- Do not introduce blocking calls on the UI thread.
- Do not scatter inline formatting; use centralized helpers for labels/formats.
- Do not close a `Task` from a `ViewModel` without going through the agreed completion / service / handler path (see `WorkflowPrinciples` § *Workflow / Task / Action handler boundaries*).
- Do not change `WorkflowStage` or `ProjectStatus` directly from a `ViewModel`; transitions go through workflow actions / engine only.
- Do not execute business actions (`MoveToProject`, `ReviewTask`, `FileQuoteMaterial`, `AddMaterialToProject`, `TaskCompletion`, `RuntimeAction`-related operations) directly from a `ViewModel`; call a Service / Dispatcher / Handler / Use Case.
- Do not surface user-impacting failures as **log-only**; system-level health goes through the **existing System Status** menu and item-level problems go through **local UI status** near the relevant item (see [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Diagnostics/DiagnosticsPrinciples-2026-05-26.md)).
- Do not create a new System Status / notifications mechanism in parallel to the existing one; use or extend it.
- Do not show vague messages such as a bare `Metadata error`; the user must understand what happened, what it means, whether retry is possible, and whether a manual action is required.
- Do not let the **Inspection / Review** UI window change `Review` / `Workflow` / `Task` state directly; the window invokes the agreed Services / Handlers (see [`Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`](../PlanReview/PlanReviewPrinciples-2026-05-26.md) § *PlanReview / Inspection / Review / AI boundaries*).
- Do not let `AI` output drive business actions from the UI (approve / reject / close / advance / write state) without explicit user confirmation or an agreed `Action Handler`.

## Dropped / cancelled / postponed
- `GmailVisibleAttachmentsDomExtractor` as an active probe — dropped (disabled/no-op).
- DOM-based attachment chip parsing as **business truth** — dropped.
- DOM-based **message focusing / expansion for display** — **allowed as a UI helper**, with a maintenance warning (Gmail DOM selectors such as `data-legacy-message-id` are not a stable public API).
- Deep UI redesign — postponed.

## Relevant terms / search terms
WPF, MVVM, WebView2, WebView2Helper, ExecuteScriptAsync, GmailPopoutUrl, EmailManagementView, EmailViewerViewModel, UI-Consistency-System, project tree, folder icons, `data-legacy-message-id`, message focusing, message expansion, Gmail conversation, mailbox-local `GmailMessageId`.

## Extracted from archived documents (Round C, 26.05.2026)

Sources (archived): `ProjectWork-Documentation.md`, `Style-Compliance-Audit.md`.

- The active TFM is **.NET 10**. Older `.NET 8` references in archived docs are historical.
- `ProjectWorkView` ("בעבודה 2") is a central work screen — file tree on one side, ACC WebView2 viewer on the other. When the user enters a project, this screen drives an **initial full scan of the current project** (scoped to that project only) to build the `ProjectFileInstance` **runtime projection** (see `ProjectFilesPrinciples`). After the initial scan, the projection is updated through internal events and focused refresh; a full rescan happens only on explicit user request or a dedicated maintenance action. The UI binds to the projection — it is not a permanent DB cache.
- XAML uses the **global** style resources `AppFontSize`, `AppFontFamily`, and `AppForeground`. Hard-coded `FontSize=` / `FontFamily=` / `Foreground=` in XAML are not allowed, except for **semantic** colors (status, alerts, badges).
- Workflow management has a single window (`WorkflowManagementWindow`); UI does not introduce parallel Builder / Policy windows.
