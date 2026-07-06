# Email List Migration

> **Status:** V2 general inbox workbench (2026-07-06)  
> Related: [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md), [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md), [`EMAIL_FILING_SERVICE_DESIGN.md`](./EMAIL_FILING_SERVICE_DESIGN.md)

## Goal

General inbox workbench: paged Gmail read (50 per block), optional filters, label display/grouping, read-only project-link state from DB — without Gmail writes or LegacyBridge.

## New components

| Component | Path | Role |
| --- | --- | --- |
| `EmailListView` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml` | Multi-column list + optional label grouping |
| `EmailListViewModel` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs` | Paging, filters, link enrichment, grouping |
| `EmailWindowViewModel` | parent shell | Gmail auth, detail pane, `ApplyContext`, optional project filter |

## Default Gmail query (legacy port)

Legacy `GoogleService.GetUnassignedEmailsPagedAsync` (SiNetSQL) uses:

- **Query:** `label:INBOX`
- **Page size:** `50` (`maxResults`)
- **Paging:** Gmail `pageToken` stack (one API page per UI page)

Configured in `GmailOptions.DefaultMailboxQuery` (`"label:INBOX"`). Project-scoped loads remain available when **Filter by project** is enabled (`OptionalProjectLabel` pushed to Gmail `label:`).

## V2 scope (read-only)

| Feature | Implementation |
| --- | --- |
| Default load | `IEmailGateway.GetMailboxPageAsync` — all INBOX emails; **no project required** |
| Paging | 50 items; Previous/Next via token stack in `EmailListViewModel` |
| Labels | `EmailSummary.LabelNames`, `PrimaryLabel`; column + optional group-by |
| Link state | `IEmailThreadLinkQueryService` → `ThreadStatusMapping` + `EmailInboxMessage` + `Projects` |
| Filters | Free text, from/to, subject, label, link state (All/Linked/Unlinked), optional project |
| Grouping | `CollectionViewSource` grouped by `PrimaryLabel` — multi-label rows appear once under first user label under `{RootLabel}/…`; else `"ללא label"` |

## Project context

| Surface | Project selector |
| --- | --- |
| Task Workbench | Local filter only |
| Email window | **Global** `ICurrentProjectContext` — **optional filter** (`FilterByCurrentProject`), not a load gate |

## Application ports

- `IEmailGateway.GetMailboxPageAsync(EmailMailboxQuery, pageToken?)`
- `IEmailGateway.GetMailboxLabelsAsync()`
- `IEmailThreadLinkQueryService.GetLinkStatesByInternetMessageIdsAsync`

## Task-driven open

- `WorkSurfaceContext.PrimaryWorkTargetEntityId` = `EmailInboxMessage.Id`
- `IEmailInboxQueryService` → correlate with current Gmail page
- Opening from task with `ProjectId` enables optional project filter automatically

## Workflow gaps (V2)

| Action | Status |
| --- | --- |
| Unlinked → triage task | Not implemented — no `CreateTaskFromEmail` |
| Linked → ProjectWork | Not implemented |
| File/unlink → `ProjectAssignmentEvent` | Blocked — `IEmailFilingService` design only |
| Completion from email | Blocked |

See [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md).

## Explicitly deferred

- Gmail write / label modify / send / delete
- `IEmailFilingService` implementation
- LegacyBridge / `EmailManagementView` hosting
- Schema changes
