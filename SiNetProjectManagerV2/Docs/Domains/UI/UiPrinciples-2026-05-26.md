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
4. Gmail WebView opens by **message** using `https://mail.google.com/mail/u/0/#all/{MessageId}`. Thread-based URLs are a fallback only.
5. **DOM is for display improvement only** (e.g. `ExecuteScriptAsync` cosmetic fixes). DOM is **never** a business source of truth (attachments, identity, status).
6. Status visible to the user must come through proper UI surfaces, not just logs.
7. Folder icons in the project tree are GREEN if any descendant subtree contains at least one file, GRAY if the entire subtree is empty (recursive).
8. When an ACC file is selected, the ACC viewer URL must be exposed (e.g. copyable) so pasting it into a browser opens the file in ACC.
9. Dependency Injection is mandatory for services and settings used from the UI.

## What we do not do now
- Do not use Gmail DOM to determine attachments or any business state.
- Do not build ACC Viewer URLs from DB identifiers as a UI fallback.
- Do not introduce blocking calls on the UI thread.
- Do not scatter inline formatting; use centralized helpers for labels/formats.

## Dropped / cancelled / postponed
- `GmailVisibleAttachmentsDomExtractor` as an active probe — dropped (disabled/no-op).
- DOM-based attachment chip parsing as truth — dropped.
- Deep UI redesign — postponed.

## Relevant terms / search terms
WPF, MVVM, WebView2, WebView2Helper, ExecuteScriptAsync, GmailPopoutUrl, EmailManagementView, EmailViewerViewModel, UI-Consistency-System, project tree, folder icons.

## Extracted from archived documents (Round C, 26.05.2026)

Sources (archived): `ProjectWork-Documentation.md`, `Style-Compliance-Audit.md`.

- The active TFM is **.NET 10**. Older `.NET 8` references in archived docs are historical.
- `ProjectWorkView` ("בעבודה 2") is a central work screen — file tree on one side, ACC WebView2 viewer on the other.
- XAML uses the **global** style resources `AppFontSize`, `AppFontFamily`, and `AppForeground`. Hard-coded `FontSize=` / `FontFamily=` / `Foreground=` in XAML are not allowed, except for **semantic** colors (status, alerts, badges).
- Workflow management has a single window (`WorkflowManagementWindow`); UI does not introduce parallel Builder / Policy windows.
