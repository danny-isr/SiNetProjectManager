# Dev Tools — Reset & Seed (New System)

> **DEBUG-only.** Development database operations for local testing. Not for production.

## Overview

Dev reset and workflow/task seed were migrated from legacy `SiNetSQL.Services` to the New System stack:

| Capability | Application port | Infrastructure implementation |
| --- | --- | --- |
| Dev data reset | `IDevDataResetService` | `SqlDevDataResetService` |
| Static / workflow / demo seed | `IStaticSeedService` | `SqlStaticSeedService` (+ internal helpers) |

**Legacy implementations remain** in `SiNetSQL` (`DevDataResetService`, `WorkflowSeedService`, `TaskManagementSeedService`) and the V2 `MainWindow` dev menu — marked legacy, not deleted.

## NewShell entry (DEBUG only)

Menu items (Management role + Windows user allow-list):

1. **כלי פיתוח — איפוס נתוני פיתוח** — wipe migration tables + re-seed
2. **כלי פיתוח — טעינת Seed בסיסי** — static lookups + mappings + workflow
3. **כלי פיתוח — טעינת משימות דemo** — demo tasks for Task Panel read-only

Wired via `DevToolsCoordinator` → Application ports only. **Does not** call `SiNetSQL.Services.DevDataResetService` or open legacy `MainWindow`.

## What reset deletes

All migration-introduced tables (email, ACC, tasks, workflow, inspection, decisions, etc.). **Legacy baseline tables are not touched.**

Default preservation:

| Option | Default | Effect |
| --- | --- | --- |
| `PreserveSystemSettings` | `true` | Keeps `SystemSettings` |
| `ResetUserSettings` | `false` | Keeps `UserGroups`, memberships, `UserSetting`, `UserStatusPreference` |

Mechanics: disable FK constraints → `DELETE` (not TRUNCATE) → reseed identity → re-enable FK constraints. Missing tables are skipped silently.

## Seed order (after reset or manual seed)

1. Task static lookups (`SqlTaskManagementSeedService.EnsureStaticLookupDataAsync`)
2. ProjectType mappings (`ResetMappingsToDefaults`)
3. Workflow definitions + user groups + activations (`SqlWorkflowSeedService.SeedAllAsync`)
4. Optional demo tasks (`SqlTaskDemoSeedService`)

## Demo tasks

Prefix: `DEBUG_TASK_SEED` in task title.

- 3 Quick + 3 Medium + 3 Long open tasks with priorities 1–3
- 1 closed task (non-open queue)
- 1 open task without `WorkPriority`
- 1 resolve candidate (`FileInitialInquiry` task type) when static seed ran first

Idempotent: existing titles with the prefix are not duplicated.

Demo tasks are deleted when dev reset wipes `ProjectAssignment`.

## Registration

```csharp
services.AddSiNetDevTools(); // in AddSiNet() and AddSiNetNewSystemGraph()
```

Release builds register fail-closed stubs.

## Authorization

- `#if DEBUG` compile gate on destructive paths
- Windows user allow-list (same as legacy)
- `AppFeatureCodes.DevToolsReset` / `DevToolsSeed` — minimum role **Management**

## Database

This tooling assumes a **development DB**. Do not run against production.

## Related docs

- [`PROCESS_BACKBONE_FOUNDATION.md`](./PROCESS_BACKBONE_FOUNDATION.md)
- [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md)
