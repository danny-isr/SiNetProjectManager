# Email Detail Component

> **Status:** Phase 0 scaffold (2026-07-09)  
> Related: [EMAIL_LIST_MIGRATION.md](./EMAIL_LIST_MIGRATION.md), [WORK_SURFACE_WORKFLOW_INTEGRATION.md](./WORK_SURFACE_WORKFLOW_INTEGRATION.md)

## Goal

Self-contained Email Detail component: viewer, attachments, action bar, workflow actions. Clean New System only.

## Components

| Component | Path |
| --- | --- |
| EmailDetailView | src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailView.xaml |
| EmailDetailViewModel | Detail/EmailDetailViewModel.cs |
| EmailViewerPaneView | Detail/EmailViewerPaneView.xaml |
| EmailAttachmentStripView | Detail/EmailAttachmentStripView.xaml |
| EmailActionBarView | Detail/EmailActionBarView.xaml |
| EmailWorkflowActionsPaneView | Detail/EmailWorkflowActionsPaneView.xaml |
| EmailWorkItemWindow | Detail/EmailWorkItemWindow.xaml |

## Hosting

- Full inbox: EmailWindowView = EmailListView + EmailDetailView (2 columns, no calendar sidebar)
- Work item: EmailWorkItemWindow = EmailDetailView only
- Task: WorkSurfaceLauncher.ApplyContext

## Body rendering (local single-message)

- Source: `IEmailGateway.GetDetailsAsync(messageId)` → Gmail API `Messages.Get(format=full)` for **one** message id (not a thread).
- Display: `IEmailBodyRenderer` / `SiNet.App.Wpf.Surfaces.Email.WebView2EmailBodyRenderer` → `NavigateToString(HtmlBody)`. Not a Gmail popout URL.
- DI: **Transient** per email surface via `AddSiNetNewSystemWpf` (standalone) and V2 `App.xaml.cs` (same App.Wpf type) so WebView2 is not reparented across hosts.
- External download chips: detected from plain text **and** HTML body.
- Jumbo→ACC (N2): standalone registers `WpfEmailExternalDownloadBrowserHost` + `NativeEmailExternalDownloadExecutor`. V2 may override the executor with Legacy. System-browser open remains a fallback only when the browser host is missing.
- Layout: `EmailViewerPaneView` gives `BodyHost` a star-sized Grid row so WebView2 gets real height (do not nest it in a StackPanel).
- Embedded images (`<img src="cid:...">`): `GmailEmailGateway` fetches the referenced inline attachment bytes (`Messages.Attachments.Get`) into `EmailMessageDetails.InlineImages`. `WebView2EmailBodyRenderer` rewrites `cid:` to `https://sinet-mail-images.local/{content-id}` and serves the bytes via `WebResourceRequested`. Bytes are **not** inlined as Base64 data-URIs (crashes WebView2 with large images / hits the `NavigateToString` size limit). External `http(s)` images load normally.

## Ports

See plan: IEmailBodyRenderer, IEmailAccIngestionService, IEmailAttachmentTaggingService, IEmailMoveToProjectService, IEmailExternalDownloadService, IEmailWorkflowContextService, IEmailSuggestedActionService.

### Attachment tag picker (standalone)

- Button **🔗 בחר קובץ** calls `IEmailAttachmentProjectFilePickerHost` (then `IEmailAttachmentTaggingService.SetTagAsync`).
- **Standalone** registers `WpfEmailAttachmentProjectFilePickerHost` via `AddSiNetProjectContext` (`TryAddTransient`).
- UI: shared hierarchical `SiNet.App.Wpf.Shared.Pickers.FileTreePickerWindow` (same tree / type-filter / search UX as V2) fed by `IEmailAttachmentTaggingService.LoadTagPickerCatalogAsync` (OutSidData + folders + JobTypes).
- **V2** may still override with its own host wrapping the legacy window; behavior should match.
- If the host is missing, the click must surface a visible error (status + MessageBox) — not a silent no-op.

### ACC ingest without attachments (N4 / N4.3)

- Zero-attachment emails create ACC Inbox (`00_Email.pdf` + layout) only when
  mailbox-filed to a project (or after File-to-project). Unfiled browse without
  attachments does not ingest. See `docs/NATIVE_EMAIL_ACC_INGEST.md` §N4.3.
- “העלה ל-ACC” requires attachments **or** `IsFiledToProject`.

### FileMaterial / MoveToProject (six decisions)

Canonical Target: [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md).

- `00_Email.pdf` is taggable as «תוכן המייל (PDF)» only when opted in; not required by default.
- Move dismiss / `WorkItemDismissRequested` only after `AllFilesTransferred` **and** successful `CompleteAsync` with `TaskClosed` and no `WorkflowAdvancePending` — never on transfer flag alone.
- Empty business attachments: Yes (include body PDF) / No (confirm no material → Complete) / Back.

### InboxMessageId resolution for tagging (thread-safe)

When refreshing SQL attachment tag state for the selected Gmail row:

1. Prefer `EmailListRow.InboxMessageId` on that row.
2. Else resolve via `IEmailInboxQueryService.FindByMessageIdentityAsync` (RFC Message-ID / message unique id of **that** message).
3. Else fall back to task `PrimaryWorkTargetEntityId` **only** when the selected row is the pending task target (SendQuote / filing anchor).

Do **not** patch a sibling reply in the same thread with the anchor's `InboxMessageId`. Doing so makes identical attachment filenames tag/mutate the wrong SQL row.

## Boundary

Detail folder must not reference SiNetSQL, SiNetSQL.MVVM, or LegacyBridge.
