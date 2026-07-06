# Email List Migration

> **Status:** V3 standalone component (2026-07-06)  
> Related: [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md), [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md), [`EMAIL_FILING_SERVICE_DESIGN.md`](./EMAIL_FILING_SERVICE_DESIGN.md)

## Goal

Self-contained **Email List component** for the Email Workbench: Outlook-style cards, Gmail paging (50), account bar, filters, labels, and read-only project-link state — without Gmail writes or LegacyBridge.

## Components

| Component | Path | Role |
| --- | --- | --- |
| `EmailWindowView` | `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml` | Workbench shell: project bar (row 1), filter bar (row 2), list + preview + sidebar (row 3) |
| `EmailListFilterBar` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml` | Full-width account bar, paging, filters, project-group chrome |
| `EmailListView` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml` | List panel only: load banners + scrollable virtualized card list |
| `EmailListItemCard` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml` | Compact Outlook-style row |
| `EmailListViewModel` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs` | Paging, filters, auth, load states, link enrichment |
| `EmailWindowViewModel` | parent shell | Detail pane, `ApplyContext`, optional project context |

## Default Gmail query (legacy port)

Legacy `GoogleService.GetUnassignedEmailsPagedAsync` uses `label:INBOX`, page size 50, Gmail `pageToken` stack.

Configured in `GmailOptions.DefaultMailboxQuery`. Optional project filter pushes `label:` when enabled.

## V3 scope (read-only)

| Feature | Implementation |
| --- | --- |
| Workbench layout | Row 1: project context bar (full width). Row 2: `EmailListFilterBar` (account, filters, paging). Row 3: list column = `EmailListView` only |
| List scroll | Internal `ListBox` scroll within current page (`VirtualizingStackPanel`, row `Height="*"`) — separate from Gmail 50-item paging |
| Default load | `IEmailGateway.GetMailboxPageAsync` — INBOX; no project required |
| Paging | 50 items; token stack; prev/next in filter bar |
| Account status | `IConnectorAuthService.ConnectedAccountEmail` + Connect/Disconnect in filter bar |
| Cards | `ListBox` + `EmailListItemCard` (not GridView) |
| Load states | `EmailListLoadState`: Loading, Loaded, PartialFailure, Error, NoResults |
| Partial enrichment | Gmail rows shown + warning if DB link query fails |
| Labels | Chips on card; filter + group-by `PrimaryLabel` |
| Link state | `IEmailThreadLinkQueryService` + client filter All/Linked/Unlinked |
| Unread | `GmailEmailGateway.ResolveIsUnread`: `true` only when Gmail `LabelIds` contains `UNREAD`; card shows blue side bar (unread) or gray side bar (read) — no badge |

## Grouping rule

Multi-label messages appear once under **PrimaryLabel** (first user label under `{RootLabel}/…`; else `"ללא label"`).

## Application ports (reuse — no parallel IEmailListService)

- `IEmailGateway.GetMailboxPageAsync` / `GetMailboxLabelsAsync`
- `IEmailThreadLinkQueryService.GetLinkStatesByInternetMessageIdsAsync`
- `IConnectorAuthService` (+ `ConnectedAccountEmail`, `RefreshAccountProfileAsync`, `Logout`)

## Workbench integration

`EmailWindowView` uses a **5-row grid**:

| Row | Content |
| --- | --- |
| 0 | Header chrome |
| 1 | Project context bar — shared `ProjectSelectorView` via `ICurrentProjectContext` (full width) |
| 2 | `EmailListFilterBar` — account, filters, paging, project-group chrome (`DataContext="{Binding EmailList}"`) |
| 3 | Main split: `EmailListView` (list only) \| preview \| context sidebar |
| 4 | Status bar |

The list component receives `EmailListProjectContext` through `ApplyProjectContextAsync` — it does not own project selection.

| Mode | Behavior |
| --- | --- |
| No project selected | Gmail INBOX paging (50), filters, label grouping |
| Project selected | Gmail project-label query; first 10 emails + "הצג עוד" (+10); separate from 50-page paging |

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
