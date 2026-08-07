# SiNet Target Architecture

> **Status:** Planning / Target State (architecture rules) + Existing (production host = App.Wpf)
> **Date:** 27.06.2026
> **Updated:** 07.08.2026 (As-Is reconciliation -- branch SoT; pre-production claim split)
> **Scope:** Target clean architecture rules and Existing State notes for the production App.Wpf host.
> **Working branches:** `release` + `development` -- see [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §3. `SiWorkNet10` is deprecated (not the working SoT).
> **Frozen reference (do not modify):** `Before_refactoring`
> **New solution:** `SiNet.sln` · **Legacy/functional reference:** `SiNetProjectManager.sln`
> **Reconciliation:** [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md)

This document describes the **target** clean architecture for the SiNet ecosystem and the
rules every new project/file must follow. It is the source of truth for the refactoring;
when code and this document disagree, fix the document first, then the code.

> **Domain target docs:** per-domain target-state documents refine this architecture for a specific
> area. Project domain / Project Context: [`PROJECTS.md`](PROJECTS.md) (implemented by
> [`PROJECT_CONTEXT_MIGRATION.md`](PROJECT_CONTEXT_MIGRATION.md)).

---

## 1. Purpose

### Existing (As-Is, 2026-08)

- Production desktop host is **`SiNet.App.Wpf`** (pilot / cutover path). V2 is not published.
- Live users may already run App.Wpf on pilot machines -- do **not** claim "no live users."
- DB/schema changes remain operator-owned; ACC remains source of truth where applicable.

### Target (architecture direction)

Optimize for a **fast, clean rebuild** (a *Workflow-first fast track*) rather than a long
dual-maintenance strangler forever:

- The existing solution (`SiNetProjectManager.sln` / V2) is a **frozen reference** and behavior source --
  kept for recovery, **not** a second shipped product.
- New, clean code lives in `SiNet.sln` and grows **beside** the old code.
- Functionality is moved behind ports, but in **fast vertical slices**: a screen opens, receives
  context, loads target data, performs one real action, completes a task or advances workflow
  through the official services, and the app still builds.
- Old code is removed once the new path compiles, the UI flow works, tests exist, and
  `MIGRATION_MAP.md` marks the old path as replaced.

### Historical Snapshot

Early foundation rounds (2026-06) described the app as **pre-production / no live users**. That
framing is **Historical** -- it must not be read as the 2026-08 production/pilot As-Is.

> One-line direction:
> **Workflow first. Tasks second. Screens as Work Surfaces. Domain services behind screens.
> Infrastructure at the bottom. Fast vertical slices. No direct workflow mutation from UI.**

See the [`MIGRATION_MAP.md`](MIGRATION_MAP.md) _Refactor Strategy -- Workflow-first Fast Track_
section for the per-domain ledger and the _Next implementation order_.

---

## 2. Solution layout (`SiNet.sln`)

| Project | TFM | Depends on | Responsibility |
| --- | --- | --- | --- |
| `SiNet.Domain` | `net10.0` | — | Entities, value objects, enums, pure domain rules. No framework dependencies. |
| `SiNet.Application` | `net10.0` | Domain | **Ports** (interfaces), application services/use-cases, DTOs. No infrastructure. |
| `SiNet.Infrastructure.Sql` | `net10.0` | Application, Domain | EF Core persistence through `IDbContextFactory<>` over the existing SiNetSQL context (wired in a later round). |
| `SiNet.Infrastructure.Google` | `net10.0` | Application, Domain | Gmail / Google integration. **No WPF.** |
| `SiNet.Infrastructure.Autodesk` | `net10.0` | Application, Domain | ACC / BIM 360 integration. **No WPF.** |
| `SiNet.Infrastructure.FileSystem` | `net10.0` | Application, Domain | File storage and IO. |
| `SiNet.Infrastructure.Logging` | `net10.0` | Application, Domain | Logging implementation (Serilog adapter in a later round). |
| `SiNet.LegacyBridge` | `net10.0` | Application, Domain | Adapters that implement new ports by **delegating to legacy code** during migration. |
| `SiNet.App.Composition` | `net10.0` | Application, Domain, all Infrastructure.*, LegacyBridge | DI **composition root**; aggregates the modular `AddSiNet*` extensions. |
| `SiNet.App.Wpf` | `net10.0-windows` | App.Composition (+ Application, Domain) | WPF host/shell: views & view-models only. **The only project allowed to use WPF types.** |

---

## 3. Dependency rules

Allowed reference direction (left depends on nothing; arrows point to dependencies):

```
Domain  ◄─ Application  ◄─ Infrastructure.* / LegacyBridge  ◄─ App.Composition  ◄─ App.Wpf
```

- `Domain` depends on **nothing**.
- `Application` depends on **Domain only**.
- `Infrastructure.*` and `LegacyBridge` depend on **Application + Domain only**.
- `App.Composition` depends on Application, Domain, every `Infrastructure.*`, and `LegacyBridge`.
- `App.Wpf` depends on `App.Composition` (+ Application/Domain). It is the **only** project that
  may reference WPF/UI types.
- `Infrastructure.*` projects must **not** reference each other.
- Nothing may reference `App.Wpf`.

> **Composition TODOs:**
> ```plaintext
> Consider separating AddSiNetClean() from AddSiNetWithLegacyBridge() so the clean app can eventually run without always registering LegacyBridge.
> ```
> ```plaintext
> Avoid App.Wpf depending directly on infrastructure option types where practical.
> If GmailOptions must be configured by the host temporarily, make the dependency explicit or wrap it in composition-level options.
> ```

---

## 4. Process Control model (Workflow-first)

The runtime is organized around **process control**, not around isolated screens. Layers:

```plaintext
Workflow
  = owns process state, start, advance, auto-advance, stalled recovery, sub-workflows.

Tasks
  = executable work items created by workflow or manually.
  = connect workflow stages to actual work targets.

Work Surfaces
  = UI screens/windows that let the user perform work.
  = examples: Email, Inspection, ProjectWork, Task Panel, Workflow Instance.

Domain Services
  = perform actual business work.
  = examples: EmailContext, InspectionReport, ProjectFileFiling, FileIndex, ACC.

Infrastructure
  = external IO only.
  = examples: Gmail API, Google Drive, ACC, SQL, FileSystem, Logging.
```

### Process-control rule (authoritative)

```plaintext
Only IWorkflowCommandService may start, advance, auto-advance or recover workflow instances.
UI screens and feature ViewModels must not mutate workflow state directly.
Task completion must go through a task completion/coordinator service, which may call IWorkflowCommandService.
```

### Bridge rule

```plaintext
Task completion is the bridge back into workflow.
Feature screens complete work; they do not advance workflow directly.
```

### Workflow boundaries (these already exist)

Workflow is the **process backbone**. Both official boundaries live in `SiNet.Application.Workflow`
and are DTO-clean:

* `IWorkflowQueryService` — reads (definitions, instances, transition history, dashboard snapshots).
* `IWorkflowCommandService` — start / advance / auto-advance / stalled recovery
  (e.g. `CheckAndAutoAdvanceAsync`).

The internal `WorkflowEngine` and task provisioning **may remain entity-based** inside
`SiNet.Infrastructure.Sql`. The boundary returns DTOs; the engine stays an implementation detail.
The next step is **not** merely "extract workflow reads/writes" — it is to make Workflow the central
process backbone used by the new UI shell.

### WorkSurfaceContext (planned, application-level)

```csharp
public sealed record WorkSurfaceContext(
    int? TaskId,
    int ProjectId,
    int? WorkflowInstanceId,
    string ComponentKey,
    int? PrimaryWorkTargetEntityId,
    IReadOnlyList<string> AllowedResultCodes);
```

* A Work Surface should **not guess** how it was opened.
* A screen opened from a task gets an **explicit context**.
* The context tells the screen what **project / task / workflow / work target** it operates on.
* **Inspection** should receive report target context.
* **Email** should receive email/task/project context.
* **ProjectWork** should receive project/file context.
* The context is **runtime-only** unless a real persistence requirement appears later.

### Task ports (planned, Application-level)

```csharp
ITaskQueryService
ITaskCommandService
ITaskNavigationService
ITaskCompletionService
```

```plaintext
ITaskQueryService
- Load task queues.
- Load project tasks.
- Load task detail.

ITaskCommandService
- Create/update/close tasks when not directly part of completion workflow.

ITaskNavigationService
- Convert taskId into WorkSurfaceContext / navigation target.
- Decide which screen/component should open.
- UI must not bypass this resolver.

ITaskCompletionService
- Complete task work targets.
- Record task result.
- Close task according to policy.
- Call IWorkflowCommandService.CheckAndAutoAdvanceAsync when appropriate.
```

---

## 5. Hard conventions

1. **File size:** no new file above **~600 lines**. If it grows, split by responsibility.
2. **No WPF outside `App.Wpf`:** connectors/infrastructure return primitives (e.g. a hex
   `string` color), never `Brush`/`DependencyObject`/etc.
3. **No `DbContext` in UI:** the UI talks to Application ports. `Infrastructure.Sql` uses
   `IDbContextFactory<>`.
4. **Everything external behind an interface:** Google, Autodesk, SQL, and FileSystem are
   reached only through ports defined in `SiNet.Application`.
5. **Modular DI:** each module exposes `AddSiNet…(this IServiceCollection)`; the composition
   root aggregates them. No monolithic `ConfigureServices`.
6. **Async & cancellation:** all IO is async; propagate `CancellationToken`; no
   sync-over-async (`.GetAwaiter().GetResult()`).
7. **No business logic in code-behind** or oversized view-models.
8. **No MediatR and no full Repository pattern** unless explicitly requested.
9. **EF migrations:** never hand-edit migrations, `*.Designer.cs`, or `ModelSnapshot`. Change
   model/configuration only and report when `Add-Migration`/`Remove-Migration` is required.

---

## 6. Refactor workflow (per slice)

1. Define/confirm the ports in `SiNet.Application`.
2. Implement them in the right `Infrastructure.*` **or** add a `SiNet.LegacyBridge` adapter that
   delegates to the existing service.
3. Register via the module's `AddSiNet*` extension.
4. Point new consumers at the port; make the slice **vertical** (screen opens → receives
   `WorkSurfaceContext` → loads target data → performs one real action → completes task / advances
   workflow through the official services).
5. `dotnet build SiNet.sln`; run and extend tests.
6. Update `MIGRATION_MAP.md`; retire the old path only when its status is **✅ Replaced**.

**Next implementation order (Workflow-first):**

```plaintext
1. Update migration/architecture docs with Workflow-first model.
2. Make sure IWorkflowQueryService and IWorkflowCommandService are the official workflow boundaries.
3. Define WorkSurfaceContext.
4. Define task navigation/completion ports.
5. Build/adjust shell navigation so screens are work surfaces.
6. Connect Workflow screens first (dashboard, instance detail, task-created flows, advance/pause/resume/complete/cancel).
7. Connect Task navigation (task opens correct work surface; work surface receives context).
8. Connect Inspection task mode.
9. Connect Email action execution to Workflow/Task services.
10. Connect ProjectWork/File filing to task/workflow completion where relevant.
11. Only after the backbone works, continue filling screen-specific features.
```

> Email/Google is already migrated to native infrastructure
> (`SiNet.Infrastructure.Google` → Gmail API, no `LegacyBridge`). Note that native Gmail **read**
> plumbing is **not** the same as the full Email Management Work Surface — see the
> _Email Management — Workflow/Task Integration Plan_ in `MIGRATION_MAP.md`.

---

## 7. Build & guardrails

- Verify the new solution with `dotnet build SiNet.sln`.
- During the Foundation Round do **not** modify: `SiNetProjectManager.sln`, legacy projects,
  EF migrations, `DbContext`, `ModelSnapshot`, `*.Designer.cs`, or existing WPF screens.
- Recover any old code from the frozen `Before_refactoring` branch.
