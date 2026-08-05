# Projects Overview Dashboard — ריכוז פרויקטים

> **Status:** Approved target — MVP (2026-08-01)  
> **Related:** [`PROJECTS.md`](./PROJECTS.md), [`APP_SHELL.md`](./APP_SHELL.md),  
> [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md), [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md)

## 1. Purpose

The **Projects Overview Dashboard** («ריכוז פרויקטים») is the New System **business overview** of
all projects. It answers in seconds:

1. Which projects exist, and in which **lifecycle status** (`ProjectStatus`)?
2. Which **job types** (project types) are linked to each project?
3. Which projects have **open workflows** (Active/Paused), and at which stage?
4. How many **open tasks** does each project carry?
5. How do projects look when filtered by place, dates, status, type, or open work?

It **complements** the shared Project Selector (pick one project) and the Workflow Ops Dashboard
(instance health). It does **not** replace either.

## 2. Audience and permissions (MVP)

| Audience | Access |
| --- | --- |
| Management and above | Feature `Shell.OpenProjectsDashboard` (min role **Management**, same bar as `Project.Create`) |
| Employee | No menu item |
| Administrator | Included via Management+ hierarchy |

## 3. Shell placement

- Menu group: **«פרויקטים ותבניות»**
- Menu item: **«ריכוז פרויקטים»**
- Host: floating `ShowWindow` (same pattern as Workflow Ops), not in-shell content

## 4. MVP screen structure

### 4.1 Summary cards (KPI)

Computed from the **currently filtered** row set:

| Card | Meaning |
| --- | --- |
| סה״כ מוצגים | Count of filtered rows |
| פעילים | `IsActive == true` |
| סגורים | `IsActive == false` (only meaningful when include-closed is on) |
| עם תהליך פתוח | `OpenWorkflowCount > 0` |
| ללא תהליך | `OpenWorkflowCount == 0` |
| משימות פתוחות | Sum of `OpenTaskCount` |
| רענון אחרון | Wall clock of last successful load |

### 4.2 Filters

| Filter | Behavior |
| --- | --- |
| Free text | Number / name / place / company / assigned worker |
| Status | By `StatusId` (from `IProjectFilterOptionsService`) |
| Job type | By linked `JobTypeId` |
| Place | By `PlaceName` (distinct from loaded rows + catalog when available) |
| Start date From–To | Inclusive on `Start` (date part) |
| Created date From–To | Inclusive on `Created` (date part) |
| Include closed | Re-queries the port with `IncludeClosed` |
| Only with open workflow | Client filter `OpenWorkflowCount > 0` |
| Only with open tasks | Client filter `OpenTaskCount > 0` |

Sorting: built-in DataGrid column sort (place, status, dates, counts, …).

### 4.3 Grid columns

| Column | Source |
| --- | --- |
| מספר | `ProjectNumber` |
| שם | `ProjectName` |
| מקום | `PlaceName` |
| לקוח | `CompanyName` |
| סוגי פרויקט | All linked JobType titles (joined) |
| סטטוס | `ProjectStatus.Title` (business lifecycle stage) |
| אחראי | `Worker` / `AssignedUserName` |
| תהליכים פתוחים | Count + summary (`DefinitionName — StageName`, Active/Paused, project-bound) |
| משימות פתוחות | Count of assignments whose status `IsOpen` |
| התחלה | `Start` |
| סיום | `End` |
| נוצר | `Created` |
| פעיל | Yes/No from `IsActive` (`EndOfProject != true`) |

**Important distinction:** grid **סטטוס** = project lifecycle (`ProjectStatus`). Workflow stage appears
only under **תהליכים פתוחים**.

### 4.4 Drill-down

| Action | Behavior |
| --- | --- |
| **Double-click** row | Open **עדכון פרויקט** (`ProjectEdit` dialog) for that project — see [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) |
| Toolbar **«פתח פרויקט»** | Set `ICurrentProjectContext` from the row + open Project Work browse (`IProjectWorkSurfaceHost.TryOpenBrowseAsync`) |

No workflow mutation and no status edits **inside the grid**. Metadata edits happen only in the edit dialog (feature `Project.Update`).

## 5. Data sources (no parallel stack)

| Need | Port / source |
| --- | --- |
| Project rows + dates + types + status | `IProjectDashboardQueryService.GetRowsAsync` (new; DB read-only) |
| Status / JobType filter options | `IProjectFilterOptionsService` (existing) |
| Open workflows | Aggregated from `WorkflowInstance` where `Status` ∈ {Active, Paused} and `IsProjectBound` |
| Open tasks | Aggregated from `ProjectAssignment` where `AssignmentStatus.IsOpen` |
| Current project / Project Work | Existing `ICurrentProjectContext` + `IProjectWorkSurfaceHost` |

**Source of truth:** SQL `Project` / `ProjectStatus` / `JobType` / `Place`. Not ACC attributes.

Do **not** use `GetAllProjectWorkflowSnapshotsAsync` for the grid load (too heavy — full stage graphs).

Do **not** extend `ProjectSummaryDto` (selector contract stays unchanged).

## 6. Application contracts

```
ProjectDashboardRowDto
ProjectDashboardQuery
IProjectDashboardQueryService
```

- `ProjectDashboardQuery` MVP: `(bool IncludeClosed = false)`.
- UI applies the remaining filters client-side after one load (re-load when `IncludeClosed` changes).
- WPF binds DTOs / row VMs only — never EF entities.

## 7. Out of MVP

- Email / ACC backlog columns
- Excel / Sheets export
- Editing project status **from the grid** (edit dialog is in scope — see §4.4)
- Auto-refresh / realtime
- Server-side paging
- Assigned-user id filter (Worker remains a display string)

## 8. Guardrails

- Grid load path stays read-only (no `SaveChanges` / workflow commands from the dashboard query).
- Writes go through `IProjectUpdateService` / `IProjectRenameOrchestrator` opened from the edit dialog — not inline grid editors.
- No duplication of the Project Selector.
- No duplication of Workflow Ops (instance-centric health stays there).
- Feature access deny-by-default via `AppFeatureCodes` + `AppFeatureAuthorization`.
