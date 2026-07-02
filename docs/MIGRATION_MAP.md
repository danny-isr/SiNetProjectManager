# SiNet Migration Map

> **Working branch:** `SiWorkNet10` · **Frozen reference (recover old code here):** `Before_refactoring`
> Tracks the move from the legacy `SiNetProjectManager.sln` into the clean `SiNet.sln`.

**Status legend:** ⬜ Not started · 🟡 In progress · ✅ Replaced (old path retired) · 🔒 Frozen reference

A legacy path may be retired **only** when: the new path compiles, the UI flow works, tests
exist/updated, and its row below is marked **✅ Replaced**.

> **Companion architecture docs:**
> [`ARCHITECTURE_TARGET.md`](ARCHITECTURE_TARGET.md) (process-control model, ports, dependency rules) ·
> [`AI_DEVELOPMENT_GUIDE.md`](AI_DEVELOPMENT_GUIDE.md) (coding rules for the refactor).
>
> **Domain target docs:** [`PROJECTS.md`](PROJECTS.md) (Project domain / Project Context target state,
> implemented by [`PROJECT_CONTEXT_MIGRATION.md`](PROJECT_CONTEXT_MIGRATION.md)).

---

## Refactor Strategy — Workflow-first Fast Track

This refactor is **not** a long-lived strangler with two actively maintained systems. The legacy
app is **not** the active production path during the refactor, so we optimize for a **fast, clean**
rebuild and keep the old code as a **frozen reference**, not a second system to maintain
indefinitely.

**Order of attack:**

* Do **not** start by fully rebuilding each screen independently.
* First architectural priority: make **Workflow** and **Task** the **backbone** of the application.
* Move/create the UI shell and screens as **Work Surfaces**.
* After the shell/screens exist, **migrate the Workflow flow first**.
* Then connect **Email**, **Inspection**, **ProjectWork**, **Files** and **ACC** onto the
  Workflow/Task backbone.
* The old legacy code stays a **frozen reference** and source of behavior, **not** a second active
  system to maintain indefinitely.
* Prefer **fast vertical slices** over slow read-only-only migration when the behavior is needed for
  the process.

> One-line direction:
> **Workflow first. Tasks second. Screens as Work Surfaces. Domain services behind screens.
> Infrastructure at the bottom. Fast vertical slices. No direct workflow mutation from UI.**

### Process Control model

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

**Process-control rule (authoritative):**

```plaintext
Only IWorkflowCommandService may start, advance, auto-advance or recover workflow instances.
UI screens and feature ViewModels must not mutate workflow state directly.
Task completion must go through a task completion/coordinator service, which may call IWorkflowCommandService.
```

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

**Bridge rule:**

```plaintext
Task completion is the bridge back into workflow.
Feature screens complete work; they do not advance workflow directly.
```

### Next implementation order

```plaintext
1. Update migration/architecture docs with Workflow-first model.
2. Make sure IWorkflowQueryService and IWorkflowCommandService are the official workflow boundaries.
3. Define WorkSurfaceContext.
4. Define task navigation/completion ports.
5. Build/adjust shell navigation so screens are work surfaces.
6. Connect Workflow screens first:
   - dashboard
   - instance detail
   - task-created workflow flows
   - advance / pause / resume / complete / cancel
7. Connect Task navigation:
   - task opens correct work surface.
   - work surface receives context.
8. Connect Inspection task mode.
9. Connect Email action execution to Workflow/Task services.
10. Connect ProjectWork/File filing to task/workflow completion where relevant.
11. Only after the backbone works, continue filling screen-specific features.
```

### Speed requirement

```plaintext
Because this refactor is intended to finish quickly, prefer vertical slices over long parallel read-only scaffolding.

A vertical slice is considered useful when:
- screen opens,
- receives context,
- loads target data,
- performs one real action,
- completes task or updates workflow through the official services,
- app still builds.
```

Avoid:

```plaintext
- rebuilding every screen fully before connecting workflow,
- keeping two active implementations indefinitely,
- copying legacy ViewModels wholesale,
- moving code without establishing boundaries,
- allowing screens to directly control workflow state.
```

---

## Domains

| Domain | Legacy source (approx. size) | New home | Status | Notes |
| --- | --- | --- | --- | --- |
| Email / Google | `SiOffice.GoogleConnector/GoogleService.cs` (~3,369), `EmailManagementViewModel.cs` (~6,909) | `SiNet.Application` `IEmail*` ports → `SiNet.Infrastructure.Google` (**native Gmail API**) | 🟢 | **Accepted; native sign-in + real config done** (no `LegacyBridge`). Read-only; deferred: send/modify only. **Google/Gmail consolidation: blocked until capability and token-store parity are explicitly designed** — legacy `GoogleService` (Gmail+Sheets+Drive, shared token store) and native `GmailClientProvider` (Gmail-only, separate token store) are **not** behavior-equivalent; do not consolidate inside `SiNetProjectManagerV2`. See sections below. |
| Workflow | Legacy Workflow windows, `WorkflowTaskOrchestrator`, `WorkflowEngine`, task provisioning | `SiNet.Application.Workflow` `IWorkflowQueryService` + `IWorkflowCommandService`; SQL implementation keeps engine/provisioning internal | 🟡 | **Workflow is the process backbone.** Both boundaries exist and are DTO-clean: `IWorkflowQueryService` (reads) and `IWorkflowCommandService` (start / advance / auto-advance / stalled recovery). Internal `WorkflowEngine` + provisioning stay entity-based inside SQL/infrastructure. Next work should **connect new screens to workflow/task control** rather than rebuilding screens as isolated modules. See _Refactor Strategy_ + the Workflow sections below. |
| ACC / Autodesk | `SiOffice.AutodeskConnector/Bim360Service.cs` (~3,231) | `SiNet.Application` `IAcc*` ports → `SiNet.Infrastructure.Autodesk` (+ `LegacyBridge`) | ⬜ | ACC remains source of truth; DB is cache/helper. |
| SQL / DbContext | `SiNetSQL` (`DbContext` ~1,948) | `SiNet.Infrastructure.Sql` via `IDbContextFactory<>` | 🟡 | **EF layer extracted** (models, configs, DbContext, factory, migrations) into clean `net10.0` module; legacy `SiNetSQL` references it. **Do not** edit migrations / `ModelSnapshot` / `*.Designer.cs`. See section below. |
| App startup / DI | `App.xaml.cs` (~1,821), `ConfigureServices()` (~700) | `SiNet.App.Composition` + `SiNet.App.Wpf` | 🟡 | **Phases 1–2 + SQL gate done.** Host delegates the Workflow **read slice**, **command port**, and now the **SQL `DbContextFactory`** (via `AddSiNetSql` + `SiNetSqlOptions` DEBUG-diagnostics opt-in) to the modular stack. Remaining host-specific: FileSystem/Logging (no host consumer) and Google (native Gmail auth). See D1/D2/D3 below. |
| File system | `FileHelpers` / scattered IO | `SiNet.Application` `IFileStorage` → `SiNet.Infrastructure.FileSystem` | ⬜ | |
| Logging | scattered Serilog usage | `SiNet.Application` `IAppLogger` → `SiNet.Infrastructure.Logging` | ⬜ | |
| Identity / Permissions | `CurrentUserContext`, `UserService`, `ActionPermissionService`, `MainWindow` menu gates | `SiNet.Application.Identity` ports → host adapters | 🟡 | **Target spec:** [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md). P1–P6 **implemented** + User Management menu (legacy window via factory). `AddUserWindow` still legacy-only entry. |

---

## Foundation round (this change)

| Deliverable | Status |
| --- | --- |
| Architecture docs (`ARCHITECTURE_TARGET`, `MIGRATION_MAP`, `AI_DEVELOPMENT_GUIDE`) | ✅ |
| `SiNet.sln` + 10 project skeletons under `src/` | ✅ |
| `SiNet.Domain` seed types | ✅ |
| `SiNet.Application` seed port interfaces | ✅ |
| `Infrastructure.*` modular DI skeletons | ✅ |
| `SiNet.App.Composition` aggregate DI extension | ✅ |
| One `SiNet.LegacyBridge` adapter stub | ✅ |
| `SiNet.App.Wpf` host scaffold | ✅ |
| `dotnet build SiNet.sln` green | ✅ _0 errors, 0 warnings (build succeeded in ~3.6s)_ |

> No legacy domain has been migrated yet. The Foundation Round only establishes the new
> structure beside the old one. `SiNetProjectManager.sln` is unchanged and fully functional.

---

## Email / Google slice — migrated to native infrastructure ✅

> **Email/Google native infrastructure: accepted foundation state.**
> Reviewed and accepted on branch `SiWorkNet10` as the current target state for this slice.
> The legacy-backed Round 1 plan is retired and must not be reintroduced (`LegacyEmailGatewayAdapter`
> removed; no `SiNet.LegacyBridge` reference in `SiNet.Infrastructure.Google`).

The Email/Google slice now runs entirely on the new stack with a **native Gmail
implementation**. `SiNet.Infrastructure.Google` talks to the Gmail API directly; there is no
longer any dependency on the legacy `GoogleService` or on `SiNet.LegacyBridge`.

Active flow: `SiNet.App.Wpf` `InboxViewModel` → `IEmailGateway` → `GmailEmailGateway`
→ `GmailClientProvider` (OAuth / silent token restore) → Gmail API.

| Deliverable | Status |
| --- | --- |
| `IEmailGateway` shape `GetProjectEmailsAsync(location, project)` / `GetByIdAsync` | ✅ |
| `EmailAddress.TryParse` / `CreateOrFallback` (non-throwing, `Unknown` fallback) | ✅ |
| Native `GmailEmailGateway` (Gmail API → `EmailSummary`, label-scoped per project) | ✅ |
| `GmailClientProvider` (silent token restore; returns `null` when not signed in, never throws) | ✅ |
| `GmailOptions` config model (client secrets path, token store, app name, scopes, interactive flag) | ✅ |
| `AddSiNetGoogle()` / `AddSiNetGoogle(Action<GmailOptions>)` register `IEmailGateway` → native gateway | ✅ |
| `SiNet.App.Composition` `AddSiNet(Action<GmailOptions>)` host configuration overload | ✅ |
| `SiNet.App.Wpf` host configures `GmailOptions` (env-var secrets/token store, no legacy coupling) | ✅ |
| `SiNet.App.Wpf` inbox consumer (VM + `DataGrid`, empty/not-signed-in handled) | ✅ |
| `dotnet build SiNet.sln` green + legacy WPF host green (full MSBuild) | ✅ _0/0 both_ |

> **No strangler debt remains for this slice.** `SiNet.Infrastructure.Google` references only
> `SiNet.Application`, `SiNet.Domain`, and the Gmail NuGet packages.

### Bridge cleanup (this round)

The legacy-backed bridge path is no longer the active implementation and was cleaned up:

| Bridge file | Disposition |
| --- | --- |
| `SiNet.LegacyBridge/Email/LegacyEmailGatewayAdapter.cs` | **Removed** — orphaned; not referenced or wired anywhere. |
| `SiNet.LegacyBridge/Email/ILegacyEmailSource.cs` | **Kept (inactive)** — still consumed by the legacy host `SiNetProjectManagerV2`; doc-comment marked INACTIVE in the new stack. |
| `SiNet.LegacyBridge/Email/LegacyEmailDto.cs` | **Kept (inactive)** — same; legacy-host-only. |
| `AddSiNetLegacyBridge()` | No-op; comment corrected (Email/Google is native, bridge wires nothing). |

> The `ILegacyEmailSource` seam stays only because the **legacy** WPF host still binds it to its
> authenticated `GoogleService`. It is intentionally **not** registered in the new composition
> root and must not become the active `IEmailGateway` again. Remove it when the legacy host is
> retired.

### Deferred gaps — status

This slice was accepted as a **foundation state** with five deferred gaps. Gaps #1, #2, and #5 were
closed in the *Interactive sign-in + real config* round (below). Gaps #3 and #4 remain deferred and
must not be addressed unless separately requested.

| # | Gap | Status |
| --- | --- | --- |
| 1 | Native interactive Gmail sign-in | ✅ Closed — `GmailClientProvider.SignInInteractiveAsync` + "Connect Google" UI |
| 2 | Real Gmail configuration source (was env-var scaffold) | ✅ Closed — `appsettings.json` (`Gmail` section) bound to `GmailOptions`; env vars override |
| 3 | Native gateway is read-only | ⬜ Deferred (out of scope) |
| 4 | Send/modify Gmail capabilities | ⬜ Deferred (out of scope) |
| 5 | App-startup first-time auth / token acquisition | ✅ Closed — startup silent restore (`TrySignInSilentlyAsync`) + on-demand interactive sign-in |

---

## Email / Google — Interactive sign-in + real config (round 2) ✅

Builds on the accepted native foundation. **Read-only scope unchanged** (`GmailReadonly`); no
send/modify added.

Active flow (sign-in): `SiNet.App.Wpf` "Connect Google" → `InboxViewModel.ConnectCommand`
→ `GmailClientProvider.SignInInteractiveAsync` → Gmail OAuth consent → token store.
At startup the host attempts `TrySignInSilentlyAsync` (no browser) to restore a prior session.

| Deliverable | Status |
| --- | --- |
| `GmailClientProvider.TrySignInSilentlyAsync` (no-browser startup restore) | ✅ |
| `GmailClientProvider.SignInInteractiveAsync` (explicit consent; forces browser when no token) | ✅ |
| `GmailSignInResult` (`Success` / `NotConfigured` / `Failed`) + `IsSignedIn` status signal | ✅ |
| Real config source: `src/SiNet.App.Wpf/appsettings.json` `Gmail` section → `GmailOptions` | ✅ |
| `Microsoft.Extensions.Configuration.*` (Json + EnvironmentVariables + Binder) in WPF host | ✅ |
| Env-var override of file config retained (`SINET_GOOGLE_*`) for back-compat | ✅ |
| Host startup: bind config, register `IConfiguration`, attempt silent sign-in off UI thread | ✅ |
| `InboxViewModel` "Connect Google" command + status; `MainWindow.xaml` button | ✅ |
| `dotnet build SiNet.sln` green; `appsettings.json` copied to output | ✅ _0/0_ |

> Still independent of legacy `AppConfiguration`. `SiNet.Infrastructure.Google` references only
> `SiNet.Application`, `SiNet.Domain`, and Gmail NuGet packages — no `LegacyBridge`.

---

## SQL / DbContext slice — EF layer extracted to clean module 🟡

> **Strategy A1 + Option C.** The real EF layer was *extracted, not redesigned*, out of the mixed
> legacy `SiNetSQL` (app + EF + Windows/COM/WPF) into the clean `SiNet.Infrastructure.Sql`
> (`net10.0`). Legacy `SiNetSQL` now **references** the clean module; namespaces (`SiNetSQL.Models`,
> `SiNetSQL.Data`, `SiNetSQL.Migrations`) were **preserved** so call sites and migration identity
> are unchanged. No schema change; no migration/`ModelSnapshot`/`*.Designer.cs` edits.

**Moved verbatim into `src/SiNet.Infrastructure.Sql` (namespaces preserved):**

| Artifact | Detail |
| --- | --- |
| `Models/*` | 106 entity POCOs + enums (+ later `ProjectFile.TagDisplay`, `TypeOfProjectInProject` helpers) |
| `Data/Configurations/*` | 21 EF Fluent configurations |
| `Data/SiNetSQLDbContext.cs` (+ `.EmailInbox` partial) | DbContext core + `OnModelCreatingPartial` wiring; `OnCreated()` calls removed from ctors |
| `Data/SiNetSQLDbContextFactory.cs` | Existing `IDbContextFactory<SiNetSQLDbContext>` (used by ViewModels) |
| `Migrations/*` + `SiNetSQLDbContextModelSnapshot.cs` | 168 `.cs`, moved **unchanged** |
| `SqlServiceCollectionExtensions.cs` | New `AddSiNetSql(connectionString)` registers the factory with `UseCompatibilityLevel(120)` (mirrors legacy `OnConfiguring`) |

**Contamination kept out of the clean module (Windows / COM / impersonation):**

| Behavior | Disposition |
| --- | --- |
| `Project.ProjectFullPhate` (mapped drive `U:\` + `ImpersonationFolderEdit.MAPDrive` + `FixDirectoryName()`) | **Re-homed legacy-side** as `SiNetSQL.Services.Files.IProjectPathBuilder` / `ProjectPathBuilder` + `ProjectPathExtensions.GetProjectFullPath()`. Formula ported **exactly** (same null handling, prefix, path format). All **13** call sites rewired. |
| File-extension policy (`AvailableExtensions`, `CheckallowedExtension`, `CheckNotSetToReadOnlyExtension` + allow-list) | **Pure data — kept on the clean `SiNetSQLDbContext`** (`SiNetSQLDbContext.FileExtensionPolicy.cs`), allow-list ported verbatim, **zero** Windows dependency. 2 call sites compile unchanged. |

**Deferred — legacy authorization behavior (intentionally removed, not re-homed):**

> `CheckUser(SiUserGrop)`, the hardcoded `Admin` / `Maskrot` / `Worker` user-group `SortedList`s, and
> the `WindowsIdentity.GetCurrent()` lookup (formerly in `My Partial Class/SiNetSQLDbContext.cs`)
> were **deleted with the contaminated partial**. Verified **0 real callers** in either repo.
> No replacement service was created (per approved decision: *do not keep dead compatibility APIs*).
> The DbContext `ProjectDirectory` and `PreSelectItemName` passthroughs were likewise dead (0 callers)
> and removed. If a real caller surfaces later, re-home authorization explicitly (DB-driven
> `AppUserRole` / `CurrentUserContext`) rather than restoring the `WindowsIdentity` lists.

| Deliverable | Status |
| --- | --- |
| EF packages added to clean module (EF Core / SqlServer / Design / Tools 10.0.7) | ✅ |
| Models / Configurations / DbContext / Factory / Migrations moved (namespaces preserved) | ✅ |
| `AddSiNetSql(connectionString)` factory registration (compat level 120) | ✅ |
| `IProjectPathBuilder` + 13 `ProjectFullPhate` call sites rewired (legacy-side) | ✅ |
| Clean `FileExtensionPolicy` members (2 call sites unchanged) | ✅ |
| Contaminated partials deleted; `CheckUser`/WindowsIdentity deferred (0 callers) | ✅ |
| `SiNetSQL.csproj` references clean module; `DI.Abstractions` aligned 10.0.7 → 10.0.9 | ✅ |
| `dotnet build SiNet.sln` green; `SiNetSQL.csproj` green (VS MSBuild) | ✅ _0/0_ |
| Clean module stays `net10.0`, no Windows/COM/WPF leakage | ✅ |

> `SiNet.Infrastructure.Sql` references only `SiNet.Application`, `SiNet.Domain`, and EF NuGet
> packages. The legacy project (which still has the `IWshRuntimeLibrary` COM reference) must be
> built with **VS MSBuild** — `dotnet build` cannot resolve COM (`MSB4803`). This is a pre-existing
> legacy constraint, unaffected by the extraction.

---

## Workflow slice — read services extracted to clean module 🟡

> **Reads-first, writes-deferred.** Only the two **read-only** workflow services were relocated this
> round. The write/engine layer (orchestrator, executor, transition evaluator, seed, validation,
> watchdog, etc.) stays in legacy `SiNetSQL.Services.Workflow` and is untouched.

**Why `SiNet.Infrastructure.Sql` (not `SiNet.LegacyBridge` or `SiNet.Application`):**

> The services need `IDbContextFactory<SiNetSQLDbContext>` (EF, now in the clean SQL module).
> `SiNet.LegacyBridge` (`net10.0`) **cannot** reference legacy `SiNetSQL` (`net10.0-windows` + COM)
> without contaminating the clean graph. Exposing the EF entities from a port in `SiNet.Application`
> would force an illegal `Application → Infrastructure` dependency. So the ports are **co-located**
> in `SiNet.Infrastructure.Sql` for this transitional round (**Option Y**), and the EF entities are
> **temporarily** allowed in the port signatures (to be DTO-cleaned in a later round).

**Moved into `src/SiNet.Infrastructure.Sql/Services/Workflow` (namespace `SiNetSQL.Services.Workflow` preserved):**

| Artifact | Detail |
| --- | --- |
| `WorkflowQueryService.cs` | Read-only queries (definitions, instances, transition history, dashboard snapshots). 9 methods + `ProjectWorkflowSnapshot` / `WorkflowInstanceSnapshot` DTOs. |
| `ProjectWorkflowPolicyService.cs` | Read-only allowed-workflow resolution by ProjectType (open-policy fallbacks). 3 methods. |
| `IWorkflowQueryService.cs` *(new)* | Co-located read port for the query service. |
| `IProjectWorkflowPolicyService.cs` *(new)* | Co-located read port for the policy service. |
| `WorkflowServiceCollectionExtensions.cs` *(new)* | `AddSiNetWorkflowReads()` registers both services as concrete + port (forwarded). |

**Wiring:**

| Change | Detail |
| --- | --- |
| `SiNet.App.Composition` | `AddSiNetWorkflowReads()` aggregated after `AddSiNetSql()`. |
| Legacy `App.xaml.cs` | Existing concrete `AddTransient` registrations resolve to the moved types automatically (namespace preserved, transitive ref `SiNetSQL → SiNet.Infrastructure.Sql`). Added interface forwarders for both ports. |
| `WorkflowManagementWindow.xaml.cs` | Dashboard read consumers re-pointed to `IWorkflowQueryService` / `IProjectWorkflowPolicyService`. Write-path `WorkflowTaskOrchestrator` left unchanged. |

**Deferred (not in this round):** all workflow writes/engine, Builder-tab inline `DbContext` reads,
DTO cleanup (remove EF entities from port signatures), UI/VM migration to `SiNet.App.Wpf`.

| Deliverable | Status |
| --- | --- |
| Read services + snapshot DTOs moved (namespaces preserved) | ✅ |
| `IWorkflowQueryService` / `IProjectWorkflowPolicyService` ports defined (co-located) | ✅ |
| `AddSiNetWorkflowReads()` DI + composition-root aggregation | ✅ |
| Legacy host port forwarders + Dashboard read consumers re-pointed | ✅ |
| Focused tests (9) in `SiNetSQL.Tests/Services/Workflow` | ✅ _9/9_ |
| Existing workflow regression tests | ✅ _25/25_ |
| `dotnet build SiNet.sln` green | ✅ _0/0_ |
| Clean module stays `net10.0`, no Windows/COM/WPF leakage | ✅ |
| No schema / migration / `ModelSnapshot` edits | ✅ |

### A1 — port adoption across remaining consumers ✅

> **Ports-only, no DTO swap.** Every remaining concrete `WorkflowQueryService` /
> `ProjectWorkflowPolicyService` dependency was switched to the co-located ports
> `IWorkflowQueryService` / `IProjectWorkflowPolicyService`. No DTO redesign, no query-logic
> change, and the write-path `WorkflowEngine` / `WorkflowTaskOrchestrator` stay concrete.
> Because the ports share the `SiNetSQL.Services.Workflow` namespace, no `using` changes were
> needed — each edit is a field/ctor-param type swap (VMs) or `GetRequiredService<Concrete>()`
> → `GetRequiredService<Interface>()` (hosts).

**Consumers re-pointed to ports:**

| Consumer | Change |
| --- | --- |
| `SiNetSQL.MVVM/WorkflowStatusViewModel.cs` | Field + ctor param → `IWorkflowQueryService`. |
| `SiNetSQL.MVVM/WorkflowInstanceViewModel.cs` | Query field + ctor param → `IWorkflowQueryService` (engine/orchestrator left concrete). |
| `SiNetSQL.MVVM/WorkflowDashboardViewModel.cs` | Query + policy fields/params → ports (orchestrator left concrete). |
| `WorkflowStatusMonitorWindow.xaml.cs` | Resolves `IWorkflowQueryService`. |
| `WorkflowInstanceWindow.xaml.cs` | Resolves `IWorkflowQueryService`. |
| `WorkflowDashboardView.xaml.cs` | Resolves `IWorkflowQueryService` + `IProjectWorkflowPolicyService`. |
| `WorkflowCreateProjectWindow.xaml.cs` | Resolves `IProjectWorkflowPolicyService`. |
| `Services/Migration/GoogleSheetReviewMigrationPreviewService.cs` | Field + ctor param → `IWorkflowQueryService`. |
| `MigrationPocWindow.xaml.cs` (×2) | Resolves `IWorkflowQueryService` before constructing the preview service. |

**Deferred (still not in this round):** DTO cleanup (remove EF entities from port signatures),
all workflow writes/engine, Builder-tab inline `DbContext` reads, UI/VM migration to `SiNet.App.Wpf`.

| Deliverable | Status |
| --- | --- |
| All concrete read/policy consumers switched to ports (both repos) | ✅ |
| Only DI forwarders reference the concrete types | ✅ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| No DTO / schema / migration / `ModelSnapshot` edits | ✅ |

### C1 — canonical `WorkflowStatus` enum + boundary mapper (additive) ✅

> **Smallest safe slice toward clean contracts ("Option C now → Option A later").**
> Adds the canonical clean-layer status enum and a boundary mapper **without** touching
> the ports, VMs, XAML, EF mapping, or the legacy enum's identity. Purely additive —
> nothing is wired into a port yet, so runtime behavior is unchanged.

**Why the full DTO/port migration ("Option A") is still deferred:** the read-port consumers
(`WorkflowInstanceViewModel`, `WorkflowDashboardViewModel`) live in the separate `SiNetSQL`
repo, bind EF entities directly to XAML, and interleave the **write** engine
(`WorkflowEngine` / `WorkflowTaskOrchestrator`, which still returns entities). Converting only
the read ports to DTOs would create a mixed entity/DTO graph and break bindings + write calls.
A faithful migration must move the write engine and rewrite XAML across two repos — out of this
round's scope.

**Added:**

| Artifact | Detail |
| --- | --- |
| `src/SiNet.Domain/Workflow/WorkflowStatus.cs` *(new)* | Canonical enum (`Draft=0`, `Active=1`, `Paused=2`, `Completed=3`, `Cancelled=4`) — values identical to legacy `SiNetSQL.Models.WorkflowStatus`. |
| `src/SiNet.Infrastructure.Sql/Services/Workflow/WorkflowStatusMappings.cs` *(new)* | Boundary mapper `ToDomain()` / `ToLegacy()` (incl. nullable overloads) between legacy EF and canonical enums. Single translation point for the future DTO migration. |
| `SiNetSQL.Tests/Services/Workflow/WorkflowStatusMappingTests.cs` *(new)* | 9 guard tests: name+value lockstep, round-trip both directions, per-member mapping, null handling. |

**Deferred to Option A:** move `IWorkflowQueryService` / `IProjectWorkflowPolicyService` into
`SiNet.Application`, return clean DTOs (replacing EF entities + snapshots), migrate the write
engine, and rewrite VMs/XAML accordingly.

| Deliverable | Status |
| --- | --- |
| Canonical `WorkflowStatus` in `SiNet.Domain` (values match legacy) | ✅ |
| Boundary mapper in `SiNet.Infrastructure.Sql` (not wired into any port) | ✅ |
| Guard tests in `SiNetSQL.Tests/Services/Workflow` | ✅ _9/9_ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| No port / VM / XAML / DTO / schema / migration / `ModelSnapshot` edits | ✅ |

### A2 — full DTO/port migration of the workflow read slice (Option A) ✅

> **Completes "Option A":** moves the workflow **read** ports into `SiNet.Application.Workflow`,
> returns clean DTOs (replacing EF entities + in-file snapshots), and propagates the DTO
> contract through every read consumer across both repos. The **write** engine
> (`WorkflowEngine` / `WorkflowTaskOrchestrator`) is still invoked by ID and left concrete —
> read VMs build DTO graphs and call writes by scalar id during the transition.

**Application layer (new DTO contract):**

| Artifact | Detail |
| --- | --- |
| `src/SiNet.Application/Workflow/IWorkflowQueryService.cs`, `IProjectWorkflowPolicyService.cs` *(moved)* | Ports relocated from infrastructure; now return DTOs. `GetByProjectAsync` takes `SiNet.Domain.Workflow.WorkflowStatus?`. |
| `src/SiNet.Application/Workflow/*Dto.cs` *(new)* | `WorkflowDefinitionDto`, `WorkflowInstanceDto`, `WorkflowStageDefinitionDto`, `WorkflowStageTransitionDto`, `WorkflowUserRefDto`, `WorkflowProjectRefDto`, snapshot DTOs — the full read surface. |
| `src/SiNet.Infrastructure.Sql/Services/Workflow/WorkflowDtoMappings.cs` *(new)* | Central entity→DTO mapper; uses `WorkflowStatusMappings.ToDomain()` and preserves stage/transition ordering. |
| `WorkflowQueryService.cs`, `ProjectWorkflowPolicyService.cs` *(rewritten)* | Return DTOs at the boundary; in-file snapshot classes removed. |

**Consumers re-pointed to DTOs (both repos):**

| Consumer | Change |
| --- | --- |
| `SiNetSQL.MVVM/WorkflowInstanceViewModel.cs` | Collections/properties → DTOs; writes still by id. |
| `SiNetSQL.MVVM/WorkflowDashboardViewModel.cs` | `Instances`/`Definitions` → DTOs; projects left entity-based. |
| `SiNetSQL.MVVM/WorkflowStatusViewModel.cs` + `ProjectWorkflowStatusItem.cs` | Snapshot DTOs + canonical domain `WorkflowStatus`. |
| `WorkflowManagementWindow.xaml.cs` (dashboard tab) | DTO lists/selection; management tree left entity-based. |
| `WorkflowCreateProjectWindow.xaml.cs` | Continuation workflow uses DTO allowed-workflow results. |
| `GoogleSheetReviewMigrationPreviewService.cs` | Consumes DTO active workflows. |
| `Domain/Actions/Handlers/StartWorkflowProcessActionHandler.cs` | Resolves definition into scalar `definitionId`/`definitionName`; `EnsureNoDuplicateForEmailAsync` takes `string?`. |
| `Services/EmailContext/ActionExecutor.cs` | `ResolveDefinitionAsync` → `WorkflowDefinitionDto?`. |
| `Services/EmailContext/EmailContextAnalyzer.cs` + `DTOs/Email/EmailContextResult.cs` | `ActiveWorkflows` → `IReadOnlyList<WorkflowInstanceDto>`; `CalculateConfidence` updated. |
| `App.xaml.cs` | DI forwarders point at `SiNet.Application.Workflow` ports. |
| `WorkflowQueryServiceTests.cs`, `ProjectWorkflowPolicyServiceTests.cs` | Use moved ports + domain status filter + DTO assertions. |

**Deferred (next):** migrate the **write** engine (`WorkflowEngine`/`WorkflowTaskOrchestrator`)
to DTO-returning contracts and move read VMs/XAML to `SiNet.App.Wpf`.

| Deliverable | Status |
| --- | --- |
| Read ports moved to `SiNet.Application.Workflow`, returning DTOs | ✅ |
| All read consumers propagate the DTO contract (both repos) | ✅ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| No schema / migration / `ModelSnapshot` edits | ✅ |

### A3 — workflow write-API result boundary returns DTOs (boundary-only) ✅

> Moves the **write** orchestration's *public result records* off EF entities and onto
> `WorkflowInstanceDto`, closing the last entity leak on the workflow read/write surface.
> Unlike the original "engine returns DTOs" idea, this is scoped to the **orchestrator
> boundary only** — the `WorkflowEngine` lifecycle methods and the task-provisioning chain
> stay entity-based on purpose (see rationale below).

**Why the engine stayed entity-based (deferred, deliberate):**

`WorkflowStageTaskProvisioningService.EnsureInitialStageTasksAsync(WorkflowInstance, …)` is a
hard entity consumer: it reuses the live `WorkflowInstance`, recursively calls
`WorkflowEngine.StartAsync(…)` for hosted sub-workflows, and re-feeds that entity result back
into itself, with `AutoAdvancePastStartNodeAsync` entity-in/entity-out. Changing the engine
signature to return DTOs would cascade through this recursion and far exceed the intended
scope. The boundary-only approach keeps the internal chain intact while presenting a clean
DTO contract to every public caller.

**Boundary contract (DTO at the public surface):**

| Artifact | Detail |
| --- | --- |
| `SiNetSQL/Services/Workflow/WorkflowTaskOrchestrator.cs` | `WorkflowStartResult.Instance` and `WorkflowAdvanceResult.Instance` are now `WorkflowInstanceDto`; `StageCompletionResult.AdvancedInstance` is `WorkflowInstanceDto?`. `StartWorkflowAsync` / `AdvanceWithTasksAsync` map at the return via `instance.ToDto()`; the entity is kept locally for status checks, provisioning, and parent-notification before mapping. `ExecuteTransitionAsync` forwards the now-DTO `result.Instance` into `AdvancedInstance` (no extra change). |
| `src/SiNet.Infrastructure.Sql/Services/Workflow/WorkflowDtoMappings.cs` | Promoted `internal static` → `public static` so the `SiNetSQL` write boundary can project at construction without an extra DB round-trip. |
| `WorkflowEngine.cs`, `WorkflowStageTaskProvisioningService.cs` | **Unchanged** — remain entity-returning internally by design. |

**Consumers (no code change required — verified DTO-safe scalars):**

| Consumer | Access |
| --- | --- |
| `Services/EmailContext/ActionExecutor.cs` | `BuildWorkflowStartResult` reads only `result.Instance.Id` + `result.CreatedTasks.Count`. |
| `WorkflowManagementWindow.xaml.cs` (dashboard start) | Reads `result.CreatedTasks.Count` + `result.Instance.Id`. |
| `StalledWorkflowWatchdog.cs`, `WorkflowActionCompletedHandler.cs` | Read only `StageCompletionResult.Action` / `.TargetStageId`; never `AdvancedInstance`. |
| `CheckAndAutoAdvanceAsync` / `CheckAndAutoAdvanceStalledWorkflowAsync` | Return `ExecuteTransitionAsync` (DTO-backed) or scalar-only results. |
| `ProposalProjectCreationCompletionAlignmentTests.cs` | Reads `start.Instance.Id` (DTO scalar); `WorkflowInstanceDto` resolves transitively (Tests → `SiNetSQL` → `SiNet.Infrastructure.Sql` → `SiNet.Application`). |

| Deliverable | Status |
| --- | --- |
| Public orchestrator result records expose `WorkflowInstanceDto` | ✅ |
| Engine + provisioning chain intentionally left entity-based | ✅ _deferred, documented_ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| Workflow engine + orchestrator + sub-workflow + E2E tests green | ✅ _21/21_ |
| No schema / migration / `ModelSnapshot` edits | ✅ |

**Next-round assessment (document-only):** the deferred "full engine + provisioning conversion"
has been scoped in [`WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md`](./WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md)
— write surface, coupling map (engine/provisioning recursion), target `IWorkflowCommandService`
sketch, and a phased P1–P5 cut plan. No code changed; each phase is its own approval gate.

### A4 — P1: write-result tasks projected to DTO (closes C6) ✅

> First executable phase from the command-service assessment. Removes the **last** EF-entity
> leak on the workflow write contract: `CreatedTasks` no longer exposes `ProjectAssignment`.
> Methods stay where they are; engine + provisioning chain untouched.

| Artifact | Detail |
| --- | --- |
| `src/SiNet.Application/Workflow/ProjectAssignmentSummaryDto.cs` *(new)* | Read-only task summary (id, project, title, assignee, status id + legacy status, task-type id, work-priority, due date). |
| `src/SiNet.Infrastructure.Sql/Services/Workflow/WorkflowDtoMappings.cs` | Added `ProjectAssignment.ToSummaryDto()` + `ToSummaryDtoList()`. |
| `SiNetSQL/Services/Workflow/WorkflowTaskOrchestrator.cs` | `WorkflowStartResult.CreatedTasks` and `WorkflowAdvanceResult.CreatedTasks` are now `IReadOnlyList<ProjectAssignmentSummaryDto>`; mapped via `tasks.ToSummaryDtoList()` at both return sites. |

**Consumers — no change required:** all 6 production readers use only `.Count`
(`StartWorkflowProcessActionHandler`, `WorkflowDashboardViewModel`, `WorkflowInstanceViewModel`,
`ActionExecutor`, `TaskLifecycleService`, `WorkflowManagementWindow`), which is identical on
`IReadOnlyList<T>`. The unrelated `TaskImportResult.CreatedTasks` was left untouched.

| Deliverable | Status |
| --- | --- |
| `CreatedTasks` exposes a DTO, not `ProjectAssignment` | ✅ |
| Engine + provisioning chain unchanged | ✅ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| Workflow tests green | ✅ _21/21_ |
| No schema / migration / `ModelSnapshot` edits | ✅ |

---

### A5 — P2: workflow write port (`IWorkflowCommandService`) + adapter + pilot consumer ✅

> Second executable phase from the command-service assessment
> (`WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md` §6, P2). Introduces an **additive** Application-layer
> write port and fronts the existing orchestrator with a thin adapter — **no orchestration logic
> moved**, engine + provisioning chain untouched. One pilot consumer switched off the concrete
> class.

| Artifact | Detail |
| --- | --- |
| `src/SiNet.Application/Workflow/WorkflowCommands.cs` *(new)* | Command records (`StartWorkflowCommand`, `AdvanceWorkflowCommand`, `TaskClosedCommand`, `StalledWorkflowCommand`) + `WorkflowTriggerTypeDto` (Application mirror of the infra enum, which lives in `SiNetSQL.Models`). |
| `src/SiNet.Application/Workflow/WorkflowWriteResultDtos.cs` *(new)* | DTO-only results (`WorkflowStartResultDto`, `WorkflowAdvanceResultDto`, `StageCompletionResultDto`, `StageCompletionActionDto`). |
| `src/SiNet.Application/Workflow/IWorkflowCommandService.cs` *(new)* | Write counterpart to `IWorkflowQueryService`; four `ValueTask` methods, DTOs only across the boundary. |
| `SiNetSQL/Services/Workflow/WorkflowCommandServiceAdapter.cs` *(new)* | Implements the port by delegating to `WorkflowTaskOrchestrator`; maps `WorkflowTriggerTypeDto` ↔ `WorkflowTriggerType` and `StageCompletionAction` ↔ `StageCompletionActionDto`. |
| `SiNetProjectManagerV2/App.xaml.cs` | Registered `IWorkflowCommandService` next to the orchestrator + read-port registration. |
| `SiNetSQL/Services/EmailContext/ActionExecutor.cs` | **Pilot consumer:** now injects `IWorkflowCommandService` instead of `WorkflowTaskOrchestrator`; both start sites use `StartWorkflowCommand`; `BuildWorkflowStartResult` takes `WorkflowStartResultDto`. |
| 9 test construction sites (8 direct `new ActionExecutor(...)` + 1 `RecordingActionExecutor : ActionExecutor` base call) | Wrap the existing `orchestrator` in `new WorkflowCommandServiceAdapter(orchestrator)` — behavior-preserving (real production adapter). |

| Deliverable | Status |
| --- | --- |
| Application-layer write port exists, DTO-only contract | ✅ |
| Adapter delegates to unchanged orchestrator (no logic moved) | ✅ |
| Pilot consumer (`ActionExecutor`) off the concrete class | ✅ |
| Engine + provisioning chain unchanged | ✅ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| Workflow tests green | ✅ _60/60_ |
| No schema / migration / `ModelSnapshot` edits | ✅ |

**Deferred (future gates):** P3 (modular `AddSiNetWorkflowCommands()` + remove service-locator
usages), P4 (migrate remaining consumers, mark orchestrator methods `internal`), P5 (optional full
relocation). See `WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md` §6.

---

### A6 — P4: close the last write-path EF-entity leak (watchdog → port, orchestrator narrowed) ✅

> Executes the **P4** deferred item from A5: removes the only remaining consumer that bypassed the
> write port (`StalledWorkflowWatchdog`) and narrows the orchestration members that still exposed
> EF entities / an infra result record on a `public` surface. **No engine/provisioning behavior
> changed** — same provisioning path, same stage; only visibility and the watchdog's dependency
> changed.
>
> **Supersedes the stale "illustrative / not-to-be-built" Option-A note in
> `WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md`:** the command-port migration (A4→A6) is effectively
> closed. The orchestrator stays concrete internally, fronted by `IWorkflowCommandService`; the
> only public boundary is the Application port and its DTOs.

| Artifact | Detail |
| --- | --- |
| `src/SiNet.Application/Workflow/IWorkflowCommandService.cs` | Added `ReprovisionStalledStageTasksAsync(StalledWorkflowCommand, CancellationToken) → ValueTask<int>` — returns only the created-task **count**, so no EF entity crosses the boundary. |
| `SiNetSQL/Services/Workflow/WorkflowCommandServiceAdapter.cs` | Implements the new method by delegating to the orchestrator's `ReprovisionCurrentStageTasksAsync` and returning the count. |
| `SiNetSQL/Services/Workflow/WorkflowTaskOrchestrator.cs` | `CreateStageTasksAsync`, `ExecuteTransitionAsync`, `EvaluateManualTransitionsAsync`, and the infra `StageCompletionResult` record narrowed `public → internal`. New `internal ReprovisionCurrentStageTasksAsync` resolves the instance's current stage (DbContext access stays in infra) and delegates to the same `_provisioning.CreateStageTasksAsync` path. |
| `SiNetSQL/Services/Workflow/StalledWorkflowWatchdog.cs` | Dropped the concrete `WorkflowTaskOrchestrator` ctor dependency; 0-task recovery now calls `IWorkflowCommandService.ReprovisionStalledStageTasksAsync`. XML-doc crefs re-pointed to the port. |
| `SiNetSQL.Tests/SubWorkflowOrchestrationRuntimeTests.cs` *(new test)* | `ReprovisionStalledStageTasks_Port_ReturnsCount_AndExposesNoEntities` — drives the port path through the real adapter; asserts an `int` count (0 for a SubWorkflow host stage) and no parent task links. |

| Deliverable | Status |
| --- | --- |
| Last port-bypass consumer (`StalledWorkflowWatchdog`) off the concrete orchestrator | ✅ |
| `IWorkflowCommandService` exposes no EF entity / raw `List<>` | ✅ |
| `CreateStageTasksAsync` / `ExecuteTransitionAsync` / `EvaluateManualTransitionsAsync` / `StageCompletionResult` no longer `public` | ✅ |
| `CreateStageTasksAsync` test caller still compiles via existing `InternalsVisibleTo("SiNetSQL.Tests")` | ✅ |
| Engine + provisioning recursion unchanged | ✅ |
| Full `SiNetProjectManager.sln` build green (VS MSBuild) | ✅ _0 errors_ |
| Workflow + mapper-guard tests green | ✅ _33/33_ (20 orchestration + 13 mapping/read-port) |
| No schema / migration / `ModelSnapshot` / EF-mapping edits | ✅ |
| No UI/XAML changes; no Gmail / ACC / SQL-composition / Inspection changes | ✅ |

**Remaining (optional, deferred):** P3 (modular DI + remove service-locator usages where still
present), P5 (optional full relocation of the orchestrator). The `StageCompletionAction` enum stays
`public` (harmless; referenced only by the internal record and the DTO mapper).

---

### D1 — Legacy host composition adoption, Phase 1: delegate the Workflow read slice ✅

> First executable phase of the **App startup / DI** domain. The legacy host's monolithic
> `ConfigureServices()` (~700 lines) is incrementally pointed at the existing modular composition
> root instead of hand-wiring every registration inline. Phase 1 delegates exactly **one**
> behavior-identical group — the Workflow **read slice** — and removes the corresponding inline
> duplicates. No runtime behavior changes; startup orchestration, logging bootstrap, font resolver,
> mutex, and global handlers are untouched.

**Why not adopt the whole `SiNet.App.Composition.AddSiNet()` aggregate yet:** it also calls
`AddSiNetLogging()` (registers `ConsoleAppLogger`, which would collide with the host's Serilog
wiring) and `AddSiNetGoogle()` (the native Gmail `IEmailGateway`, explicitly out of scope this
round). Phase 1 therefore delegates a single narrowly-scoped covered group via the module's own
extension rather than the aggregate.

**`ConfigureServices()` bucket map (audit of lines 152–854):**

| Bucket | Contents |
| --- | --- |
| **(1) Covered by a module — delegated now** | Workflow read slice: `WorkflowQueryService` + `IWorkflowQueryService`, `ProjectWorkflowPolicyService` + `IProjectWorkflowPolicyService` → `AddSiNetWorkflowReads()`. |
| **(1b) Covered but intentionally kept this round** | Logging (`AddLogging`+Serilog vs module `ConsoleAppLogger`); SQL `DbContextFactory` (host sources the connection string from its secret vault); Google stack (native Gmail out of scope); Workflow **write** port (already modular via `AddSiNetWorkflowCommands()`). |
| **(2) Host-specific — preserved** | Workflow write/engine services, task-lifecycle/coordinator/status, all `IProcessActionHandler` registrations, files/ACC filing stack + `ITokenProvider` factory, MoveToProject + typed-continuation application services, WPF continuation UI host, ACC metadata/health/file-index/Drive/Ollama/AI, the **ACC remote-vs-local provisioning block** (runtime-sensitive factory + self-signed-cert handling), and all WPF ViewModels + their backing services. |
| **(3) Orphaned / duplicate** | None, other than the read slice delegated in bucket (1). |

**Change:**

| Artifact | Detail |
| --- | --- |
| `SiNetProjectManagerV2/SiNetProjectManagerV2.csproj` | Added a direct `ProjectReference` to `src/SiNet.Infrastructure.Sql`. Already reached transitively via `SiNetSQL → SiNet.Infrastructure.Sql`; making it explicit introduces **no new cycle** (host → Infrastructure.Sql → Application/Domain). |
| `SiNetProjectManagerV2/App.xaml.cs` | Replaced the four inline read-slice `AddTransient` registrations with a single `SiNet.Infrastructure.Sql.WorkflowServiceCollectionExtensions.AddSiNetWorkflowReads(services)` call (fully-qualified — no `using` change). Registration shape is byte-for-byte equivalent: concrete type `Transient` + port forwarded via `sp.GetRequiredService<Concrete>()`. The interleaved write/engine registrations and the `AddSiNetWorkflowCommands()` call are unchanged. (Supersedes the A1 "added interface forwarders for both ports" note for the legacy host.) |

**Deferred (next phases):** delegate the SQL `DbContextFactory` (via `AddSiNetSql(connectionString)`)
once the host's vault-sourced connection string is threaded through; reconcile logging; everything
else stays host-wired until its owning module exists. The full aggregate `AddSiNet()` is **not**
adopted until logging + Google reconciliation is approved.

| Deliverable | Status |
| --- | --- |
| Workflow read slice delegated to `AddSiNetWorkflowReads()`; inline duplicates removed | ✅ |
| `ConfigureServices()` registration bucket map produced | ✅ |
| Host-specific / out-of-scope registrations preserved (logging, Google, SQL factory, write/engine, ACC, VMs) | ✅ |
| No new project-reference cycle (explicit ref over existing transitive path) | ✅ |
| Full `SiNetProjectManager.sln` build green (VS MSBuild) | ✅ _exit 0_ |
| Clean module chain `dotnet build` green (`SiNet.Infrastructure.Sql` → Application → Domain) | ✅ _exit 0_ |
| Workflow read/path regression tests green | ✅ _9/9_ |
| No schema / migration / `ModelSnapshot` edits | ✅ |
| No Workflow engine/provisioning, ACC/token, or Gmail behavior changes | ✅ |

---

### D2 — Legacy host composition adoption, Phase 2: bucket re-check (no safe group remained) ✅ _assessment_

> Phase 2 re-audited the four approved candidate groups for delegation at a faster pace, intending
> to delegate **all** clearly-covered, behavior-identical groups in one block. The verdict: **every
> remaining candidate hits a real, enumerated blocker**, so no code was changed this phase. Per the
> pace rule ("do not stop after one tiny group *unless there is a real blocker*"), each stop below is
> a genuine behavior/lifetime/auth blocker, documented for the next gate. The host's
> `ConfigureServices()` is unchanged from the D1 state.

**Candidate verdicts (priority order):**

| # | Candidate group | Module extension | Verdict | Blocker / reason |
| --- | --- | --- | --- | --- |
| 1 | SQL `DbContextFactory` | `AddSiNetSql(connectionString)` | ⛔ **Behavior mismatch — kept in host** *(→ resolved in D3)* | Vault sourcing *itself* is fine (host would pass the Credential-Manager string), **but** the host registration adds a `#if DEBUG` block — `EnableSensitiveDataLogging()` + `EnableDetailedErrors()` — that the clean `net10.0` module's fixed options lambda does **not** emit. Delegating would silently drop DEBUG diagnostics. Preserving it would require changing the shared module's signature (≈ "invent/alter a module"), which was out of Phase 2 scope. **Unblocked and delegated in D3** via the `SiNetSqlOptions.EnableEfDebugDiagnostics` opt-in. |
| 2 | Workflow command port | `AddSiNetWorkflowCommands()` | ✅ **Already modular (no-op)** | Delegated since A5/P2; the host already calls `WorkflowCommandsServiceCollectionExtensions.AddSiNetWorkflowCommands(services)` at `App.xaml.cs` line 274. Nothing left to move. |
| 3 | FileSystem / Logging | `AddSiNetFileSystem()` / `AddSiNetLogging()` | ⛔ **Dead/behavior-changing — kept in host** | The modules register clean ports `IFileStorage→LocalFileStorage` and `IAppLogger→ConsoleAppLogger`. A workspace scan shows the host **neither registers nor consumes** `IFileStorage`/`IAppLogger` anywhere; the host logs through `AddLogging`+Serilog and `AppLogger`. Calling these would add unused registrations and introduce a second logger abstraction — a behavior change with no consumer, not a like-for-like swap. |
| 4 | Google / Gmail | `AddSiNetGoogle(configure)` | ⛔ **Auth/runtime change — kept in host** *(→ confirmed in D4)* | The module wires the **native** `IEmailGateway→GmailEmailGateway` (direct Gmail API via `GmailClientProvider`). The host runs the legacy `GoogleAuthService` + `GoogleService` + `GmailOutboundMailService` sign-in/throttle path (bridged via `ILegacyEmailSource`). Adopting the module would change Gmail sign-in/runtime behavior — explicitly forbidden this phase. **D4 confirmed not behavior-equivalent** (read-only scope, separate OAuth client + token store, 5 legacy consumers, 0 native consumers) — no consolidation executed. |

**Net result:** group #2 was already modular; groups #1, #3, #4 remain host-specific for the
documented blockers. The `ConfigureServices()` bucket map from D1 stands unchanged; bucket **(1b)
"covered but intentionally kept"** is the authoritative list for #1/#3/#4, now with explicit
blocker reasons above.

**Unblock conditions (future gates, each its own approval):**

- **SQL #1:** ~~add a debug-diagnostics opt-in to `AddSiNetSql`~~ **Done in D3** — `SiNetSqlOptions.EnableEfDebugDiagnostics`
  opt-in added and the host SQL factory delegated with proven-identical behavior.
- **FileSystem/Logging #3:** only meaningful once host consumers are migrated to `IFileStorage` /
  `IAppLogger`; until then the modules have no consumer in this host.
- **Google #4:** ~~part of the separately-gated Gmail consolidation~~ **Investigated in D4** — paths
  are **not** behavior-equivalent (read-only native scope, separate OAuth client/token store, no
  native consumers); legacy `GoogleService` retained. No further composition gate planned for Gmail.

| Deliverable | Status |
| --- | --- |
| All four candidate groups re-audited against their module extensions | ✅ |
| Each non-delegated group has a real, documented blocker | ✅ |
| No code changed (no safe group remained) | ✅ |
| `ConfigureServices()` bucket map confirmed current (D1 (1b) authoritative) | ✅ |
| Builds untouched and still green from D1 (no regression introduced) | ✅ |
| No schema / migration / `ModelSnapshot` edits | ✅ |
| No Gmail sign-in, ACC/token, Workflow engine, or SQL config behavior changed | ✅ |

---

### D3 — SQL `AddSiNetSql` diagnostics option + host SQL-factory delegation ✅

> Closes the D2 #1 blocker. Adds a small, explicit **EF diagnostics opt-in** to the clean SQL
> registration so the legacy host can delegate its `DbContextFactory` wiring **without losing the
> existing DEBUG diagnostics**. Because the host's only divergence from the module was the
> `#if DEBUG` `EnableSensitiveDataLogging()` + `EnableDetailedErrors()` pair, the new opt-in makes
> the registrations **provably identical**, so the host factory was delegated in this same gate.

**Why delegation is behavior-identical (proof):**

| Aspect | Host (before) | Module `AddSiNetSql(conn, opt)` (now) | Same? |
| --- | --- | --- | --- |
| Context type | `SiNetSQL.Data.SiNetSQLDbContext` | `SiNetSQL.Data.SiNetSQLDbContext` (single shared type — EF layer lives in `SiNet.Infrastructure.Sql`) | ✅ |
| Registration | `AddDbContextFactory<…>` | `AddDbContextFactory<…>` | ✅ |
| Provider + compat | `UseSqlServer(conn, sql => sql.UseCompatibilityLevel(120))` | identical | ✅ |
| Connection string source | host vault (Windows Credential Manager), passed in | **unchanged** — host still resolves it and passes the string | ✅ |
| DEBUG diagnostics | `#if DEBUG` → `EnableSensitiveDataLogging()` + `EnableDetailedErrors()` | host sets `EnableEfDebugDiagnostics = true` only under `#if DEBUG`; module emits the same two calls | ✅ |
| Release diagnostics | none (`#if DEBUG` compiled out) | flag stays `false` → calls never made | ✅ |

**Added / changed:**

| Artifact | Detail |
| --- | --- |
| `src/SiNet.Infrastructure.Sql/SiNetSqlOptions.cs` *(new)* | Options object with a single `bool EnableEfDebugDiagnostics` (default `false`). Documented as a DEBUG-only opt-in; must stay off in Release. |
| `src/SiNet.Infrastructure.Sql/SqlServiceCollectionExtensions.cs` | New overload `AddSiNetSql(this IServiceCollection, string connectionString, Action<SiNetSqlOptions> configure)`. The existing `AddSiNetSql(services, connectionString)` now forwards to it with a no-op configure (default = diagnostics off — **byte-for-byte prior behavior**). When `EnableEfDebugDiagnostics` is set, the factory adds `EnableSensitiveDataLogging()` + `EnableDetailedErrors()`. The no-connection-string `AddSiNetSql(services)` no-op overload is unchanged. |
| `SiNetProjectManagerV2/App.xaml.cs` | The inline `AddDbContextFactory<SiNetSQLDbContext>(…)` block was replaced with `SiNet.Infrastructure.Sql.SqlServiceCollectionExtensions.AddSiNetSql(services, connectionString, options => { #if DEBUG options.EnableEfDebugDiagnostics = true; #endif })`. The vault lookup (`AppConfiguration.GetConnectionString("SiNetDatabase")`) and its throw-guard are unchanged; the project already references `SiNet.Infrastructure.Sql` (D1). |

**SQL factory delegation status:** ✅ **unblocked and delegated.** The D1 bucket-map entry for SQL
moves from **(1b) "covered but intentionally kept"** to **(1) delegated**.

**Not changed:** connection string source, EF mappings/configurations, migrations, `ModelSnapshot`,
Release SQL/runtime behavior, and every other registration group (workflow, logging, Google, ACC,
task/VM services).

| Deliverable | Status |
| --- | --- |
| `SiNetSqlOptions` diagnostics opt-in added; default disabled | ✅ |
| `AddSiNetSql(conn, configure)` overload; existing overload forwards unchanged | ✅ |
| Host SQL factory delegated with proven-identical behavior (incl. `#if DEBUG`) | ✅ |
| Connection string source unchanged (host vault) | ✅ |
| Clean module `dotnet build` green (Release) | ✅ _exit 0_ |
| Full `SiNetProjectManager.sln` via VS MSBuild green (Debug **and** Release) | ✅ _exit 0_ |
| Release behavior unchanged (`#if DEBUG` compiled out; flag stays `false`) | ✅ |
| Workflow/DI regression tests green | ✅ _22/22_ |
| No schema / migration / `ModelSnapshot` / EF-mapping edits | ✅ |
| No unrelated registration groups touched | ✅ |

---

| No Gmail sign-in / ACC-token / Workflow / SQL / schema changes | \u2705 |

---

### D5 \u2014 Native Google capabilities: auth-state / health bridge \u2705

> First **execution** step of the post-D4 *Native Google capabilities* track. Ports the smallest
> dependency-surface capability that legacy `GoogleService` still uniquely covers \u2014 the
> **auth-state / health bridge** (`GoogleService.AuthStateChanged`) \u2014 into the clean stack, **without**
> touching the legacy production host. No new OAuth scopes, no new NuGet, no Gmail/Drive API calls.

**What was added (clean module only \u2014 `SiNet.Infrastructure.Google`):**

| Artifact | Detail |
| --- | --- |
| `GmailClientProvider.cs` | New `event Action<bool>? AuthStateChanged` + private `RaiseIfAuthStateChanged(wasSignedIn)`. Raised (outside `_gate`) on a real transition from `TryGetServiceAsync`, `TrySignInSilentlyAsync`, `SignInInteractiveAsync` (false\u2192true) and on `Logout` / `DisposeAsync` (true\u2192false). New `Logout()` drops the cached session **without** deleting the persisted refresh token (silent restore can re-establish it). |
| `GmailConnectorAuthService.cs` *(new)* | `IConnectorAuthService` adapter over the provider: `IsAuthenticated`\u21d2`IsSignedIn`, `LoginAsync`\u21d2`SignInInteractiveAsync` (true on `Success`), `TryRestoreSessionAsync`\u21d2`TrySignInSilentlyAsync`, `Logout`\u21d2`Logout`, and `AuthStateChanged` forwarded via add/remove accessors. |
| `GoogleServiceCollectionExtensions.cs` | `AddSiNetGoogle` now registers `IConnectorAuthService \u2192 GmailConnectorAuthService` as a singleton **over the same `GmailClientProvider` singleton** that backs `IEmailGateway` \u2014 one shared source of truth for signed-in state + events. |

**Why this is the right first capability:** `IConnectorAuthService` (the connector-agnostic auth/health
port, already in `SiNet.Application.Common`) is the native equivalent of the legacy health bridge. The
native provider already tracked `IsSignedIn` but raised no event, so health/status consumers in the new
stack had nothing to subscribe to. This closes that gap with **zero** scope/NuGet/API surface and keeps
the module free of WPF/legacy dependencies.

**Legacy host:** untouched. `SiNetProjectManagerV2` still owns `GoogleAuthService`/`GoogleService` and
its `AuthStateChanged` health wiring exactly as before; the native bridge lives only in the clean stack.

**Remaining `GoogleService` replacement gaps (for future, separately-approved capability steps):**

| Capability | Native status | Gap to close before host switch |
| --- | --- | --- |
| Gmail **read** | \u2705 `IEmailGateway \u2192 GmailEmailGateway` (`GmailReadonly`) | none |
| Auth-state / **health** | \u2705 **D5** `IConnectorAuthService \u2192 GmailConnectorAuthService` | none |
| Gmail **send** | \u2705 **D6** `IEmailSender \u2192 GmailEmailSender` (`GmailSend` scope) | re-consent on first native send (token scope upgrade) |
| Google **Drive** read/write | \u26d4 not ported | needs `Google.Apis.Drive.v3` + Drive scopes + Drive port(s) + impl |
| Native **sign-in/config** flow | \u26a0 partial | interactive sign-in exists; production-grade config/secrets wiring for the host switch still TBD |

| Deliverable | Status |
| --- | --- |
| Native auth-state event added to `GmailClientProvider` | \u2705 |
| `IConnectorAuthService` native adapter implemented + registered | \u2705 |
| Clean module free of WPF / legacy / `SiNetProjectManagerV2` deps | \u2705 |
| Clean module `dotnet build` (Debug) green | \u2705 |
| Full `SiNetProjectManager.sln` build green | \u2705 |
| Legacy Google path unchanged and still builds | \u2705 |
| No OAuth scope / NuGet / ACC-token / Workflow / SQL / schema changes | \u2705 |
| Remaining `GoogleService` gaps documented (send, Drive, config) | \u2705 |

---

### D6 \u2014 Native Gmail send capability \u2705

> Second **execution** step of the *Native Google capabilities* track, run as its own explicit gate
> because it requires the **`GmailSend` OAuth scope** and therefore a one-time **re-consent**. Adds
> native send behind a new clean Application port. The legacy production host is **not** switched and
> **not** modified.

**Plan deliverable (the 7 required topics):**

| # | Topic | Decision |
| --- | --- | --- |
| 1 | Application send port | `IEmailSender.SendAsync(EmailSendRequest, CancellationToken) \u2192 EmailSendResult`. UI-agnostic DTOs: `EmailSendRequest` (To/Cc/Bcc, Subject, Body, `IsHtml`, optional `From`, `ThreadId`, `InReplyToMessageId`, `Attachments`), `EmailAttachment` (name + content-type + `ReadOnlyMemory<byte>`), `EmailSendResult` (Success / MessageId / Error / **`RequiresConsent`** / ShouldRetry). |
| 2 | Required scopes | Added the **narrowest** send scope `GmailService.Scope.GmailSend` alongside `GmailReadonly` (not full `MailGoogleCom`). |
| 3 | Token / consent impact | The persisted token grants read-only; expanding scopes means the first native **send** fails with an insufficient-permission error until a deliberate interactive sign-in re-grants read + send. **Reads keep working** on the old token; **startup stays silent** (no surprise consent). The sender surfaces this as `EmailSendResult.RequiresConsent`. |
| 4 | Where send lives | `GmailEmailSender` in `SiNet.Infrastructure.Google`, over the shared `GmailClientProvider` singleton; registered by `AddSiNetGoogle`. No new project. |
| 5 | No legacy coupling | No reference to legacy `IOutboundMailService` / `GmailOutboundMailService` / `SiNetProjectManagerV2`. Module references only `SiNet.Application` + Google libs. |
| 6 | Tests / smoke | Clean-module build + full-solution build + compile-level verification of the MIME/base64url builder and non-throwing failure paths. No live-network send (would require real consent). |
| 7 | Stop condition | Port + DTOs defined, impl + registration done, scope added with documented consent handling, builds green, legacy unchanged, remaining gaps documented. |

**What was added:**

| Artifact | Detail |
| --- | --- |
| `SiNet.Application/Abstractions/Email/IEmailSender.cs` *(new)* | Send port; non-throwing for expected failures. |
| `SiNet.Application/Abstractions/Email/EmailSendRequest.cs` + `EmailSendResult.cs` + `EmailAttachment.cs` *(new)* | UI-agnostic send DTOs (no WPF types). |
| `SiNet.Infrastructure.Google/GmailEmailSender.cs` *(new)* | Builds RFC 5322 MIME by hand (no new NuGet): single text part or `multipart/mixed` with attachments, RFC 2047 encoded-word headers for non-ASCII, base64 bodies/attachments chunked at 76 cols, then base64url; sends via `users.messages.send` (`"me"`); threads via `ThreadId` + `In-Reply-To`/`References`. Maps 403 insufficient-permission \u2192 `RequiresConsent`, 429/5xx \u2192 retryable, not-signed-in \u2192 `Fail`. |
| `SiNet.Infrastructure.Google/GmailClientProvider.cs` | `Scopes` now `{ GmailReadonly, GmailSend }`, with an inline note on the consent impact. |
| `SiNet.Infrastructure.Google/GoogleServiceCollectionExtensions.cs` | `AddSiNetGoogle` registers `IEmailSender \u2192 GmailEmailSender` over the same provider singleton that backs read + auth-state. |

**Legacy host:** untouched. `SiNetProjectManagerV2` still sends via legacy `GmailOutboundMailService`/`GoogleService`; the native sender is available in the clean stack only and is inert for the host.

**Remaining gaps (still deferred to their own gates):** Google **Drive** read/write; native **sign-in/config** adoption by a real host; the eventual **production-host switch** off legacy `GoogleService`.

| Deliverable | Status |
| --- | --- |
| `IEmailSender` port + UI-agnostic DTOs added | \u2705 |
| `GmailEmailSender` implemented (MIME + base64url, non-throwing) | \u2705 |
| `GmailSend` scope added (narrowest) with consent handling | \u2705 |
| `IEmailSender \u2192 GmailEmailSender` registered over shared provider | \u2705 |
| Clean module free of WPF / legacy / `SiNetProjectManagerV2` deps | \u2705 |
| Clean module `dotnet build` (Debug) green | \u2705 |
| Full `SiNetProjectManager.sln` build green | \u2705 |
| Legacy Google send/Drive/sign-in path unchanged and still builds | \u2705 |
| No new NuGet / Drive scope / ACC-token / Workflow / SQL / schema changes | \u2705 |
| Drive + host-switch gaps remain documented | \u2705 |

---

### D7 \u2014 Native Gmail send/MIME tests (\u0060SiNet.Infrastructure.Google.Tests\u0060) \u2705

> Verification gate for D6. Adds a small, **deterministic, offline** test project for the native
> send path: it locks the hand-built RFC 5322 / base64url MIME output and the send error
> classification, with **no** live Gmail API, OAuth, network, or real send. The legacy host and the
> production send path are **not** touched.

**Testability seams (behavior-preserving):**

| Change | Detail |
| --- | --- |
| \u0060GmailEmailSender\u0060 helpers \u0060private static\u0060 \u2192 \u0060internal static\u0060 | \u0060BuildRawMessage\u0060, \u0060EncodeAddressList\u0060, \u0060EncodeHeader\u0060, \u0060IsAscii\u0060, \u0060EnsureAngleBrackets\u0060, \u0060ChunkBase64\u0060, \u0060Base64UrlEncode\u0060, \u0060IsInsufficientScope\u0060, \u0060IsTransient\u0060. **Accessibility only** \u2014 no logic change. |
| \u0060BuildRawMessage(request, string? boundary = null)\u0060 | Optional fixed-boundary seam for deterministic \u0060multipart/mixed\u0060 assertions. Production still passes \u0060null\u0060 \u2192 a fresh random \u0060Guid\u0060 boundary. |
| \u0060<InternalsVisibleTo Include="SiNet.Infrastructure.Google.Tests" />\u0060 | Exposes the internal seams to the test assembly only. |

**What was added:**

| Artifact | Detail |
| --- | --- |
| \u0060src/SiNet.Infrastructure.Google.Tests/SiNet.Infrastructure.Google.Tests.csproj\u0060 *(new)* | \u0060net10.0\u0060 xUnit 2.9.3 project (matches repo test conventions); references the Google module only. Added to \u0060SiNetProjectManager.sln\u0060. |
| \u0060GmailMimeBuilderTests.cs\u0060 *(new)* | Decodes the base64url output back to MIME and asserts: RFC 5322 header order/presence (From/To/Cc/Bcc/Subject/MIME-Version), \u0060text/plain\u0060 vs \u0060text/html\u0060, non-ASCII subject/body/filename (RFC 2047 \u002B base64), threading (\u0060In-Reply-To\u0060/\u0060References\u0060 \u002B angle-bracket normalization), \u0060multipart/mixed\u0060 with fixed boundary, \u0060application/octet-stream\u0060 fallback, multiple attachments, random-boundary-per-call, base64url validity round-trip, and the encoding helpers (\u0060IsAscii\u0060, \u0060EncodeHeader\u0060, \u0060EncodeAddressList\u0060, \u0060EnsureAngleBrackets\u0060, \u0060ChunkBase64\u0060 76-col wrap, \u0060Base64UrlEncode\u0060). |
| \u0060GmailSendErrorMappingTests.cs\u0060 *(new)* | Synthetic \u0060GoogleApiException\u0060 (status \u002B \u0060RequestError\u0060/\u0060SingleError.Reason\u0060) drives \u0060IsInsufficientScope\u0060 (403 \u002B \u0060insufficientPermissions\u0060/\u0060ACCESS_TOKEN_SCOPE_INSUFFICIENT\u0060, case-insensitive \u2192 consent; 403 unrelated/no-detail \u2192 false; 401 \u002B insufficient message \u2192 consent; non-403 \u2192 false) and \u0060IsTransient\u0060 (429/5xx \u2192 retryable; 4xx \u2192 not). |

**Result:** **51 tests, 51 passed, 0 failed** (offline). No production behavior change beyond the
accessibility widening and the optional boundary parameter.

**Remaining native send risks (documented, not addressed here):** no end-to-end test against the live
\u0060users.messages.send\u0060 API (would require real consent/network); MIME is hand-rolled (no \u0060MimeKit\u0060), so
exotic cases \u2014 header folding for very long encoded-words, inline/\u0060Content-ID\u0060 images, explicit
\u0060multipart/alternative\u0060 (text+HTML) bodies, and non-UTF-8 charsets \u2014 are out of scope; \u0060RequiresConsent\u0060
is asserted at the classifier level (\u0060IsInsufficientScope\u0060) rather than through a live 403; the actual
interactive **re-consent** flow for the expanded \u0060GmailReadonly\u0060 \u002B \u0060GmailSend\u0060 scopes remains a separate
deferred item.

| Deliverable | Status |
| --- | --- |
| \u0060SiNet.Infrastructure.Google.Tests\u0060 project created \u002B added to solution | \u2705 |
| Deterministic MIME/encoding/threading tests | \u2705 |
| Send error-mapping tests (\u0060RequiresConsent\u0060 / retryable / fail) | \u2705 |
| Seam extraction is accessibility-only \u002B optional boundary (no behavior change) | \u2705 |
| All Google module tests pass | \u2705 _51/51_ |
| Full \u0060SiNetProjectManager.sln\u0060 build green | \u2705 |
| No live Gmail / OAuth / network / real send | \u2705 |
| No Drive / host-switch / ACC / Workflow / SQL / schema changes | \u2705 |
| Remaining native send risks documented | \u2705 |

---

## Google/Gmail consolidation: blocked until capability and token-store parity are explicitly designed

**Decision:** Do **not** consolidate the legacy `GoogleService` registration into the native
`SiNet.Infrastructure.Google` path inside `SiNetProjectManagerV2`. The legacy and native Google
paths are **not behavior-equivalent**.

**Findings (accepted):**

| Aspect | Legacy `GoogleService` (`SiOffice.GoogleConnector`, active in `SiNetProjectManagerV2`) | Native `GmailClientProvider` (`SiNet.Infrastructure.Google`, active in `SiNet.App.Wpf`) |
| --- | --- | --- |
| OAuth scopes | `GmailModify` **+ Sheets.Spreadsheets + Drive.DriveReadonly** | `GmailReadonly` + `GmailSend` (Gmail only) |
| Token store | `%APPDATA%\SiNet\GoogleTokens` (shared) | **Independent** token store (separate from legacy) |
| Capabilities | Gmail + **Sheets** + **Drive** | Gmail read/send only |
| Sign-in | `LoginAsync` / `TryRestoreSessionAsync` (broad consent) | silent restore; interactive only if `AllowInteractiveSignIn`; **send needs re-consent** |

- `SiNetProjectManagerV2` (legacy host) does **not** call `AddSiNet`/`AddSiNetGoogle`; the native
  Gmail stack is not registered there.
- `SiNet.App.Wpf` (modular host) calls `services.AddSiNet(ConfigureGmail)` → `AddSiNetGoogle`; no
  legacy `GoogleService`.
- Switching the legacy host onto the native registration would change runtime sign-in behavior,
  force re-authentication (different token store), break Drive/Sheets consumers (no scopes), and
  trigger a send re-consent prompt.

**Constraints while blocked:** no code change for consolidation; do not change token stores, OAuth
scopes, or sign-in behavior; do not retire legacy `GoogleService`; do not merge the two paths inside
the legacy host.

**Unblock requires (separate, explicitly approved design):** capability parity (Sheets + Drive on the
native path or a dedicated native Drive provider), a token-store strategy (reuse the existing store
vs. an accepted one-time re-consent), and porting the legacy consumers into `SiNet.App.Wpf` rather
than swapping registrations inside `SiNetProjectManagerV2`.

---

## Inspection decomposition — Phase 2 (in-place helper extraction)

**Goal:** Reduce `FloatingInspectionViewModel` by extracting several low-risk responsibilities
in one block while preserving behavior. No XAML changes; no schema/migration/ModelSnapshot;
no Google/Gmail, ACC/token, or Workflow changes; report generation and sent/locked report
actions intentionally untouched.

**What was extracted (new internal helpers in `SiNetSQL.MVVM`):**

| New file | Responsibility moved out of the VM |
| --- | --- |
| `InspectionNoteHelpers.cs` | Pure note utilities: `IsSubNote`, `CanAddNoteToSection`, `SubscribeNoteForCommandRefresh`, `ResolveNoteStatusId(note, statusKeyToId)`, `CanMoveNote(note, direction)`. |
| `InspectionNoteOrdering.cs` | `ComputeReindex(section)` — the pure sub-note re-indexing computation (mutates `NoteSubIndex`, returns the changed `(NoteId, SubIndex)` renumberings). |
| `InspectionAvailabilityDiagnostics.cs` | `LogActiveFileAvailability(...)` — the self-contained active-file availability diagnostics log (log-only). |

**How behavior is preserved:** the VM keeps the same private members (`ResolveNoteStatusId`,
`SubscribeNoteForCommandRefresh`, `IsSubNote`, `CanAddNoteToSection`, `CanMoveNote`,
`LogActiveFileAvailability`) as thin wrappers that delegate to the helpers, so every existing call
site and command binding is unchanged. `ReindexSectionNotesAsync` now calls
`InspectionNoteOrdering.ComputeReindex` and still persists via `_reportService.RenumberNotesAsync`;
`DeleteEmptyNote` / `MoveNoteUp` / `MoveNoteDown` keep their persistence + `StatusMessage` handling
in the VM. The diagnostics log line is emitted verbatim.

**Tests:** added `InspectionNoteHelpersTests.cs` (19 tests covering `IsSubNote`,
`CanAddNoteToSection`, `CanMoveNote`, `ResolveNoteStatusId`, and `ComputeReindex`), exercising the
extracted pure logic via the existing `InternalsVisibleTo("SiNetSQL.Tests")`.

| Deliverable | Status |
| --- | --- |
| `InspectionNoteHelpers` / `InspectionNoteOrdering` / `InspectionAvailabilityDiagnostics` extracted | ✅ |
| VM delegates via thin wrappers; call sites + XAML unchanged | ✅ |
| Focused unit tests added (19) | ✅ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| Inspection tests green (73 passed, 2 pre-existing skips) | ✅ |
| No schema / migration / ModelSnapshot / Google / ACC / Workflow changes | ✅ |
| Report generation + sent/locked actions untouched | ✅ |

**Remaining Inspection debt:** `FloatingInspectionViewModel` is still large (~5,330 lines). Next
candidates (later phases): `Template Sync On Load`, `Drawing Management`, `Reviewed Plan (Phase A/B)`,
and ultimately the largest region `Sent / Locked report actions` + report generation (explicitly out
of scope for this phase). The original goal "split list/report/notes/photos" into
`SiNet.Application` `IInspection*` ports remains a larger, separately-scoped effort.

---

## Inspection decomposition — Phase 3 (pure transform builders)

**Goal:** Extract a larger, behavior-preserving block than the Phase 2 micro-helpers by pulling the
deterministic, dependency-free transforms out of the `Drawing Management` and
`Reviewed Plan (Phase A/B)` regions. No XAML changes; no schema/migration/ModelSnapshot;
no Google/Gmail, ACC/token, SQL-composition, or Workflow changes; report generation and
sent/locked report actions intentionally untouched.

**Dependency scan outcome:** the `Drawing Management` and `Reviewed Plan` command handlers are
`async void` and tightly coupled to VM state (`StatusMessage`, `MessageBox`, `_reportService`,
`ActiveFileQueryRegistry` / `FileOpenServiceRegistry`, `ReportDrawings`, `OnPropertyChanged`),
so they stay in the VM. Only the **pure transforms** embedded inside them are cohesive and
side-effect-free, so those were extracted.

**What was extracted (new internal helpers in `SiNetSQL.MVVM`):**

| New file | Responsibility moved out of the VM |
| --- | --- |
| `InspectionReviewedPlanBuilder.cs` | `BuildCandidates(activeFiles)` — projects the live `ActiveFileInfo` snapshot into `ReviewedPlanCandidate` rows (empty-alternatives fallback + `AlternativeName ?? string.Empty`); `BuildReviewedFileRows(reportId, chosen)` — shapes chosen candidates into `InspectionReportReviewedFile` rows (drop-blank → trim → `Distinct()` → sequential `SortOrder`). |
| `InspectionDrawingStampBuilder.cs` | `BuildStampEntities(reportId, items)` — maps `DrawingDisplayItem`s to `InspectionReportDrawing` entities plus the parallel `(int Id, int[] LayoutIndices)` persistence payload (skip null id; selected-layout filtering; empty selection serializes to `"[]"`). |

**How behavior is preserved:** `SelectReviewedPlan` now calls
`InspectionReviewedPlanBuilder.BuildCandidates` (dialog/registry orchestration unchanged);
`PersistReviewedFilesAsync` calls `BuildReviewedFileRows` and still persists via
`_reportService.PersistReviewedFilesAsync` with the same try/catch + `StatusMessage`;
`StampDrawings` calls `BuildStampEntities` and keeps `SaveDrawingLayoutsAndStatusAsync`, the
stamping service call, status messages, and UI updates in the VM. Each extracted block is a verbatim
move of the original transform.

**Tests:** added `InspectionReviewedPlanBuilderTests.cs` (9 tests) and
`InspectionDrawingStampBuilderTests.cs` (6 tests) covering candidate projection, reviewed-row
shaping (trim / blank-drop / dedupe-after-trim / SortOrder), and stamp-entity mapping
(id-skip / selected-only / `"[]"` serialization / stamp-data order), via the existing
`InternalsVisibleTo("SiNetSQL.Tests")`.

| Deliverable | Status |
| --- | --- |
| `InspectionReviewedPlanBuilder` / `InspectionDrawingStampBuilder` extracted | ✅ |
| VM call sites delegate to builders; bindings + XAML unchanged | ✅ |
| Focused unit tests added (15) | ✅ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| Inspection builder/helper tests green (34 passed, 0 failed) | ✅ |
| No schema / migration / ModelSnapshot / Google / ACC / SQL / Workflow changes | ✅ |
| Report generation + sent/locked actions untouched | ✅ |

**Remaining Inspection debt:** `FloatingInspectionViewModel` is still large (~5,270 lines). The
remaining command handlers in `Drawing Management` (`LoadAvailableDrawingFiles`,
`LoadReportDrawings`, `AddDrawingToReport`, `RemoveDrawingFromReport`, `StampDrawings`,
`ResolveDwfTemplatePath`, `ResolveApprovedPlanDirectory`) and `Reviewed Plan` (`SelectReviewedPlan`,
`OpenNoteLinkedFile`, persistence wrappers) stay coupled to services/dialogs/registries.
**Recommended Phase 4:** lift `Drawing Management` (and the reviewed-plan/open-file orchestration)
behind a dedicated injectable service so the VM holds only thin command bodies — a bigger,
DI-touching step that should be approved on its own; the largest region
`Sent / Locked report actions` + report generation remains explicitly deferred.

---

## Inspection decomposition — Phase 4 (injectable Drawing-Management service)

**Goal:** Move from pure helper extraction to a real injectable-service extraction for the
`Drawing Management` orchestration, while preserving current UI behavior. The VM stays the
command/UI-state owner; the deterministic coordination (active-file/candidate discovery and
path resolution) moves behind a constructor-injected service.

**Dependency scan (pre-extraction):** `ResolveApprovedPlanDirectory` and `ResolveDwfTemplatePath`
were each only called from `StampDrawings` inside the VM; `LoadAvailableDrawingFiles` is called
once internally. There is **no** `new FloatingInspectionViewModel(...)` anywhere — the VM is pure
DI (`AddTransient`), so adding a constructor parameter is safe. `FileIndexService` was already
registered as `AddSingleton`, so the VM's `ServiceLocator.GetService<FileIndexService>()` could be
replaced with proper injection through the new service.

**What was extracted (new service in `SiNetSQL.Services.InspectionSync`):**

| New file | Responsibility moved out of the VM |
| --- | --- |
| `IInspectionDrawingManagementService.cs` | Contract + UI-agnostic result/request types: `LoadAvailableDrawingCandidatesAsync` (returns `AvailableDrawingDiscoveryResult` with an `AvailableDrawingDiscoveryStatus` discriminator + `AvailableDrawingCandidate` list), `ResolveDwfTemplatePathAsync`, `ResolveApprovedPlanDirectoryAsync`. Request structs `AvailableDrawingRequest` / `ApprovedPlanDirectoryRequest` carry the project/series context so the service stays free of `ActiveProjectContext`/WPF state. |
| `InspectionDrawingManagementService.cs` | The deterministic orchestration: slot lookup (`GetAvailableDrawingSlotsAsync`) → file-index scan (`FileIndexService.ScanFolderAsync`) → `.pdf`/`.dwf` filter → parsed-number slot matching → cross-store dedup; plus DWF stamp-template resolution (settings + `File.Exists`) and approved-plan output-directory resolution (config lookup + `Directory.CreateDirectory`). Verbatim moves of the original VM logic. |

**How behavior is preserved:** `LoadAvailableDrawingFiles` stays an `async void` command handler:
it still owns `AvailableDrawingFiles.Clear()`, the null-project early return, the `catch`, the
`finally` `OnPropertyChanged`, and the **exact** Hebrew `StatusMessage` strings — now driven by the
returned `AvailableDrawingDiscoveryStatus` (`NoSlotsForSeries` / `NoSlotsForProject` /
`IndexUnavailable` / `Success`). The VM's `ServiceLocator` usage is gone (0 remaining references).
`ResolveDwfTemplatePath` is now a passthrough; `ResolveApprovedPlanDirectory` keeps its
`_activeProject`/`_selectedSeries` VM-state guard and delegates the rest. `StampDrawings` is
unchanged apart from those two resolver calls now hitting the service.

**DI:** `services.AddTransient<IInspectionDrawingManagementService, InspectionDrawingManagementService>()`
registered in `App.xaml.cs`, immediately before the `FloatingInspectionViewModel` registration
(transient, matching its `IInspectionReportService` dependency).

**Tests:** added `InspectionDrawingManagementServiceTests.cs` (6 tests) covering the status mapping
(`NoSlotsForSeries`, `NoSlotsForProject`, series-id-0 treated as no-series, `Success` with empty
candidates against a real `FileIndexService` built with no stores) and the resolvers
(`ResolveDwfTemplatePath` → null when unset, `ResolveApprovedPlanDirectory` → null when no config).
Uses a hand-written `FakeReportService` (the test project references no mocking library) and the
existing `TestDbContextFactory` for a real `SystemSettingsService`.

| Deliverable | Status |
| --- | --- |
| `IInspectionDrawingManagementService` + `InspectionDrawingManagementService` extracted | ✅ |
| VM `LoadAvailableDrawingFiles` + resolvers delegate; `ServiceLocator` removed from VM | ✅ |
| Service registered in `App.xaml.cs` DI | ✅ |
| Focused unit tests added (6) | ✅ |
| Full `SiNetProjectManager.sln` build green | ✅ _0 errors_ |
| Inspection service/builder/detector tests green (21 passed, 0 failed) | ✅ |
| No schema / migration / ModelSnapshot / Google / ACC / SQL / Workflow changes | ✅ |
| Report generation + sent/locked actions untouched; XAML unchanged | ✅ |

**Dependencies moved out of `FloatingInspectionViewModel`:** the `FileIndexService` service-locator
lookup, the slot-grouping/scan/dedup discovery loop, the DWF-template settings resolution, and the
approved-plan config/path-building logic.

**What remains VM-owned (by design):** commands, `SelectedSeries`/selected items, `MessageBox`/dialog
decisions, `StatusMessage`, observable collections (`AvailableDrawingFiles`, `ReportDrawings`) and
their mutation, `OnPropertyChanged`/UI refresh, the `async void` command bodies, and the
registry-driven open/active-file orchestration (`ActiveFileQueryRegistry` / `FileOpenServiceRegistry`).

**Remaining Inspection debt:** `FloatingInspectionViewModel` is still large. `LoadReportDrawings`,
`AddDrawingToReport`, `RemoveDrawingFromReport`, and the `Reviewed Plan` command handlers
(`SelectReviewedPlan`, `OpenNoteLinkedFile`, persistence wrappers) remain coupled to
services/dialogs/registries. **Recommended Phase 5:** extract a reviewed-plan/open-file orchestration
service (mirroring this phase) to absorb `SelectReviewedPlan` candidate sourcing + `OpenNoteLinkedFile`
resolution, leaving the VM with thin command bodies; the `Sent / Locked report actions` + report
generation region remains explicitly deferred.

---

## Inspection decomposition — Phase 5 (Template-Sync pure transforms)

> **UX guardrail:** executed under the approved `same UX, cleaner internals` principle. Production UI
> stays `FloatingInspectionView` (unchanged); the tabbed `InspectionShellView` was **not** expanded.
> (The "reviewed-plan/open-file service" idea noted at the end of Phase 4 remains a *separate* future
> candidate; this Phase 5 instead targets the self-contained Template-Sync math, the safest next block.)

**Goal:** Extract the pure, side-effect-free Template-Sync logic from `FloatingInspectionViewModel`
into a unit-testable helper, with **no** UI/XAML/behavior change. No schema/migration/ModelSnapshot;
no Google/Gmail, ACC/token, SQL-composition, or Workflow changes; report generation and sent/locked
report actions intentionally untouched.

**What was extracted (new helper in `SiNetSQL.MVVM`):**

| New file | Responsibility moved out of the VM |
| --- | --- |
| `InspectionTemplateSyncMath.cs` | `Compare(tags, sections)` — the pure core of the former `CompareTemplateWithTree`: builds the template code/title lookups (status-tag `DefaultText` is authoritative, legacy note-tag `Title` is the fallback), decides each section's target `SectionSyncState` (missing / title-mismatch via trim + case-insensitive compare / normal), collects title-mismatches, counts missing/new codes, and composes the Hebrew summary string — returned as a `TemplateSyncComparison` result. `ComputeHash(tags)` — the former `ComputeTemplateHash` structural fingerprint (code/row/col, order-independent), moved verbatim. Inputs use a UI-free `SectionDescriptor(NumericCode, DisplayTitle)` so the helper stays free of WPF/observable types. |

**How behavior is preserved:** `CompareTemplateWithTree` now maps the live tree sections to
`SectionDescriptor`s, calls `InspectionTemplateSyncMath.Compare`, then **applies** the returned
`SyncStates` onto the matching `SectionTreeItem`s, sets `StatusMessage` from `SummaryText`, and maps
the `TitleMismatch` records back to the existing `(Code, OldTitle, NewTitle)` tuple so the unchanged
`PromptTitleMismatchApprovalAsync` dialog is called exactly as before. Both hash call sites
(`ScanTemplateForSyncAsync`, `ValidateTemplateIntegrityAsync`) now call `ComputeHash`; the private
`ComputeTemplateHash` method was removed. The Hebrew wording
(`"סנכרון תבנית: …"`, `"… חסרים מהתבנית"`, `"… שינויי כותרת"`, `"… סעיפים חדשים בתבנית"`) is byte-for-byte unchanged.
The VM keeps all I/O (`ScanTemplateAsync`), `MessageBox` prompts, persistence (`SyncSectionNamesAsync`),
and `ValidateTemplateIntegrityAsync` orchestration.

**Tests:** added `InspectionTemplateSyncMathTests.cs` (17 tests) covering hash
(empty / order-independence / move-sensitivity / title-and-bracket-text ignored), compare
(missing-from-template, title-mismatch collection, normalized match via case + trim, status-tag
precedence over note-tag, note-tag title fallback, new-codes counting, blank-section-code ignored,
null-safety), and the Hebrew summary composition (null when no change, all-three-categories ordering,
missing-only), via the existing `InternalsVisibleTo("SiNetSQL.Tests")`.

| Deliverable | Status |
| --- | --- |
| `InspectionTemplateSyncMath` extracted (`Compare` + `ComputeHash`) | ✅ |
| VM delegates; `SyncState`/`StatusMessage`/dialog applied in VM; bindings + XAML unchanged | ✅ |
| Private `ComputeTemplateHash` removed; both call sites repointed | ✅ |
| Focused unit tests added (17) | ✅ |
| `dotnet build SiNet.sln` green | ✅ _0 errors_ |
| Full `SiNetProjectManager.sln` (VS MSBuild) green | ✅ _0 errors_ |
| Inspection tests green (112 passed, 0 failed, 2 pre-existing skips) | ✅ |
| No schema / migration / ModelSnapshot / Google / ACC / SQL / Workflow changes | ✅ |
| Report generation + sent/locked actions untouched; legacy window untouched | ✅ |

**Note (build tooling):** `SiNetSQL` uses COM references, so test build/run for this repo must use
**VS MSBuild** + `vstest.console` (`dotnet test` fails with `MSB4803`). `SiNet.sln` (clean stack) still
builds with `dotnet build`.

**Spec check:** no mismatch found. The behavior matches
`Docs/Domains/PlanReview/InspectionReportUiAndLifecycle-2026-06-21.md` (Template Sync On Load / section
sync states); only the internal location of the pure logic changed.

**Remaining Inspection debt:** `FloatingInspectionViewModel` is still large (~5,070 lines). The largest
deferred region is `Sent / Locked report actions` + report generation (explicitly out of scope).
**Recommended next UX-preserving block:** finish the small `Note Helpers` region, or extract the
reviewed-plan/open-file orchestration service noted in Phase 4 — both behind the existing UI.

---

## Inspection New WPF Screen Foundation — Phase 1

**Strategic shift:** the legacy `FloatingInspectionViewModel` window is now a **functional
reference / behavior source / temporary host** — not the long-term UI. The new target is rebuilt
in `SiNet.App.Wpf` in small areas, reusing the extracted services/builders. The old VM and old
XAML are **not** copied wholesale.

**Phase 1 = minimal self-contained foundation (no real reuse yet).** `SiNet.App.Wpf` references
only `SiNet.App.Composition` / `SiNet.Application` / `SiNet.Domain` (not legacy `SiNetSQL`), so the
shell is self-contained; real reuse of the extracted logic is deferred to a bridge phase via a
defined port. Host navigation is unchanged (`MainWindow` stays on Inbox; shell is DI-resolvable but
not shown).

| New file | Role |
| --- | --- |
| `SiNet.Application/Abstractions/Inspection/IInspectionWorkspace.cs` | UI-agnostic port (future LegacyBridge seam). `GetSeriesAsync` + `InspectionSeriesSummary` only. |
| `SiNet.App.Wpf/Inspection/ObservableObject.cs` | Lightweight `INotifyPropertyChanged` base (mirrors Inbox slice; no MVVM toolkit). |
| `SiNet.App.Wpf/Inspection/InspectionTreeViewModel.cs` … `InspectionReportViewModel.cs` | Five sub-area placeholder VMs: Tree, Notes, Drawings, ReviewedPlan, Report. |
| `SiNet.App.Wpf/Inspection/InspectionShellViewModel.cs` | Root VM composing the five injected sub-areas. |
| `SiNet.App.Wpf/Inspection/InspectionShellView.xaml(.cs)` | Tabbed placeholder UserControl bound to the shell VM. |
| `SiNet.App.Wpf/App.xaml.cs` | DI: 5 child VMs + shell VM + view registered (`AddSingleton`), not shown. |

**Feature-migration plan (legacy region → new sub-area → extracted reuse):**

| New sub-area | Legacy source | Extracted pieces to reuse (via `IInspectionWorkspace` / future bridge) |
| --- | --- | --- |
| `InspectionTree` | series/report tree + selection | `InspectionAvailabilityDiagnostics` (read-only) |
| `InspectionNotes` | notes grid + reorder/status | `InspectionNoteHelpers`, `InspectionNoteOrdering`, `InspectionChangeDetector` |
| `InspectionDrawings` | `Drawing Management` | `IInspectionDrawingManagementService`, `InspectionDrawingStampBuilder` |
| `InspectionReviewedPlan` | `Reviewed Plan` A/B | `InspectionReviewedPlanBuilder` |
| `InspectionReport` | report gen + sent/locked | **deferred** — stays in legacy window |

| Deliverable | Status |
| --- | --- |
| `IInspectionWorkspace` port added to `SiNet.Application` | ✅ |
| Shell + 5 sub-area VMs + tabbed view created in `SiNet.App.Wpf` | ✅ |
| DI registration added (shell not shown; legacy untouched) | ✅ |
| `dotnet build SiNet.sln` green | ✅ _0 errors_ |
| Full `SiNetProjectManager.sln` (VS MSBuild) green | ✅ _0 errors_ |
| Legacy Inspection window + XAML unchanged; old VM not copied | ✅ |
| No schema / migration / ModelSnapshot / Google / ACC / SQL / Workflow changes | ✅ |

**Recommended Phase 2:** add a `SiNet.LegacyBridge` adapter implementing `IInspectionWorkspace`
over the extracted `SiNetSQL` services, then migrate the first real area (`InspectionTree` series
list) end-to-end; add host navigation between Inbox and the Inspection shell only once a safe
pattern exists. `Sent / Locked` + report generation remain explicitly deferred.

## Inspection New WPF Screen — Phase 2 (LegacyBridge Tree/series adapter)

The clean `IInspectionWorkspace` port is now backed by a controlled LegacyBridge adapter, mirroring
the established `ILegacyEmailSource` strangler seam. The new app stays free of `SiNetSQL`; only the
legacy WPF host (which already references both worlds) binds the seam to live data.

**Bridge files added (`SiNet.LegacyBridge`):**
- `Inspection/LegacyInspectionSeriesDto.cs` — bridge-local series projection (`SeriesId`, `DisplayName`).
- `Inspection/ILegacyInspectionSource.cs` — host-bound seam, no `SiNetSQL` dependency.
- `Inspection/LegacyInspectionWorkspace.cs` — `IInspectionWorkspace` adapter; empty list when seam unbound.

**Application:** `IInspectionWorkspace` / `InspectionSeriesSummary` reused as-is (no port changes).

**App.Wpf:** `InspectionTreeViewModel` consumes `IInspectionWorkspace` (`LoadSeriesAsync`, `Series`,
`IsLoading`); shell Tree tab lists series. **No `SiNet.App.Wpf -> SiNetSQL` reference.**

**Legacy host:** `ReportServiceLegacyInspectionSource` adapts `IInspectionReportService.GetSeriesForProjectAsync`
(reusing the `סדרה #id (dd/MM/yyyy)` fallback name); registered transient next to the email seam.

| Verification | Status |
| --- | --- |
| `IInspectionWorkspace` adapter resolves through DI (`AddSiNetLegacyBridge`) | ✅ |
| Real series visible in new shell when run from legacy host; empty fallback in new app | ✅ |
| `dotnet build SiNet.sln` green | ✅ _0 errors_ |
| Full `SiNetProjectManager.sln` (VS MSBuild) green | ✅ _0 errors_ |
| Legacy Inspection window untouched; navigation still deferred | ✅ |
| No SiNetSQL ref from App/Bridge; no schema/migration/ACC/Google/SQL/Workflow changes | ✅ |

**Remaining:** Notes, Drawings, ReviewedPlan, Report still placeholders. **Recommended Phase 3:**
migrate report rows under the selected series (read-only) + add safe shell navigation; keep report
generation and sent/locked actions in the legacy host.

## Inspection New WPF Screen — Phase 3 (safe navigation + read-only series detail)

The new shell is now reachable in `SiNet.App.Wpf` via a simple Inbox/Inspection tab switch, and the
Tree area shows read-only report rows under the selected series. All write/generate/sent-locked
behaviour stays in the legacy window; this phase is strictly read-only.

**Navigation:** `MainWindow` Inbox content was wrapped in a 2-tab `TabControl` (Inbox default +
Inspection). `MainViewModel` exposes `Inbox` + `Inspection`; the Inspection tab hosts the
DI-resolved `InspectionShellView` via a `ContentControl`. No MainWindow rewrite, no ServiceLocator.

**Port/DTO:** `IInspectionWorkspace.GetReportsAsync(projectId, seriesId)` + `InspectionReportRow`
(`ReportId`, `ReportNumber`, `InspectionDate`, `InspectorName`) added.

**Bridge/seam:** `LegacyInspectionReportDto` + `ILegacyInspectionSource.GetReportsForSeriesAsync`
added; `LegacyInspectionWorkspace` maps them; legacy-host `ReportServiceLegacyInspectionSource`
adapts `IInspectionReportService.GetReportsForProjectAsync(projectId, seriesId, null)`.

**Read-only data visible:** selectable series list + per-series report rows (round / date /
inspector). New app shows empty (no seam bound); legacy host shows live rows.

| Verification | Status |
| --- | --- |
| Inbox/Inspection tab switch; Inbox default; Inbox still works | ✅ |
| `dotnet build SiNet.sln` green | ✅ _0 errors_ |
| Full `SiNetProjectManager.sln` (VS MSBuild) green | ✅ _0 errors_ |
| Legacy Inspection window untouched; no SiNetSQL ref from App/Bridge | ✅ |

**Remaining placeholder-only:** Notes, Drawings, ReviewedPlan, Report generation. **Recommended
Phase 4:** read-only notes under a selected report (extend port/seam), still no writes; defer report
generation and sent/locked.

---

## Inspection New WPF Screen — Phase 4 (read-only notes for selected report)

Selecting a report row in the Tree area now drives a read-only notes list in the Notes tab. All note
editing/creation/deletion/reordering and status writes stay in the legacy window; this phase is
strictly read-only.

**Selected report:** `InspectionTreeViewModel.SelectedReport` (auto-selects the first row after a
series loads, cleared on reload). `InspectionShellViewModel` observes `SelectedReport` and calls
`InspectionNotesViewModel.LoadNotesAsync(reportId)`; null/0 ⇒ empty list (no report selected).

**Port/DTO:** `IInspectionWorkspace.GetNotesAsync(reportId)` + `InspectionNoteRow`
(`NoteId`, `Number`, `Text`, `Status`) added.

**Bridge/seam:** `LegacyInspectionNoteDto` + `ILegacyInspectionSource.GetNotesForReportAsync` added;
`LegacyInspectionWorkspace` maps them (empty when seam unbound); legacy-host
`ReportServiceLegacyInspectionSource` adapts `IInspectionReportService.GetNotesForReportAsync(reportId)`
(`NoteSubIndex` → Number, `NoteText` → Text, `NoteStatus` → Status).

**Read-only data visible:** notes for the selected report shown as a read-only DataGrid (number /
text / status). New app shows empty (no seam bound); legacy host shows live notes.

| Verification | Status |
| --- | --- |
| Notes follow report selection; empty when none selected; Inbox still works | ✅ |
| `dotnet build SiNet.sln` green | ✅ _0 errors_ |
| Full `SiNetProjectManager.sln` (VS MSBuild) green | ✅ _0 errors_ |
| Legacy Inspection window untouched; no SiNetSQL ref from App/Bridge | ✅ |

**Remaining placeholder-only:** Drawings, ReviewedPlan, Report generation. **Recommended Phase 5:**
read-only Drawings/ReviewedPlan listing for the selected report (extend port/seam), still no writes;
defer report generation and sent/locked.

> **Superseded by the alignment correction below.** The "read-only Drawings/ReviewedPlan"
> recommendation is **paused**. Before adding more tabs/features, the migration must first prove
> UX parity with the legacy `FloatingInspectionView`. See _Inspection Refactoring Alignment_.

---

## Inspection Refactoring Alignment — principle + corrected plan (documentation-only)

**Principle (authoritative):** `Same UX, cleaner internals.` The goal of the Inspection migration is
**not** to redesign the screen. The existing `FloatingInspectionView` UI layout, workflow, terminology,
and visible behavior are the **reference**. Internals (ViewModel size/responsibility split, services,
ports/interfaces, DTOs, LegacyBridge adapters, testable builders, DI) may change; user-facing behavior
**must not** change unless explicitly approved. Any implementation/spec mismatch is **reported**, not
silently redesigned.

**Source-of-truth spec:** `SiNetProjectManagerV2/Docs/Domains/PlanReview/InspectionReportUiAndLifecycle-2026-06-21.md`
(scope: `FloatingInspectionView`, lifecycle, sections/notes, sent/locked) is the documented UX baseline.
Supporting: `PlanReview/PlanReviewPrinciples-*`, `UI/UiPrinciples-*`, `Inspection-Template-Guide.md`,
and the `Migration/GoogleSheetReview*` designs. **Spec gap:** none of these define the new tabbed
`InspectionShellView` as the target UX — so the tabbed shell is **not** spec-backed and stays a harness.

**Status of `src/SiNet.App.Wpf/Inspection/InspectionShellView.xaml`:** **technical prototype / wiring
harness only.** It is LTR, ~900×600, tab-based (Tree/Notes/Drawings/ReviewedPlan/Report), read-only
DataGrids. The legacy window is RTL, frameless floating (`WindowStyle=None`, `AllowsTransparency`,
topmost toggle, ~420×850), single-pane **card** flow with create-bar + action-bar (templates, series,
inspector, Create Report, Mark-Response-Received, Re-pull, Open-source, Unlock, Share-with-link,
reviewed-version). **These differ in layout, orientation, structure, and density — the tabbed shell is
NOT the target UI and must not be promoted to one without explicit approval.**

**Corrected migration plan (replaces "read-only Drawings/ReviewedPlan next"):**

1. Keep the legacy `FloatingInspectionView` as the visual/behavior reference; do not delete/replace it.
2. Continue the *internals-only* track already in flight (Inspection decomposition Phases 2–4: pure
   helpers/builders + injectable services behind the existing VM), keeping XAML/bindings unchanged.
3. Treat the new shell strictly as an additive, admin-gated **preview harness** for validating clean
   ports (`IInspectionWorkspace`) against live data — not as the future screen.
4. Defer any new shell tabs/features (Drawings/ReviewedPlan/Report) until a UX-parity rebuild of the
   legacy layout is explicitly approved; when approved, rebuild the **same** RTL card layout on top of
   the clean ports, not the tabbed prototype.
5. Do not change report generation or sent/locked actions; keep both `SiNet.sln` and
   `SiNetProjectManager.sln` green.

> **Updated by _Refactor Strategy — Workflow-first Fast Track_ (top of file).** The
> `same UX, cleaner internals` principle and the existing read-only phases above remain **valid**.
> What changes is the *priority*: the next near-term Inspection work is the **task-mode / workflow
> integration** vertical slice below — not more read-only tabs. The tabbed `InspectionShellView`
> stays a harness; any production UX rebuild still needs explicit approval.

---

## Inspection New WPF Screen — Task Mode / Workflow Integration (planned)

**Why now:** under the Workflow-first fast track, Inspection's value to the *process* is that a
workflow task can open the **exact** report it targets, the user completes the review, and that
completion flows back into workflow auto-advance. This is a **vertical slice**, not another
read-only tab. The existing read-only phases (1–4) stay valid as behavior scaffolding.

**Scope of this phase:**

* New Inspection shell can **open from a task**.
* Opening is resolved through `ITaskNavigationService` (UI must not hand-build the target).
* The shell receives an explicit `WorkSurfaceContext` (project / task / workflow / report target).
* It opens the **exact report from the task target** (`PrimaryWorkTargetEntityId`).
  **No fallback** to first/last report.
* Report work completion goes through `ITaskCompletionService` (record result + close per policy).
* Workflow auto-advance goes through `IWorkflowCommandService.CheckAndAutoAdvanceAsync` — never
  mutated from the Inspection ViewModel.
* Legacy `FloatingInspectionViewModel` remains the **behavior reference**, not the long-term
  implementation.

```plaintext
Task → ITaskNavigationService → WorkSurfaceContext(ComponentKey = "Inspection", PrimaryWorkTargetEntityId = reportId)
     → Inspection shell opens that exact report
     → user completes review
     → ITaskCompletionService (record result, close task)
     → IWorkflowCommandService.CheckAndAutoAdvanceAsync
```

**Vertical-slice done criteria:** shell opens from a task, receives context, loads the target
report, performs one real completion action, completes the task and triggers workflow auto-advance
through the official services, and both solutions still build.

---

## Workflow-first backbone — Slice 1: ports + LegacyBridge seams + Inspection task navigation 🟡

> First executable step of the Workflow-first fast track. Establishes the **process backbone
> primitives** (`WorkSurfaceContext` + task ports) and the first navigation half of the Inspection
> task-mode vertical slice. Read access stays through `IWorkflowQueryService`; workflow mutation
> stays exclusively behind `IWorkflowCommandService`. Task completion is bridged but its concrete
> legacy backing is left to the legacy host (the new app degrades gracefully). No schema/migration/
> `ModelSnapshot` edits; no Gmail/ACC/SQL-composition/Workflow-engine changes; the legacy
> `FloatingInspectionViewModel` is untouched.

**Application layer (new ports + runtime context):**

| Artifact | Detail |
| --- | --- |
| `src/SiNet.Application/WorkSurfaces/WorkSurfaceContext.cs` *(new)* | Runtime-only record `(int? TaskId, int ProjectId, int? WorkflowInstanceId, string ComponentKey, int? PrimaryWorkTargetEntityId, IReadOnlyList<string> AllowedResultCodes)`. No DB table. Screens consume it instead of guessing how they were opened. |
| `src/SiNet.Application/Tasks/ITaskQueryService.cs`, `ITaskCommandService.cs` *(new)* | Placeholder read/write task ports (members deferred) so the boundary exists for the next slices. |
| `src/SiNet.Application/Tasks/ITaskNavigationService.cs` *(new)* | `ResolveAsync(int taskId, CancellationToken) → WorkSurfaceContext?`. The only sanctioned way to turn a task id into a navigation target; UI must not hand-build context. |
| `src/SiNet.Application/Tasks/ITaskCompletionService.cs` *(new)* | `CompleteAsync(CompleteTaskCommand, CancellationToken) → TaskCompletionResultDto`. The bridge back into workflow; records completion and routes auto-advance through `IWorkflowCommandService`. |
| `src/SiNet.Application/Tasks/CompleteTaskCommand.cs` *(new)* | UI-agnostic completion command (`TaskId`, `CompletionEventCode`, `TaskResultCode?`, `CompletedTaskLinkIds?`, `UserId`). |
| `src/SiNet.Application/Tasks/TaskCompletionResultDto.cs` *(new)* | Completion result; carries the workflow auto-advance outcome via the existing `StageCompletionResultDto`. `Failure(...)` (business) vs `Unavailable(...)` (no backend bound). |

**LegacyBridge seams + strangler adapters (`SiNet.LegacyBridge`, Application+Domain only — no `SiNetSQL`):**

| Artifact | Detail |
| --- | --- |
| `Tasks/LegacyTaskNavigationRequestDto.cs` *(new)* | Bridge-local projection of the legacy `TaskNavigationRequest` (`long? PrimaryWorkTargetEntityId`, `IsSuccess`, …) — no `SiNetSQL` types. |
| `Tasks/ILegacyTaskNavigationSource.cs` *(new)* | Host-bound seam over `TaskNavigationResolver`; unbound in the new app. |
| `Tasks/LegacyTaskNavigationService.cs` *(new)* | Implements `ITaskNavigationService`; maps the seam DTO → `WorkSurfaceContext` (clamps `long`→`int?`, never truncating into a different valid id). Returns `null` when the seam is unbound **or** the resolver failed — caller shows a clear error, never guesses a target. |
| `Tasks/LegacyTaskCompletionResultDto.cs`, `LegacyCompleteTaskCommandDto.cs`, `ILegacyTaskCompletionSource.cs` *(new)* | Bridge-local completion seam mirroring `TaskCompletionCoordinator.CompleteAsync`; the legacy coordinator already routes auto-advance through `IWorkflowCommandService.CheckAndAutoAdvanceAsync`. |
| `Tasks/LegacyTaskCompletionService.cs` *(new)* | Implements `ITaskCompletionService`; returns `Unavailable` when the seam is unbound, else projects the legacy result (incl. `StageAdvanceResult`). |
| `LegacyBridgeServiceCollectionExtensions.cs` | `AddSiNetLegacyBridge()` now also registers `ITaskNavigationService → LegacyTaskNavigationService` and `ITaskCompletionService → LegacyTaskCompletionService` (both seams optional → graceful degradation). |

**Inspection task-mode (navigation half wired; completion pending a bound seam):**

| Artifact | Detail |
| --- | --- |
| `src/SiNet.App.Wpf/Inspection/InspectionTreeViewModel.cs` | New `SelectReportByIdAsync(projectId, reportId)`: scans the project's series for the **exact** report and selects it; **no first/last fallback** — returns `false` and selects nothing when the target is absent. The existing read-only first-item default path (`LoadReportsAsync`) is unchanged. |
| `src/SiNet.App.Wpf/Inspection/InspectionShellViewModel.cs` | Injects `ITaskNavigationService`. New `OpenFromTaskAsync(int taskId)` resolves `WorkSurfaceContext`, validates `ComponentKey == "Inspection"`, requires a concrete `PrimaryWorkTargetEntityId`, and opens that exact report. Exposes `IsTaskMode` / `IsBusy` / `TaskStatusMessage` / `Context`. The shell never mutates workflow. |
| `src/SiNet.App.Wpf/Inspection/InspectionShellView.xaml` | Added a task-mode status banner (visible only in task mode) showing `TaskStatusMessage`; the read-only tabs are otherwise unchanged. |

**Deferred (next slices, by design):**

* Actual task **completion** from the new shell — needs a real `ILegacyTaskCompletionSource` bound
  by the legacy host (or a native infrastructure completion service); until then completion reports
  `Unavailable`.
* A host entry point that calls `OpenFromTaskAsync` from a real workflow-created task (task-queue UI
  → Inspection surface).
* `ITaskQueryService` / `ITaskCommandService` member definitions and implementations.
* Legacy-host binding of `ILegacyTaskNavigationSource` to `TaskNavigationResolver`.

| Deliverable | Status |
| --- | --- |
| `WorkSurfaceContext` added (runtime-only, exact doc shape) | ✅ |
| Task navigation/completion ports + command/result DTOs added | ✅ |
| LegacyBridge task seams + strangler adapters added (no `SiNetSQL` dependency) | ✅ |
| Adapters degrade gracefully when seam unbound (null nav / `Unavailable` completion) | ✅ |
| Task adapters registered in `AddSiNetLegacyBridge()` | ✅ |
| Inspection shell opens an **exact** report from `WorkSurfaceContext`; no first/last fallback | ✅ |
| Inspection task-mode status surfaced in the shell view | ✅ |
| `dotnet build SiNet.sln` green | ✅ _0 errors_ |
| No schema / migration / `ModelSnapshot` edits | ✅ |
| No Gmail / ACC / SQL-composition / Workflow-engine changes; legacy Inspection window untouched | ✅ |
| Task completion from the new shell (needs bound legacy seam) | 🟡 Deferred |
| Host entry point opening Inspection from a real task | 🟡 Deferred |

---

## Email Management — Workflow/Task Integration Plan (planned)

**Clarification:** the **native Gmail read infrastructure** (`SiNet.Infrastructure.Google`,
`GmailClientProvider`) is **not** the same thing as the full **Email Management screen**. Read
infrastructure is plumbing; the Email Management screen is a **Work Surface** that turns email
context into workflow/task/file actions.

**Target model:**

```plaintext
Email screen
- displays inbox/list/viewer/search/filter state.

EmailContextService
- analyzes email/project/attachments/workflow family.

SuggestedActionsService
- builds available actions from context.

ActionExecutionService
- executes selected action.

Workflow actions
- must call IWorkflowCommandService.

Task actions
- must call task services.

File/project actions
- must call ProjectFileFilingService / ProjectWork services.

The Email ViewModel must not own Gmail, ACC, Workflow, Task and Project filing logic directly.
```

**Boundary rule:** the Email screen *requests* actions; it does not *perform* workflow, task, ACC,
or filing work inline. Each action category routes to its owning service (Workflow →
`IWorkflowCommandService`; Task → task services; File/project → `ProjectFileFilingService` /
ProjectWork services).

---

## ProjectWork / Files (formal domain)

ProjectWork is promoted to a formal domain: a **Work Surface** over the project file/folder model,
with all filing/placement logic behind domain services, and **no** direct workflow control.

| Domain | Legacy source | New home | Status | Notes |
| --- | --- | --- | --- | --- |
| ProjectWork / Files | Legacy ProjectWork window(s) + scattered file/filing logic | ProjectWork **Work Surface** + `ProjectFileFilingService` / `FileIndex` domain services; `SiNet.Infrastructure.*` for IO (ACC, Drive, FileSystem) | ⬜ | Screen is a work surface only; filing/placement/versioning/naming live in services; ProjectWork **must not** advance workflow directly. See target below. |

**Target responsibilities:**

```plaintext
ProjectWork screen
- display project file/folder tree.
- show versions/alternatives.
- open files.
- show ACC viewer.
- support drag/drop as a work surface.

ProjectFile services
- perform filing, placement, versioning, naming, storage-destination logic.

Workflow
- receives task completion / process events.
- ProjectWork must not advance workflow directly.
```

**Existing principle (carried over from the active ProjectWork docs —
`SiNetProjectManagerV2/Docs/Domains/ProjectWork/ProjectWorkWindow2-2026-06-19.md`):**

```plaintext
ProjectWork is not responsible for Workflow management logic.
MoveToProject belongs outside the screen, in ProjectFileFilingService or equivalent.
```

---

## Recovery points

- **Frozen reference:** `Before_refactoring` 🔒 — never modify; restore any old file from here.
- **Working branch:** `SiWorkNet10`.
