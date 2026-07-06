# Email List Migration

> **Status:** V1 read-only slice (2026-07-06)  
> Related: [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md), [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md), [`EMAIL_FILING_SERVICE_DESIGN.md`](./EMAIL_FILING_SERVICE_DESIGN.md)

## Goal

Extract the email list from `EmailWindowView` into a reusable read-only component while keeping the legacy `EmailManagementView` unchanged.

## New components

| Component | Path | Role |
| --- | --- | --- |
| `EmailListView` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml` | Visual list panel (RTL) |
| `EmailListViewModel` | `src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs` | Rows, selection, unread badge |
| `EmailWindowViewModel` | parent shell | Filters, Gmail load, detail pane, `ApplyContext` |

## V1 scope (read-only)

- List rows loaded via `IEmailGateway` (parent VM)
- Subject/from search (parent)
- Project scope via **global** `ICurrentProjectContext` + `ProjectSelectorView`
- Link-state display deferred to stage 2 (label-based filter)
- **No** Gmail write, link/unlink, send, task completion

## Project context difference

| Surface | Project selector scope |
| --- | --- |
| Task Workbench | **Local** `InMemoryCurrentProjectContext` — filter only |
| Email window | **Global** `ICurrentProjectContext` — shared across shell |

## Task-driven open (stage 4)

- `WorkSurfaceContext.PrimaryWorkTargetEntityId` = `EmailInboxMessage.Id`
- `IEmailInboxQueryService` resolves inbox row → correlate with Gmail list (`InternetMessageId` / subject)
- `IWorkSurfaceLauncher` maps `Component.EmailFiling` → `IEmailWindowFactory` + `ApplyContext`

## Deferred

- `IEmailFilingService` implementation (stage 3 — write policy)
- Unassigned inbox view without project
- `ThreadStatusMapping` read port for link-state badges
