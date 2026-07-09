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

## Ports

See plan: IEmailBodyRenderer, IEmailAccIngestionService, IEmailAttachmentTaggingService, IEmailMoveToProjectService, IEmailExternalDownloadService, IEmailWorkflowContextService, IEmailSuggestedActionService.

## Boundary

Detail folder must not reference SiNetSQL, SiNetSQL.MVVM, or LegacyBridge.
