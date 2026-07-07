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

## Default Gmail query (legacy vs new)

### Legacy (`GoogleService` / `EmailManagementViewModel`)

| Concern | Legacy behavior |
| --- | --- |
| Default list | `label:INBOX` — all Inbox tabs (Primary + Promotions + Social + …) |
| Unread-only list | `label:INBOX category:primary is:unread` when `ShowUnreadOnly` ON |
| Global unread badge | Count IDs with `label:INBOX category:primary is:unread` (not `ResultSizeEstimate`) |
| Per-message unread | `LabelIds` contains `UNREAD` |

Legacy default **list** was broader than Gmail Primary tab; only unread badge/unread-only mode used `category:primary`.

### New default (2026-07-07)

| Concern | New behavior |
| --- | --- |
| Default scope | **Primary Inbox** — `label:INBOX category:primary` via `EmailMailboxScope.Inbox` |
| All Mail | Opt-in scope — `-in:spam -in:trash -in:drafts -in:sent` |
| Unread scope | `label:INBOX category:primary is:unread` |
| Label scope | Explicit label selection only (Promotions etc. only when user picks label) |
| Unread total | Separate `IEmailGateway.GetMailboxUnreadCountAsync` — not derived from current 50-item page |
| Unread in page | `UnreadInCurrentPage` — count on loaded page only |
| Per-message unread | `GmailEmailGateway.ResolveIsUnread` — `UNREAD` in `labelIds`; missing labels → read |

**`category:primary` caveat:** some org Gmail configs may categorize inconsistently. Host can override `GmailOptions.DefaultMailboxQuery` if Primary tab returns empty results.

Query composition lives in `EmailMailboxQueryComposer` (Application) and is used by `GmailEmailGateway` + `EmailListViewModel` diagnostics.

## V3 scope (read-only)

| Feature | Implementation |
| --- | --- |
| Workbench layout | Row 1: project context bar (full width). Row 2: `EmailListFilterBar` (account, filters, paging). Row 3: list column = `EmailListView` only |
| List scroll | Internal `ListBox` scroll within current page (row `Height="*"`; non-virtualized because label grouping is enabled) — separate from Gmail 50-item paging |
| Default load | `IEmailGateway.GetMailboxPageAsync` — Primary Inbox (`EmailMailboxScope.Inbox`); no project required |
| Mailbox scope | Filter bar: Inbox (default) / All Mail / Unread + optional label picker |
| Paging | 50 items; token stack; prev/next in filter bar |
| Account status | `IConnectorAuthService.ConnectedAccountEmail` + Connect/Disconnect in filter bar |
| Disconnect | Hard logout (deletes persisted token + cached client); `ClearEmailState()` wipes list, preview selection, paging tokens, filters, labels |
| Reconnect | `ConnectorLoginOptions(SkipSilentRestore, PromptAccountSelection)` → OAuth → auto-load page 1 (50 items) |
| Post-connect UI refresh | `RefreshGmailAccountStatusAsync` + `AccountStatusChanged` on UI thread — no window reopen required after disconnect/reconnect |
| Cards | `ListBox` + `EmailListItemCard` (not GridView) |
| Load states | `EmailListLoadState`: Loading, Loaded, PartialFailure, Error, NoResults |
| Partial enrichment | Gmail rows shown + warning if DB link query fails |
| Labels | Chips on card; collapsible group-by label; per-label load more/all |
| Link state | `IEmailThreadLinkQueryService` + client filter All/Linked/Unlinked |
| Unread | Per-message: `UNREAD` in Gmail `labelIds`. Total: `GetMailboxUnreadCountAsync` (scope-accurate). Page: `UnreadInCurrentPage`. Display: `UnreadCountDisplay` shows total + page separately |

## Grouping rule

### Flat list (default)

Multi-label messages appear once in the flat list. `PrimaryLabel` is used for display chips and legacy grouping.

### Collapsible label groups (`GroupByLabel = true`)

| Aspect | Behavior |
| --- | --- |
| UI | `EmailLabelGroupViewModel` per label — `Expander` header + inner email list |
| Multi-label | A message with several labels may appear in **more than one group** |
| In-group dedupe | Same `GmailMessageId` never duplicated within one group |
| Seed | Groups built from the current global page (50 items) |
| Per-label paging | `Load more` / `Load all` via `GetMailboxPageAsync` with `EmailMailboxQuery.LabelId` |
| Tokens | Each group has its own `NextPageToken` — separate from global `_pageTokenStack` |
| Safety cap | Load-all stops after 20 pages (1000 messages); shows "יש עוד — לחץ טען עוד" if capped |
| Filter change | `ClearLabelGroups()` on filter/scope/clear/disconnect — extended loads reset |
| Query | Uses Gmail `LabelIds` API path when `LabelId` is set — not display name alone |

Context menu on group header: load all, load more 50, expand, collapse.

## Application ports (reuse — no parallel IEmailListService)

- `IEmailGateway.GetMailboxPageAsync` / `GetMailboxLabelsAsync` / `GetMailboxUnreadCountAsync`
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
| No project selected | Gmail Primary Inbox paging (50), mailbox scope filters, label grouping |
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
