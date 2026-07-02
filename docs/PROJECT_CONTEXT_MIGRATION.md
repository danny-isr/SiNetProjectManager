# Project Context Migration — Cross-Application Project Selector & Current Project

> **Status:** Migration plan (planning slice) — 2026-06-27
> **Working branch:** `SiWorkNet10`
> **Target document (source of truth):** [`PROJECTS.md`](./PROJECTS.md)
> **Read together with:** [`PROJECTS.md`](./PROJECTS.md),
> [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md),
> [`MIGRATION_MAP.md`](./MIGRATION_MAP.md), [`AI_DEVELOPMENT_GUIDE.md`](./AI_DEVELOPMENT_GUIDE.md),
> [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md).
>
> **This migration plan implements the target defined in [`PROJECTS.md`](./PROJECTS.md).**
> **If this migration plan conflicts with `PROJECTS.md`, `PROJECTS.md` wins and this migration plan
> must be updated.**
>
> **Scope of this document:** migration/sequencing only. The *target state* of the Project domain is
> defined in [`PROJECTS.md`](./PROJECTS.md); this document describes **how** to get there (order,
> slices, legacy mapping). No implementation is performed by this slice. No DB / schema / migration /
> `ModelSnapshot` / `DbContext` / `DbSet` changes are proposed or made.

This document is the **migration plan** for the app-wide **Project Context / Project Selector**
mechanism: how the target described in [`PROJECTS.md`](./PROJECTS.md) is reached — how a project is
searched, selected, remembered as "current", and observed by every Work Surface (Email, Inspection,
ProjectWork, Tasks, Workflow) and the MainWindow title/header. **`PROJECTS.md` is the authoritative
target-state description; this document sequences the work to implement it.**

It treats project selection as **cross-application infrastructure**, not an Email-only control.

---

## 1. Active documentation alignment

The authoritative target for this migration is [`PROJECTS.md`](./PROJECTS.md) (the active target-state
Project domain document). This plan is additionally bound by the active architecture docs at repo-root
`docs/`. Verified during this slice:

| Rule (source) | This design complies by… |
| --- | --- |
| **Workflow first. Tasks second. Screens as Work Surfaces. Domain services behind screens.** (`ARCHITECTURE_TARGET.md` §1, `AI_DEVELOPMENT_GUIDE.md` §3) | Project context is a support service *behind* screens; it never mutates workflow or tasks. |
| **Work Surfaces receive an explicit `WorkSurfaceContext` and do not guess context.** (`ARCHITECTURE_TARGET.md` §4, `AI_DEVELOPMENT_GUIDE.md` rule 12) | `WorkSurfaceContext.ProjectId` remains the authority when a surface is opened *from a task*. The Project Selector / `ICurrentProjectContext` only drives *manual, user-initiated* project selection; it must not override an explicit task context. See §7. |
| **Ports/DTOs go in `SiNet.Application`.** (`ARCHITECTURE_TARGET.md` §2/§3, `AI_DEVELOPMENT_GUIDE.md` §1) | `ProjectSummaryDto`, `ProjectSearchQuery`, `IProjectQueryService`, `ICurrentProjectContext` are defined in `src/SiNet.Application/Projects/`. |
| **No WPF outside `SiNet.App.Wpf`.** (both) | The shared `ProjectSelectorView` lives only in `src/SiNet.App.Wpf/Shared/Projects/`. |
| **No `DbContext` in the UI; external systems only behind interfaces; `Infrastructure.Sql` uses `IDbContextFactory<>`.** (both) | The real project query is an `Infrastructure.Sql` (or `LegacyBridge`) implementation behind `IProjectQueryService`; the WPF ViewModel never touches EF. |
| **No direct workflow mutation from UI.** (`ARCHITECTURE_TARGET.md` §4, `AI_DEVELOPMENT_GUIDE.md` rule 11) | Changing the current project raises an event only; it does not start/advance/complete anything. |
| **Never hand-edit migrations / `*.Designer.cs` / `ModelSnapshot`; change model only and report.** (both, rule 9/10) | This design is **runtime-only**: no EF entities, no schema, no migrations. |
| **Visual-clone first: preserve existing windows, strip hidden logic, no DB/schema change.** (`UI_WINDOW_MIGRATION_MAP.md`) | The shared selector reproduces the *existing* `SearchableProjectSelector` UX and filter strip; it does not redesign it. |

### Documentation-mismatch check (required by the task)

- The task warned that `UI_WINDOW_MIGRATION_MAP.md` might still show Email as
  `EmailManagementView → src/SiNet.App.Wpf/Surfaces/Inbox/... (TBD) → Not started`.
- **This mismatch does NOT exist in the current working tree.** `UI_WINDOW_MIGRATION_MAP.md` already
  targets `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView` with status *Started / partial*
  (migration-map table + the "Email window — EmailManagementView" section). The target is correctly
  `Surfaces/Email`, **not** `Surfaces/Inbox`. No rename or correction was required.
- As part of this slice, a small cross-reference note was added to the Email section of
  `UI_WINDOW_MIGRATION_MAP.md` pointing here and clarifying that the project selector + filters are
  shared, not Email-only. No other doc changes were needed.

> **Note on doc locations:** the four *active* migration docs live at repo-root `docs/`. A separate,
> older per-domain documentation tree exists under `SiNetProjectManagerV2/Docs/`
> (`Domains/`, `Decisions/`, `Archive/`); it is **not** the active migration source of truth for this
> refactor and is left untouched.

---

## 2. Existing legacy project-selection mechanisms found

All paths are relative to the legacy host repo (`SiNetProjectManager_GitHub`) unless noted.
`SiNetSQL` is a **sibling git repo** (`../SiNetSQL/SiNetSQL/`) and is treated as read-only reference.

| Mechanism | Location | Role |
| --- | --- | --- |
| **`SearchableProjectSelector`** (UserControl) | `SiNetProjectManagerV2/Controls/SearchableProjectSelector.xaml(.cs)` | The **reusable** high-performance searchable project ComboBox. Already shared by Email, ProjectWork, Tasks, project edit/create, report dialogs. This is the closest existing analog of the target shared component. |
| **`ProjectSelectorDialog`** | `SiNetProjectManagerV2/Dialogs/ProjectSelectorDialog.xaml(.cs)` | Modal "pick a project" dialog wrapping `SearchableProjectSelector`; exposes `SelectedProject`. Used by move-to-project / continuation flows (e.g. `WpfActionContinuationUiHost`). |
| **`ActiveProjectContext`** (singleton) | `../SiNetSQL/SiNetSQL/Services/ActiveProjectContext.cs` | The app-wide **current project** holder. See §4. Direct legacy analog of the target `ICurrentProjectContext`. |
| **Per-VM project lists + filters** | `EmailManagementViewModel`, `ProjectWorkViewModel`, `TaskPanelViewModel` (in `SiNetSQL/MVVM/`) | Each screen loads its own `ProjectsView` / `JobTypes` / `ProjectStatuses` / `Users` from the DB and builds its own `ProjectFilterPredicate`. This logic is **duplicated per screen** today — the core reason to centralize. |
| **`EmailSelectedProjectInfoControl`** | `SiNetProjectManagerV2/Controls/EmailSelectedProjectInfoControl.xaml(.cs)` | Read-only strip showing the selected project's `NameAndNumber` / `Place.Title` / `Company.Title`. Mirrors what a shared "current project" header would render. |

### `SearchableProjectSelector` — surface area (dependency properties)

- `ItemsSource` — the project collection.
- `SelectedItem` — two-way selected project (bound to each VM's `SelectedProject`).
- `FilterText` — user search text (highlighted in the drop-down).
- `DisplayMemberPath` — default `NameAndNumber`.
- `SortPropertyPath` / `SortAscending` — default sort by `Number`, **descending** (newest first).
- `FilterProperties` — default `NameAndNumber,Title,Place.Title,Company.Title` (supports nested paths).
- `ExcludedNumbers` — project numbers to hide (dummy projects, e.g. 0 / 9999).
- `ExternalFilter` (`Predicate<object>`) — the VM-level filter for **Status / JobType / User**;
  the control refreshes its view when this changes.

> The control binds to the legacy EF entity `SiNetSQL.Models.Project` (uses `NameAndNumber`, `Number`,
> `Title`, `Place.Title`, `Company.Title`). The target shared component will bind to a UI DTO
> (`ProjectSummaryDto`) instead, so WPF no longer depends on EF entities.

---

## 3. Existing project filters and their meaning

Both `EmailManagementView` and `ProjectWorkView` render the **same** filter strip (confirmed in
markup):

| Filter (legacy binding) | Source list | Meaning |
| --- | --- | --- |
| **Project selector** (`SelectedProject`, `ExternalFilter=ProjectFilterPredicate`) | `ProjectsView` | Free-text search over number/title/city/client; the chosen project. |
| **Job Type** (`SelectedJobTypeFilter`) | `JobTypes` (display `Title`) | Restrict the project list to a project type / discipline (legacy `JobType`, a.k.a. ProjectType). |
| **Status** (`SelectedStatusFilter`) | `ProjectStatuses` (display `Title`) | Restrict by project status/state. |
| **User** (`SelectedUserFilter`) | `Users` (display `Name`) | Restrict by assigned/responsible user. |
| **Refresh projects** (`RefreshProjectsCommand`) | — | Reload the project list from the DB. |

Email-only additions on the same row (**not** part of project selection, must stay in Email):

| Control (legacy binding) | Meaning |
| --- | --- |
| **Gmail search** (`GmailSearchText`) | Free-text search of *email content* in Gmail — an email concern, not a project filter. |
| Date pickers / pagination / calendar toggle / help | Email triage UI. |

**Design consequence:** the Job Type / Status / User filters are project-scoping filters and belong in
the shared selector. The Gmail search + email triage controls stay in the Email surface.

---

## 4. Where the current project is stored today

The app-wide "current project" lives in **`SiNetSQL.Services.ActiveProjectContext`** — a thread-safe
`Lazy<>` singleton (its own doc-comment says it "mirrors the `CurrentUserContext` pattern"):

```csharp
public sealed class ActiveProjectContext
{
	public static ActiveProjectContext Instance { get; }        // Lazy singleton

	public Project? ActiveProject { get; }                       // lock-guarded
	public int?     ActiveProjectId { get; }
	public bool     HasActiveProject { get; }

	public event Action<Project?>? ActiveProjectChanged;         // fired outside the lock
	public event Action?           TaskDataChanged;              // unrelated task-refresh signal

	public void SetActiveProject(Project? project);              // no-op if same Id; then raises event
	public void Clear();                                         // = SetActiveProject(null)
	public void NotifyTaskDataChanged();
}
```

Characteristics that the new design should preserve:

- **De-duplication by id:** `SetActiveProject` skips the event if `ActiveProject?.Id == project?.Id`.
- **Thread-safe reads:** property access is `lock`-guarded; the event is raised *outside* the lock.
- **UI marshaling is the subscriber's job** (must dispatch to the WPF Dispatcher).
- It is **runtime-only** (no persistence, no table).

> This is the concrete legacy proof that "current project" is already a global, cross-screen concept —
> the migration formalizes it behind an Application port instead of a `SiNetSQL` singleton.

---

## 5. How MainWindow/title currently reflects the current project

`SiNetProjectManagerV2/MainWindow.xaml.cs`:

- Defines a static default title: `_defaultTitle = "תוכנת ניהול   v{version}"`.
- On load, subscribes: `ActiveProjectContext.Instance.ActiveProjectChanged += OnActiveProjectChanged`.
- `OnActiveProjectChanged(Project?)` sets:
  - `Title = $"{_defaultTitle} - {project.Title}"` when a project is active, or
  - `Title = _defaultTitle` when cleared.
- (There is also an email-focused title *hint* fallback used when navigating to a specific email.)

So today the **MainWindow already listens to the shared context** and updates its own title. Individual
feature screens do **not** push the title directly — they call `SetActiveProject(...)`, and the window
reacts. The target design keeps exactly this direction (see §7).

---

## 6. Which windows depend on the selected project

Confirmed consumers of project selection / current project (legacy):

| Window / surface | How it depends |
| --- | --- |
| **EmailManagementView** | `SearchableProjectSelector` → `SelectedProject`; `ExternalFilter=ProjectFilterPredicate`; Job Type/Status/User filters; `EmailSelectedProjectInfoControl` header. |
| **ProjectWorkView** | Identical selector + filter strip (`SelectedProject`, `ProjectFilterPredicate`, Job Type/Status). |
| **TaskPanelView** | Embeds `SearchableProjectSelector` to scope the task list to a project. |
| **WorkflowDashboardView** / `WorkflowManagementWindow` | Project combo (`SelectedProject` / `_dashboardSelectedProject`) scopes dashboard/policy queries. |
| **MainWindow (title/header)** | Subscribes to `ActiveProjectContext.ActiveProjectChanged` (see §5). |
| **Move-to-project / continuation flows** | `ProjectSelectorDialog` → `SelectedProject` (e.g. `WpfActionContinuationUiHost`, `EmailManagementView` file dialog). |
| **New Email visual clone** (`Surfaces/Email/EmailWindowView`) | Currently has **local-only** fake project display state; target is to consume the shared selector + context. |

---

## 7. Proposed new architecture

### 7.1 Layering overview

```
[ SiNet.App.Wpf ]   Shared/Projects/ProjectSelectorView (+ VM, + design data)
		│  binds SelectedItem -> ProjectSummaryDto,  ExternalFilters -> query
		▼
[ SiNet.Application ]  IProjectQueryService (search/get)   ICurrentProjectContext (current + event)
		│  (ports only, runtime DTOs)
		▼
[ SiNet.Infrastructure.Sql  OR  SiNet.LegacyBridge ]  real implementations
		- IProjectQueryService  -> EF via IDbContextFactory<>  (or delegate to legacy list load)
		- ICurrentProjectContext -> may wrap legacy ActiveProjectContext during migration
```

- Manual selection: `ProjectSelectorView` → user picks a `ProjectSummaryDto` →
  `ICurrentProjectContext.SetCurrentProjectAsync(dto)` → `CurrentProjectChanged` fires →
  MainWindow title + any interested surface updates.
- **Task-opened surfaces still win:** when a Work Surface is opened from a task it receives an explicit
  `WorkSurfaceContext` (with `ProjectId`). That explicit context is authoritative for *that* surface;
  the shared current-project is for user-initiated navigation and must not silently override a task's
  target. A surface may *optionally* sync the current project to its context, but never the reverse.

### 7.2 Application layer — runtime-only DTOs and ports

Location: `src/SiNet.Application/Projects/` (new folder). **No EF entities, no schema, no migrations.**

```csharp
// ProjectSummaryDto.cs
public sealed record ProjectSummaryDto(
	int ProjectId,
	string ProjectNumber,
	string ProjectName,
	string? JobType,
	string? Status,
	string? AssignedUserName,
	bool IsActive);
```

```csharp
// ProjectSearchQuery.cs
public sealed record ProjectSearchQuery(
	string? SearchText,
	string? JobType,
	string? Status,
	int? AssignedUserId,
	bool IncludeClosed);
```

```csharp
// IProjectQueryService.cs
public interface IProjectQueryService
{
	Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
		ProjectSearchQuery query,
		CancellationToken cancellationToken = default);

	Task<ProjectSummaryDto?> GetProjectAsync(
		int projectId,
		CancellationToken cancellationToken = default);
}
```

```csharp
// ICurrentProjectContext.cs  (+ ProjectChangedEventArgs.cs)
public interface ICurrentProjectContext
{
	ProjectSummaryDto? CurrentProject { get; }

	event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged;

	Task SetCurrentProjectAsync(
		ProjectSummaryDto? project,
		CancellationToken cancellationToken = default);
}

public sealed class ProjectChangedEventArgs : EventArgs
{
	public ProjectSummaryDto? Project { get; }
	public ProjectChangedEventArgs(ProjectSummaryDto? project) => Project = project;
}
```

Design notes (carried over from the legacy `ActiveProjectContext` behavior, §4):

- De-duplicate by `ProjectId` (do not raise the event when the id is unchanged).
- Treat `null` as "no current project" (title falls back to default) — never invent an id.
- `CurrentProjectChanged` may be raised off the UI thread; WPF subscribers marshal to the Dispatcher.
- Mirror the existing `ICurrentUserContext` house style (`src/SiNet.Application/Identity/`): minimal,
  runtime-only, `null` when unbound, never an authority for authorization.

### 7.3 Shared WPF component

Location: `src/SiNet.App.Wpf/Shared/Projects/` (new folder).

```
ProjectSelectorView.xaml
ProjectSelectorView.xaml.cs
ProjectSelectorViewModel.cs
ProjectSelectorDesignData.cs
```

The `ProjectSelectorViewModel` exposes, matching what exists in the legacy UI (§2/§3):

- `SearchText` (free-text; number/title/city/client).
- `SelectedProject` (`ProjectSummaryDto?`).
- `JobTypeFilter` / available job types.
- `StatusFilter` / available statuses.
- `AssignedUserFilter` / available users *(present because the legacy Email/Work filter strip has it)*.
- `IncludeClosed` (active/closed toggle) — maps to `ProjectSearchQuery.IncludeClosed` /
  `ProjectSummaryDto.IsActive`.
- `IsBusy` / `StatusMessage` (loading + status text).
- `RefreshCommand` (re-query via `IProjectQueryService`).

Behavior parity to preserve from `SearchableProjectSelector`: default sort by number descending,
search across number/title/city/client, exclusion of dummy project numbers, and the
"[Number] Title | Company" compact display. Virtualization/highlighting are visual concerns of the view.

> For the first slices this VM can be fed by a **fake in-memory** `IProjectQueryService` and a
> **fake `ICurrentProjectContext`** so the shared control renders and behaves without any DB.

### 7.4 Email integration (target)

```
EmailWindowView
  → hosts ProjectSelectorView  (shared component)
  → ProjectSelectorView drives ICurrentProjectContext.SetCurrentProjectAsync(...)
  → EmailWindowViewModel OBSERVES ICurrentProjectContext.CurrentProject / CurrentProjectChanged
```

- The Email clone must **not** implement project selection as local-only state in
  `EmailWindowViewModel`. Its current fake `ActiveProjectDisplay` is a placeholder to be replaced by an
  observation of `ICurrentProjectContext`.
- The Gmail search + email triage controls remain in Email (they are not project selection).
- May remain fake/in-memory for now (no Gmail, no DB), consistent with the visual-clone slices.

### 7.5 MainWindow title / header (target)

```
ICurrentProjectContext.CurrentProjectChanged
  → MainWindow / MainShell listens (single subscriber)
  → app title/header updates: "{default} - {CurrentProject.ProjectName}"  (or default when null)
```

- This preserves the **existing** legacy direction (§5): the shell listens; screens do not set the
  global title independently.
- Individual windows must not update the global title directly; they change the *current project* and
  the shell reacts.

---

## 8. First implementation slice

Do **not** implement in this planning slice. After this design is accepted, the recommended first
implementation slice (fake-data-first, per `AI_DEVELOPMENT_GUIDE.md` vertical-slice rule) is:

1. Add Application DTOs/ports in `src/SiNet.Application/Projects/`:
   `ProjectSummaryDto`, `ProjectSearchQuery`, `IProjectQueryService`, `ICurrentProjectContext`
   (+ `ProjectChangedEventArgs`). Runtime-only; no EF.
2. Add a **fake/in-memory** `IProjectQueryService` (sample projects) — for now this may live as a WPF
   design-time/fake source or a small `SiNet.Application`-level fake used by the host, with the real
   `Infrastructure.Sql` / `LegacyBridge` implementation deferred.
3. Add an **in-memory** `ICurrentProjectContext` (de-dupe by id; raise `CurrentProjectChanged`).
4. Add the shared `ProjectSelectorView` + `ProjectSelectorViewModel` (+ design data) under
   `src/SiNet.App.Wpf/Shared/Projects/`, wired to the fake query service.
5. Embed `ProjectSelectorView` into `EmailWindowView` (replacing the local fake project display).
6. Make `EmailWindowViewModel` **observe** `ICurrentProjectContext` (no local-only selection state).
7. **No real DB source yet.**
8. **No real email filtering yet.**

Verification for that future slice: `dotnet build SiNet.sln` (and `MSBuild SiNetProjectManager.sln`
only if legacy host files are touched); confirm no DB/migration/schema changes.

---

## 8a. Real read-only source slice (implemented)

> **Status:** Implemented — replaces the fake list with a real, **read-only** `IProjectQueryService`
> behind the existing port. Read-only only: no writes, no schema, no migrations, no email filtering,
> no workflow mutation.

### Chosen path: `SiNet.Infrastructure.Sql` (Option A)

The real implementation is `SiNetSQL.Services.Projects.ProjectQueryService`
(`src/SiNet.Infrastructure.Sql/Services/Projects/ProjectQueryService.cs`), reading via
`IDbContextFactory<SiNetSQLDbContext>` with `AsNoTracking()`.

Chosen over the `LegacyBridge` adapter (Option B) because `Infrastructure.Sql` **already** owns the EF
model, the `IDbContextFactory<SiNetSQLDbContext>` registration, and an existing read-only service
precedent (`WorkflowQueryService`). It is the smallest, safest change that keeps the read side inside the
persistence module and matches `ARCHITECTURE_TARGET.md` / `AI_DEVELOPMENT_GUIDE.md`. The legacy authority
mirrored is `SiNetProjectManagerV2/Dialogs/ProjectSelectorDialog.LoadProjects` (load `Where(NameAndNumber != null)`,
include Place/Company, `OrderByDescending(Number)`, `AsNoTracking()`).

### Field mapping (`Project` entity → `ProjectSummaryDto`)

| DTO field | Source | Notes |
| --- | --- | --- |
| `ProjectId` | `Project.Id` | |
| `ProjectNumber` | `Project.Number` (`float?`) | Formatted to an integer string when there is no fractional part (e.g. `1042`), else invariant round-trip; empty when null. |
| `ProjectName` | `Project.Title` | `null` → empty string. |
| `PlaceName` | `Project.Place?.Title` | Blank → `null`. |
| `CompanyName` | `Project.Company?.Title` | Blank → `null`. |
| `Status` | `Project.ProjectStatus?.Title` | Blank → `null`. |
| `JobType` | first `Project.TypeOfProjectInProjects[].ProjectType.Title` | Display-only; see deferred note below. |
| `AssignedUserName` | `Project.Worker` | Display-only string. |
| `IsActive` | `Project.EndOfProject != true` | Used by the active / IncludeClosed filter. |

Parity filtering/ordering is applied by the shared, DB-free
`SiNet.Application.Projects.ProjectSummaryQuery`, so the fake and real sources behave identically.

### Search source vs `MaxResults` (display cap only)

| Mode | Search source | Display |
| --- | --- | --- |
| **Browse** (empty `SearchText`) | All active/filtered projects in SQL | Up to `MaxResults` newest (number descending). SQL should `Take(MaxResults)` without loading the full table into memory. |
| **Search** (`SearchText` set) | **All** projects matching tokens/filters in SQL | Up to `MaxResults` rows after **relevance rank** + number sort. Old projects (e.g. number `1`) must remain findable even when 200+ newer rows also match. |

**Anti-pattern:** loading 200 rows once and treating them as the only searchable set.

**Performance note (this slice):** browse mode caps in SQL; search mode filters in SQL then applies
relevance ranking + `Take(MaxResults)` in memory on the matching subset via `ProjectSummaryQuery`.
Loading the entire catalog into WPF is forbidden.

### ProjectSelector UX (TextBox + Popup)

**Shared reusable control** — `ProjectSelectorView` is embeddable in NewShell, Email, ProjectWork,
Tasks, Workflow, dialogs, and future surfaces. It is **not** owned by any one host. Feature-specific
behavior stays in host ViewModels that **observe** `ICurrentProjectContext`; the selector does not
call Email/Gmail/Task/Workflow APIs.

**Compact by default** — single horizontal toolbar row; results in a floating Popup (no fixed panel
height in the host). Hosts configure visibility/width via dependency properties (see `docs/PROJECTS.md`
§5 “Host configuration”).

Implemented in `ProjectSelectorView` / `ProjectSelectorViewModel`:

- **TextBox + Popup/ListBox** — not ComboBox.
- **UserTyping** vs **SelectedProjectDisplay** editor modes (see `docs/PROJECTS.md` §5).
- **Toggle button** (▼) opens/closes results; empty search shows browse list.
- Popup closes on: selection, focus leave, click outside, Escape.
- **`ShowExpandedResults`**: removes the display cap (`MaxResults = null`) so all matching projects are shown;
  search source remains the full catalog. List virtualization keeps the UI scrollable.
- **`EffectiveMaxResults`** drives `ProjectSearchQuery.MaxResults` only — never limits SQL search scope.

### Filters supported now

- Free-text search across number / name / place / company (case-insensitive, multi-token AND).
- **Relevance ranking** when searching: exact project-number token match ranks above substring
  matches so `MaxResults` does not hide old projects (e.g. number `1` when searching `"1"`).
- `Status` filter (exact match on the surfaced status title).
- `JobType` filter (exact match on the surfaced job-type title — display value; see deferred note).
- `IncludeClosed` / active filter (`IsActive` from `EndOfProject`).
- Default sort by project number descending (numeric).
- Dummy/reserved project-number exclusion (`0`, `9999`) — parity with legacy `ExcludedNumbers`.

### Filters deferred / partial (documented, not guessed)

- **`AssignedUserId` (user filter):** **not applied.** The display DTO carries a user *name*
  (`Project.Worker`), not a stable user id, so filtering by `AssignedUserId` is intentionally ignored
  (rows are retained, never silently dropped). A real user-id filter needs an id on the DTO/query
  projection and is a later slice.
- **`JobType` filter:** **partial.** A project can have several types (`TypeOfProjectInProject`); the DTO
  surfaces only the *first* type's title, and the scalar `JobType` filter matches that display value.
  Multi-type-aware filtering is deferred.

### DI / runtime registration

- `AddSiNetProjectQuerySql()` (`src/SiNet.Infrastructure.Sql/ProjectQueryServiceCollectionExtensions.cs`)
  registers the real `IProjectQueryService`. It is called by the composition root
  (`SiNetCompositionExtensions.AddSiNet`) and directly by the legacy host.
- The shell pieces (singleton `ICurrentProjectContext`, `EmailWindowViewModel`, `IEmailWindowFactory`)
  moved to a real `AddSiNetProjectContext()`; `AddSiNetProjectContextFake()` remains for
  design-time/tests (it additionally registers `FakeProjectQueryService`).
- `ICurrentProjectContext` remains a **singleton per running app instance**.

---


**Do NOT:**

- Implement project selection as an **Email-only** ComboBox / local VM state.
- Hard-code a global "current project".
- Call `DbContext` directly from WPF ViewModels.
- Create or edit **migrations**, `*.Designer.cs`, or `ModelSnapshot`.
- Modify `DbContext` / `DbSet` mappings, or create new tables/columns.
- Copy legacy ViewModels wholesale (`EmailManagementViewModel`, `ProjectWorkViewModel`, …).
- Connect Gmail / Outlook, or perform ACC/file work, from the selector or its VM.
- Mutate workflow from the UI (selecting a project raises an event only).
- Let the shared current-project **override** an explicit `WorkSurfaceContext` on a task-opened surface.
- Modify the legacy `SiNetProjectManager.sln` except when wiring in a replaced port (`AI_DEVELOPMENT_GUIDE.md` §5).

**DO:**

- Put ports/DTOs in `SiNet.Application` (`Projects/`).
- Put WPF components in `SiNet.App.Wpf` (`Shared/Projects/`).
- Reach SQL/DB only behind `Infrastructure.Sql` (via `IDbContextFactory<>`) or a `LegacyBridge` adapter.
- Keep Work Surfaces receiving explicit context; the UI does not guess context.
- Bind the shared selector to `ProjectSummaryDto` (a UI DTO), never to EF entities.
- Preserve the legacy `ActiveProjectContext` behaviors (de-dupe by id, thread-safe, marshal on UI side)
  in the new `ICurrentProjectContext`.
- Update `MIGRATION_MAP.md` / `UI_WINDOW_MIGRATION_MAP.md` as each implementation slice lands.

---

## DB / schema confirmation

This is a **documentation / planning** slice. It introduces **no** code, **no** migrations, **no**
`ModelSnapshot`, **no** `*.Designer.cs`, **no** `DbContext` / `DbSet` changes, and **no** schema
changes. The only file changes are Markdown docs under `docs/`.
