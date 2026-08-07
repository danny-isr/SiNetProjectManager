# SiNet Project Domain — Target State

> **Status:** Active target documentation (source of truth) — 2026-06-27
> **Updated:** 07.08.2026 (As-Is reconciliation -- branch SoT; live shell = NewShellWindow)
> **Working branches:** `release` + `development` -- see [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §3. `SiWorkNet10` is deprecated (not the working SoT).
> **Read together with:** [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md),
> [`MIGRATION_MAP.md`](./MIGRATION_MAP.md), [`AI_DEVELOPMENT_GUIDE.md`](./AI_DEVELOPMENT_GUIDE.md),
> [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md),
> [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md).
>
> **Migration plan:** [`PROJECT_CONTEXT_MIGRATION.md`](./PROJECT_CONTEXT_MIGRATION.md) implements this
> document. **If the migration plan and this document disagree, this document wins** and the migration
> plan must be updated.

This is the **active target** description of the **Project domain / Project Context** in the final
architecture: what a project *is* to the application, what "current project" means at runtime, how the
shared **Project Selector** behaves, and how each Work Surface (Email, Inspection, ProjectWork, Tasks,
Workflow) relates to it. When code and this document disagree, fix the document first, then the code
(per `ARCHITECTURE_TARGET.md` §1).

> This document describes the **target** state. It does **not** by itself add code, ports, UI, or any
> DB/schema/migration change. Implementation is tracked and sequenced in the migration plan.

---

## 1. Purpose

The SiNet application is **project-centered**. Almost everything a user does happens *in the context of
a specific project*. The Project domain exists to make that context **explicit, shared, and safe**:

- A single, well-defined notion of "the project I'm working in right now" (the **Current Project**).
- A single, reusable way to **search and select** a project (the **Project Selector**).
- Clean **Application ports** so screens read project data without touching the database or EF.
- Clear rules so project context never fights with **task/workflow** context.

The project is the main business context for:

- **Email** (inbox triage and move-to-project are scoped to a project).
- **Inspection** (inspection reports belong to a project).
- **ProjectWork** (files / folders / alternatives / versions of a project).
- **Tasks** (task queues are commonly scoped to a project).
- **Workflow** (workflow instances run per project).
- **Files / ACC** (storage destinations and ACC mappings are per project).
- **Reporting** (reports are produced for a project or set of projects).
- **Projects Overview Dashboard** (cross-project business overview — see
  [`PROJECTS_DASHBOARD.md`](./PROJECTS_DASHBOARD.md)).

---

## 2. Concepts

| Concept | Meaning (target) |
| --- | --- |
| **Project** | A business entity (number, name, place/city, company/client, job type, status, assigned user). Persisted in the database; surfaced to the UI as a **DTO**, never as an EF entity. |
| **Current Project** | The runtime, user-selected "active project" for manual navigation and shared shell context. Runtime-only, may be `null`. See §4. |
| **Project Selector** | The shared, reusable UI component for searching and choosing a project. See §5. |
| **Project filters** | Job Type / Status / User filters that narrow the selectable project list. See §6. |
| **Projects Overview Dashboard** | Cross-project table + KPI overview (status, types, open workflows/tasks, dates). See [`PROJECTS_DASHBOARD.md`](./PROJECTS_DASHBOARD.md). |
| **`WorkSurfaceContext`** | The explicit context a Work Surface receives when opened from a task/workflow; carries the authoritative `ProjectId` for that surface. See §7. |
| **`ICurrentProjectContext`** | The Application port that holds and broadcasts the Current Project. See §4/§12. |
| **`IProjectQueryService`** | The Application port that searches/loads project DTOs. See §12. |
| **`IProjectDashboardQueryService`** | Read-only port for the Projects Overview Dashboard rows. See [`PROJECTS_DASHBOARD.md`](./PROJECTS_DASHBOARD.md). |

---

## 3. Project as application context

- A project is the **primary axis** the app is organized around; Work Surfaces are "views onto a
  project" (plus task/workflow specifics).
- Project context is **support infrastructure behind screens** — it is *not* a workflow driver. It
  never starts, advances, completes, or cancels workflow, and it never completes tasks.
- Two independent ways a surface learns its project:
  1. **Explicit task/workflow context** — `WorkSurfaceContext.ProjectId` (authoritative when present).
  2. **Manual user navigation** — the shell **Current Project** chosen via the Project Selector.
- These must not silently conflict. The precedence rule is defined in §7.

---

## 4. Current Project

**Definition:** `Current Project` is a **runtime application context** representing the project the
user has currently selected for **manual navigation and shared shell state**.

Rules (target):

- It represents the project **currently selected by the user** for manual navigation.
- It is **runtime-only**.
- It is **not a persisted setting** unless a future requirement explicitly says so (see §15).
- It **may be `null`** (no project selected → application title falls back to its default).
- It must **not be invented or guessed**. `null` means "unknown/none"; callers must not substitute an
  arbitrary project or id.
- It **raises an event when changed** (`CurrentProjectChanged`).
- It **de-duplicates changes by `ProjectId`** — setting the same project id again does not re-raise the
  event.

**Scope (target):**

Current Project is **application-instance scoped**.
It is shared across windows in the same running app instance.
It is not shared across separate app instances.
Opening the application twice allows each instance to work on a different Current Project.

`ICurrentProjectContext` is a singleton **within one running application process**, not a machine-wide
or user-wide singleton:

```plaintext
One running app instance
→ one DI ServiceProvider
→ one singleton ICurrentProjectContext
→ all windows/surfaces in that app instance share the same Current Project

Two separate running app instances
→ two separate processes
→ two separate DI ServiceProviders
→ two separate singleton ICurrentProjectContext instances
→ each app instance may have a different Current Project
```

Rules:

- **Do not** persist, broadcast, or synchronize Current Project across separate application instances.
- **Do not** store Current Project in DB, settings, registry, file, named pipe, shared memory, or any
  other cross-process mechanism unless a future requirement explicitly says so (see §15).
- The "singleton" lifetime is **per DI container/process**, not global across processes. Two processes
  are two independent Current Projects by design.

Threading/consumption (target):

- The change event may be raised off the UI thread; **WPF subscribers marshal to the Dispatcher**.
- The current-project holder is thread-safe for reads.

> **Precedent:** the legacy app already has this exact concept in
> `SiNetSQL.Services.ActiveProjectContext` (a thread-safe singleton with `SetActiveProject`,
> `ActiveProject`/`ActiveProjectId`, and an `ActiveProjectChanged` event that de-dupes by id). The
> target formalizes it behind the Application port `ICurrentProjectContext` (see §12), mirroring the
> existing `ICurrentUserContext` house style: minimal, runtime-only, `null` when unbound, never an
> authority for authorization.

---

## 5. Project Selector

**Target shared component:**

```
SiNet.App.Wpf/Shared/Projects/ProjectSelectorView.xaml
SiNet.App.Wpf/Shared/Projects/ProjectSelectorView.xaml.cs
SiNet.App.Wpf/Shared/Projects/ProjectSelectorViewModel.cs
SiNet.App.Wpf/Shared/Projects/ProjectSelectorDesignData.cs
```

- It is **reusable across windows** (Email, ProjectWork, Tasks, Workflow, dialogs, Inspection).
- It is **not Shell-specific**, **not Email-specific**, and contains **no feature-window business logic**
  (no Gmail, tasks, workflow, file actions, or surface-specific filtering).
- It binds to a **project DTO** (`ProjectSummaryDto`), never to an EF entity.
- Host windows **embed** `ProjectSelectorView` and observe `ICurrentProjectContext` for reactions;
  they do not push Email/Task/Workflow logic into the selector.

### Shared / modular contract

| Responsibility | Owner |
| --- | --- |
| Search, filters, results popup, explicit project pick | `ProjectSelectorView` + `ProjectSelectorViewModel` |
| Publish selected project app-wide | `ICurrentProjectContext.SetCurrentProjectAsync` (on explicit pick) |
| React to project changes (Email list scope, title, etc.) | Each host window / its ViewModel — via `ICurrentProjectContext` events |
| Gmail search, task completion, workflow mutation | **Outside** the selector |

**Ports only** (no `DbContext`, no EF entities, no Shell/Email ViewModels):

- `IProjectQueryService`
- `IProjectFilterOptionsService`
- `ICurrentProjectContext`

**Default layout:** compact single-row toolbar (`CompactMode="True"`). Results use a **Popup** so they
never reserve vertical space in the host window.

### Host configuration (`ProjectSelectorView` dependency properties)

Hosts tune the control without forking XAML:

| Property | Default | Purpose |
| --- | --- | --- |
| `SearchBoxWidth` | `340` | Width of the search editor + ▼ toggle (user-resizable on Email; see DEV-017) |
| `PopupWidth` | `360` | Width of the results popup (independent of control width; DEV-017) |
| `CompactMode` | `True` | Smaller height, margins, labels, and combo widths |
| `ShowFilters` | `True` | Job Type + Status filter strip |
| `ShowUserFilter` | `False` | User filter (deferred; hidden until semantics exist) |
| `ShowIncludeClosed` | `True` | “Include closed projects” checkbox |
| `ShowExpandedResultsToggle` | `True` | “Show full list” checkbox |
| `ShowRefreshButton` | `True` | Reload filter options + project list |
| `ShowStatusMessage` | `True` | Inline status hint (compact font when `CompactMode`) |

Example (Email header strip):

```xml
<projects:ProjectSelectorView DataContext="{Binding ProjectSelector}"
                              CompactMode="True"
                              SearchBoxWidth="340" />
```

Example (minimal dialog — search only):

```xml
<projects:ProjectSelectorView DataContext="{Binding ProjectSelector}"
                              ShowFilters="False"
                              ShowIncludeClosed="False"
                              ShowExpandedResultsToggle="False"
                              ShowStatusMessage="False"
                              SearchBoxWidth="280" />
```

See also `docs/APP_SHELL.md` (NewShell hosts the same control) and
`docs/PROJECT_CONTEXT_MIGRATION.md` § shared selector boundaries.

It must support the existing legacy project-selection behavior (parity target, do not redesign the UX):

- **Searchable** project selection via **TextBox + Popup/ListBox** (not an editable ComboBox).
- Two editor modes (see below): **UserTyping** vs **SelectedProjectDisplay**.
- Search across **number / title / place (city) / company (client)**. **Multi-word** search: every
  token must match at least one field; token order does not matter (parity with legacy
  `SearchableProjectSelector`).
- Default **sort by project number, descending** (newest first).
- **Exclusion of dummy project numbers** (e.g. 0 / 9999).
- **Job Type** / **Status** filter combo boxes (see §6). Options load from
  `IProjectFilterOptionsService` — **not** from the capped project result list (`MaxResults`).
- **User** filter — *deferred* (hidden in UI until semantics are defined).
- **Refresh** (reload filter options + project list through Application ports).
- Loading / status message (`IsBusy` / `StatusMessage`). Search input stays enabled while loading.
- Explicit project selection only (click/choose from the results list); typing does not set
  `SelectedProject` until the user picks a row.
- **Dropdown toggle** (▼) opens/closes the results popup; opening with no search text shows the
  default browse list.
- **Expanded list** checkbox (`ShowExpandedResults`): default cap **200** rows; when checked, **no display cap**
  (all matching projects). ListBox virtualization keeps scrolling responsive.
- Popup **closes** after project selection, focus leaving the control, click outside, or Escape.

### TextBox modes: UserTyping vs SelectedProjectDisplay

| Mode | When | TextBox content | Reload query text |
| --- | --- | --- | --- |
| **UserTyping** | User is typing / editing | Exactly what the user typed | Same as TextBox |
| **SelectedProjectDisplay** | After explicit project pick (or external context sync) | `{ProjectNumber} — {ProjectName}` | `null` (browse) |

Rules:

- Async reloads **must not** overwrite TextBox text while the user is in **UserTyping** mode.
- After **explicit selection**, TextBox switches to **SelectedProjectDisplay** and the popup closes.
- Starting to type again switches back to **UserTyping** and opens the popup.
- `SelectedProject` / `ICurrentProjectContext` update only on explicit selection (unchanged).

### Search source vs display limit (critical)

Two concepts must stay separate:

| Concept | Meaning |
| --- | --- |
| **Search source** | The full, relevant project catalog in the data store (e.g. 2,000+ rows). |
| **Display limit (`MaxResults`)** | How many rows the selector **shows** at once (default **200**). |

**Correct pipeline:**

```plaintext
User types / changes filters
  → query the full project source (SQL + shared parity rules)
  → apply text / status / job type / include-closed filters
  → order (relevance first when searching — see below)
  → Take(MaxResults)
  → bind up to 200 rows to the UI
```

**Wrong pipeline (forbidden):**

```plaintext
Load 200 projects → search only inside those 200
```

**Browse mode (no search text):** show up to `MaxResults` **newest** projects (number descending).
This is a performance default only; it is **not** the search universe.

**Search mode (search text present):** filter against **all** projects in the source, then cap
display. Old projects (e.g. number `1`) must be findable even when they are not in the initial
200-row browse list.

When search text is present and many rows match, ordering uses **relevance rank** before project
number: exact number match on a token ranks above substring matches, so `Take(MaxResults)` does not
drop the intended old project just because newer rows also contain the same digit.

**Status messages** must not imply the UI shows every project. Examples:

- Browse (default cap): `מוצגים עד 200 פרויקטים. הקלד כדי לחפש בכל הפרויקטים.`
- Browse (expanded): `מוצגות תוצאות מורחבות.`
- Search at cap: `מוצגות עד {cap} תוצאות מתאימות. המשך להקליד כדי לצמצם.`

> **Legacy source:** `SiNetProjectManagerV2/Controls/SearchableProjectSelector` already implements this
> behavior (default `FilterProperties = NameAndNumber,Title,Place.Title,Company.Title`, sort by
> `Number` descending, `ExcludedNumbers`, and an `ExternalFilter` predicate for Status/JobType/User).
> The shared component reproduces this behavior over DTOs and Application ports.

---

## 6. Project filters

The selector narrows the project list with three project-scoping filters (parity with the legacy
Email/ProjectWork filter strip):

| Filter | Meaning | Maps to |
| --- | --- | --- |
| **Job Type** | Restrict to a project type / discipline (legacy `JobType` / ProjectType). | `ProjectSearchQuery.JobTypeId` — matches any linked `TypeOfProjectInProject.ProjectTypeId` (legacy parity). |
| **Status** | Restrict by project status/state. | `ProjectSearchQuery.StatusId` — matches `Project.ProjectStatusId`. |
| **User** | Restrict by assigned/responsible user. | `ProjectSearchQuery.AssignedUserId`, `ProjectSummaryDto.AssignedUserName` — *deferred* in the real source: the DTO carries a user *name*, not an id, so `AssignedUserId` is not applied yet (see migration §8a). |
| **Include closed / active** | Include or hide closed projects. | `ProjectSearchQuery.IncludeClosed`, `ProjectSummaryDto.IsActive` |
| **Free-text search** | number / title / city / client (multi-token AND). | `ProjectSearchQuery.SearchText`, `ProjectSummaryQuery.MatchesText` |
| **MaxResults** | Cap **displayed** project rows for responsiveness. | Default **200** (`DefaultMaxResults`); **no cap** (`null`) when `ShowExpandedResults` is checked. Applied **after** filtering on the **full search source** — never before text filters, and never on filter-option lists. |

Filter option lists (`Status`, `Job Type`) are loaded through **`IProjectFilterOptionsService`**
(read-only, **distinct values present on selectable projects**). Selection is stored by stable id
(`SelectedStatusId`, `SelectedJobTypeId`); `null` means **no filter** on that field.

Each combo box prepends a **field-specific default option** (UI labels only — not sent to the DB):

| Combo | Default display | Meaning |
| --- | --- | --- |
| Job Type | **כל הסוגים** | No job-type filter |
| Status | **כל הסטטוסים** | No status filter |

Combo boxes show the default label or the selected value — no separate field labels beside the control.

### SQL vs in-memory filtering (implementation)

Prefer pushing base filters and text tokens to SQL (`IQueryable` → filter → order → `Take` when
browsing). **Relevance ranking** for multi-match search may run in memory on the **already-filtered**
SQL result set (documented performance trade-off in `PROJECT_CONTEXT_MIGRATION.md` §8a). Even when
ranking is in-memory, the filtered set must come from the **full catalog**, not from a pre-capped
browse list.

Not part of the Project Selector (must stay in their owning surface):

- **Gmail content search**, date pickers, pagination, calendar toggle — these are **Email** concerns,
  not project filters (see §9).

---

## 7. Relationship to `WorkSurfaceContext`

**This is the most important rule in this document.**

```
When a Work Surface is opened from a task or workflow,
WorkSurfaceContext.ProjectId is authoritative for that surface.

ICurrentProjectContext is for manual user navigation and shared shell context.

The global Current Project must NOT silently override an explicit WorkSurfaceContext.
```

Details:

- A task-opened surface **does not guess** its project; it uses `WorkSurfaceContext.ProjectId`
  (per `AI_DEVELOPMENT_GUIDE.md` rule 12 and `ARCHITECTURE_TARGET.md` §4).
- The shell **Current Project** is for user-initiated navigation and shared context (e.g. the app
  title). It must not reach into a task-opened surface and change what that surface is working on.
- **Optional, explicit sync only:** if a task-opened surface wants to also update the shell Current
  Project to match its `WorkSurfaceContext.ProjectId`, that is allowed **only** when it is explicit and
  documented at the call site. The reverse (shell Current Project overriding an explicit
  `WorkSurfaceContext`) is **never** allowed.
- Direction of trust: `WorkSurfaceContext` → (optionally) shell Current Project. **Never** shell
  Current Project → `WorkSurfaceContext`.

---

## 8. Relationship to Workflow and Tasks

- Project selection is **read-context only**. It **never** mutates workflow (only
  `IWorkflowCommandService` may start/advance/auto-advance/recover — `ARCHITECTURE_TARGET.md` §4).
- Project selection **never** completes tasks (task completion goes through the task completion
  service, which may call `IWorkflowCommandService.CheckAndAutoAdvanceAsync`).
- Tasks/workflow provide project context to a surface via `WorkSurfaceContext.ProjectId` (§7).
- Task queues / workflow dashboards may **use** the Current Project or the Project Selector to scope
  their reads, but scoping is a query concern — it does not change process state.

---

## 9. Relationship to Email

```
EmailWindowView uses the shared ProjectSelectorView.
EmailWindowViewModel observes ICurrentProjectContext.
Email-specific Gmail search / date / pagination remain in Email.
Project selection / filtering belongs to Shared/Projects.
```

- Email **uses** Project Context but **does not own** it.
- `EmailWindowView` hosts the shared `ProjectSelectorView` (it does not implement its own project
  ComboBox or local-only selection state).
- `EmailWindowViewModel` **observes** `ICurrentProjectContext` (reads `CurrentProject`, subscribes to
  `CurrentProjectChanged`); it does not hold project-selection logic itself.
- Email-specific concerns stay in Email: Gmail content search, date filters, pagination, calendar,
  email list/viewer, move-to-project *action* (the *action* is an Email/ProjectFile concern; the
  *target project* comes from Project Context).
- May remain **fake/in-memory** during the visual-clone slices (no Gmail, no DB), per
  `UI_WINDOW_MIGRATION_MAP.md`.

---

## 10. Relationship to ProjectWork / files

- **ProjectWork** is a Work Surface over a project's file/folder model; it is scoped to a project via
  the shared Project Selector and/or `WorkSurfaceContext.ProjectId`.
- Project selection here is the **same shared mechanism** as everywhere else — ProjectWork must not
  grow its own project selector or duplicate the filter strip.
- Filing / placement / versioning / naming / storage-destination logic lives in
  **`ProjectFileFilingService` / `FileIndex`** domain services, **not** in the screen and **not** in
  the Project Selector (per `MIGRATION_MAP.md` "ProjectWork / Files" and
  `ARCHITECTURE_TARGET.md` §4). ProjectWork **must not** advance workflow directly.

---

## 11. Application title/header behavior

```
CurrentProjectChanged
  → MainWindow / MainShell updates the application title/header
```

- The **shell is the single subscriber** that renders the project into the application title/header
  (e.g. `"{default title} - {CurrentProject.ProjectName}"`, or the default when `null`).
- **Individual feature windows must not update the global title directly.** They change the *Current
  Project*; the shell reacts.

> **Precedent:** `SiNetProjectManagerV2/MainWindow.xaml.cs` already subscribes to
> `ActiveProjectContext.ActiveProjectChanged` and updates its `Title` accordingly. The target keeps
> this exact direction over `ICurrentProjectContext.CurrentProjectChanged`.

> **Implementation status (As-Is):** `ICurrentProjectContext` is registered as a
> **singleton per running app instance** (one DI `ServiceProvider` per process -- see §4 *Scope* and §12)
> shared by all surfaces in that instance. The **live shell** is **`NewShellWindow`** /
> `NewShellViewModel` in `SiNet.App.Wpf`: it owns the OS window title via `NewShellWindowTitle`
> (includes product version + Current Project when selected). `ProjectSelectorView` is **not** in the
> shell header -- Email (and other surfaces that need it) embed the selector. Feature windows do **not**
> set the global title -- they change the Current Project and the shell reacts.
>
> **Historical / V2 reference:** `SiNetProjectManagerV2/MainWindow` previously subscribed to
> `ActiveProjectContext` / `ICurrentProjectContext` for title updates. That path is **not** the
> production desktop host. Title formatting helpers such as `ProjectTitleFormatter` may still exist
> for tests/legacy coexistence; production title composition for App.Wpf is `NewShellWindowTitle`.
>
> **Temporary coexistence (explicit, V2 hybrid only):** the legacy `ActiveProjectContext` handler may
> still exist in V2. Do not treat V2 `MainWindow` as the live production shell.
>
> Scaffold `src/SiNet.App.Wpf/MainWindow` (if present) is **not** the live shell -- `NewShellWindow` is.

---

## 12. Layering and ports

Target **Application ports/DTOs** live in `src/SiNet.Application/Projects/` (runtime-only; no EF
entities, no schema, no migrations):

```
ProjectSummaryDto
ProjectSearchQuery
IProjectQueryService
IProjectDashboardQueryService
ProjectDashboardRowDto
ProjectDashboardQuery
ICurrentProjectContext
ProjectChangedEventArgs
IProjectCreateService / CreateProjectCommand
IProjectUpdateService / ProjectEditDto
IProjectRenameOrchestrator
IProjectGmailLabelSyncService
```

### Project edit + verified rename

See [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md).

- **Open:** double-click in «ריכוז פרויקטים» → edit dialog (`Project.Update`). Toolbar «פתח פרויקט» still opens Project Work.
- **Editable:** place, company, contact, parent, status, `ApproveDescription`, job-type membership, per-type `AdminWorkerId`, per-type `Bid.BidValue`.
- **Immutable:** project number.
- **Rename (dedicated button):** FileServer → ACC Docs folder → Drive root → then DB `Title`. Gmail labels are **not** renamed centrally (per-mailbox; identity = `(Number)` — see Email SoT). **ACC rename is P0** — without it, ACC-mapped projects must not be considered shippable (split-brain risk after FileServer). See plan Layers A–C.
- **Job-type remove:** warn if workflows exist; never hard-delete instances; mark orphan tracks; repair in integrity/Ops (DEV-011).
- **Create parity:** create dialog also captures per-type admin worker + contract value (+ «למי הוגש»).

Intended shapes (defined in detail in the migration plan; summarized here as the target contract):

- **`ProjectSummaryDto`** — `(int ProjectId, string ProjectNumber, string ProjectName, string? PlaceName,
  string? CompanyName, string? JobType, string? Status, string? AssignedUserName, bool IsActive,
  int? StatusId, IReadOnlyList<int>? JobTypeIds)`. The project shape the selector and shell bind to.
  The overview dashboard uses a **separate** `ProjectDashboardRowDto` (see
  [`PROJECTS_DASHBOARD.md`](./PROJECTS_DASHBOARD.md)) so selector contracts stay stable.
- **`ProjectSearchQuery`** — `(string? SearchText, string? JobType, string? Status, int? JobTypeId,
  int? StatusId, int? AssignedUserId, bool IncludeClosed, int? MaxResults)`.
- **`IProjectQueryService`** — `SearchProjectsAsync(ProjectSearchQuery, CancellationToken)` and
  `GetProjectAsync(int projectId, CancellationToken)`; returns DTOs.
- **`ICurrentProjectContext`** — `CurrentProject` (`ProjectSummaryDto?`), `CurrentProjectChanged`
  (`EventHandler<ProjectChangedEventArgs>`), `SetCurrentProjectAsync(ProjectSummaryDto?,
  CancellationToken)`; de-dupes by `ProjectId`, may be `null`.
- **`ProjectChangedEventArgs`** — carries the new `ProjectSummaryDto?`.

**WPF shared component** lives only in:

```
SiNet.App.Wpf/Shared/Projects/
```

**Real DB source** (implemented, read-only) lives behind:

```
SiNet.Infrastructure.Sql   (via IDbContextFactory<>)
```

`SiNetSQL.Services.Projects.ProjectQueryService` is the real read-only `IProjectQueryService`; it maps
the `Project` EF entity to `ProjectSummaryDto` and applies the shared `ProjectSummaryQuery` parity
filters/order. See migration plan **§8a** for the field mapping and the supported-vs-deferred filters.
(The `SiNet.LegacyBridge` adapter path was considered but not used — `Infrastructure.Sql` already owns
the EF model and factory.)

Hard layering rules (from `ARCHITECTURE_TARGET.md` §3 / `AI_DEVELOPMENT_GUIDE.md` §2):

- **No WPF outside `SiNet.App.Wpf`.**
- **No `DbContext` in the UI.** The UI talks to `IProjectQueryService` / `ICurrentProjectContext`;
  `Infrastructure.Sql` uses `IDbContextFactory<>`.
- Ports/DTOs in `SiNet.Application`; implementations in `Infrastructure.*` or `LegacyBridge`;
  registered via a modular `AddSiNet*` extension.

---

## 13. UI component rules

- **One shared selector.** `ProjectSelectorView` / `ProjectSelectorViewModel` is the single project
  selection component; surfaces host it rather than re-implementing selection.
- **DTO binding only.** The selector and consuming VMs bind to `ProjectSummaryDto`, never to EF
  entities.
- **No business logic in code-behind**; the ViewModel stays thin and free of Gmail/ACC/Workflow/Task
  logic.
- **Preserve the legacy UX** (RTL/Hebrew, search fields, sort, filters) — visual-clone first, no
  redesign (per `UI_WINDOW_MIGRATION_MAP.md`).
- **Async + cancellation** for all queries; no sync-over-async.

---

## 14. Guardrails

- **No Email-only project selector.** Project selection is shared (`Shared/Projects`), never local to
  `EmailWindowViewModel`.
- **No hard-coded current project.** Never bake in a project id; `null` is a valid "none" state.
- **No direct `DbContext` in WPF ViewModels.** Go through `IProjectQueryService` /
  `ICurrentProjectContext`.
- **No schema / migration changes** for Project Context (runtime-only). Never hand-edit migrations,
  `*.Designer.cs`, or `ModelSnapshot`.
- **No workflow mutation from project selection.** Selecting a project raises an event only.
- **No copying legacy ViewModels wholesale** (`EmailManagementViewModel`, `ProjectWorkViewModel`, …).
- **No `CurrentProject` overriding an explicit `WorkSurfaceContext`** (§7). Task/workflow context wins
  for a task-opened surface.
- **No WPF types outside `SiNet.App.Wpf`.**

---

## 15. Open questions

These require a decision/approval before or during implementation:

1. **Persistence of Current Project.** Should the last selected project be remembered across app
   restarts? Target default: **runtime-only, not persisted** (§4). Persisting would be a new
   requirement and, if it needs storage, a separate schema/migration decision. Note this is **distinct
   from cross-instance sharing**, which is explicitly out of scope per §4 *Scope*: Current Project is
   per running app instance and is never synchronized across processes.
2. **Selector data source for early slices.** Start with a **fake/in-memory** `IProjectQueryService`
   (recommended for the first vertical slice), then a real `Infrastructure.Sql` implementation vs. a
   `LegacyBridge` adapter delegating to existing legacy project loading — which first?
3. **`ProjectSummaryDto` field coverage.** Are `(number, name, jobType, status, assignedUserName,
   isActive)` sufficient for all consumers (Email header, ProjectWork, Tasks, Workflow dashboard), or
   is additional read-only data (e.g. place/city, company/client) needed on the DTO for display?
4. **User-filter semantics.** Does the "User" filter mean *assigned owner*, *responsible reviewer*, or
   *current user's projects*? Confirm the exact meaning to model `AssignedUserId` correctly.
5. **Explicit context→shell sync policy.** For task-opened surfaces, do we want them to also set the
   shell Current Project (explicitly, per §7), or should the shell title reflect only manual selection?
6. **Multi-project selection.** Some legacy report dialogs select *multiple* projects. Is multi-select
   in scope for the shared component, or does it stay single-select with multi-select handled
   separately?

---

## DB / schema confirmation

This is **target documentation only**. It introduces **no** code, **no** Application ports, **no** UI
component, **no** migrations, **no** `ModelSnapshot`, **no** `*.Designer.cs`, and **no** `DbContext` /
`DbSet` or schema changes. Implementation is sequenced in
[`PROJECT_CONTEXT_MIGRATION.md`](./PROJECT_CONTEXT_MIGRATION.md).
