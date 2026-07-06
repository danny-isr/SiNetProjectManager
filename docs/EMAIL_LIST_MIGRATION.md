# Email List Migration

> **Status:** V3 standalone component (2026-07-06)  
> Related: [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md), [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md), [`EMAIL_FILING_SERVICE_DESIGN.md`](./EMAIL_FILING_SERVICE_DESIGN.md)

## Goal

Self-contained **Email List component** for the Email Workbench: Outlook-style cards, Gmail paging (50), account bar, filters, labels, and read-only project-link state — without Gmail writes or LegacyBridge.

## Components

| Component | Path | Role |
| --- | --- | --- |
| `EmailListView` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml` | Standalone UI: account bar, paging, filters, card list |
| `EmailListItemCard` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml` | Compact Outlook-style row |
| `EmailListViewModel` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs` | Paging, filters, auth, load states, link enrichment |
| `EmailWindowViewModel` | parent shell | Detail pane, `ApplyContext`, optional project context |

## Default Gmail query (legacy port)

Legacy `GoogleService.GetUnassignedEmailsPagedAsync` uses `label:INBOX`, page size 50, Gmail `pageToken` stack.

Configured in `GmailOptions.DefaultMailboxQuery`. Optional project filter pushes `label:` when enabled.

## V3 scope (read-only)

| Feature | Implementation |
| --- | --- |
| Standalone component | Account bar + filters + paging + cards inside `EmailListView` |
| Default load | `IEmailGateway.GetMailboxPageAsync` — INBOX; no project required |
| Paging | 50 items; token stack; prev/next in component |
| Account status | `IConnectorAuthService.ConnectedAccountEmail` + Connect/Disconnect |
| Cards | `ListBox` + `EmailListItemCard` (not GridView) |
| Load states | `EmailListLoadState`: Loading, Loaded, PartialFailure, Error, NoResults |
| Partial enrichment | Gmail rows shown + warning if DB link query fails |
| Labels | Chips on card; filter + group-by `PrimaryLabel` |
| Link state | `IEmailThreadLinkQueryService` + client filter All/Linked/Unlinked |
| Unread | From Gmail `UNREAD` label id on `EmailSummary.IsUnread` |

## Grouping rule

Multi-label messages appear once under **PrimaryLabel** (first user label under `{RootLabel}/…`; else `"ללא label"`).

## Application ports (reuse — no parallel IEmailListService)

- `IEmailGateway.GetMailboxPageAsync` / `GetMailboxLabelsAsync`
- `IEmailThreadLinkQueryService.GetLinkStatesByInternetMessageIdsAsync`
- `IConnectorAuthService` (+ `ConnectedAccountEmail`, `RefreshAccountProfileAsync`, `Logout`)

## Workbench integration

`EmailWindowView` hosts `<EmailListView DataContext="{Binding EmailList}" />` beside the detail pane. Parent sets `EmailList.ProjectSelector` and handles `SelectedEmailChanged` for body load.

## Workflow gaps (deferred)

| Action | Status |
| --- | --- |
| Unlinked → triage task | Not implemented |
| Linked → ProjectWork | Not implemented |
| File/unlink | `IEmailFilingService` design only |
| Cross-page task mail select | Current page only |

## Explicitly deferred

- Gmail write / label modify / send / delete
- `IEmailFilingService` implementation
- LegacyBridge / `EmailManagementView` hosting
- Schema changes
