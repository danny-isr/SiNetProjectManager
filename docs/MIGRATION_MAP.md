# SiNet Migration Map

> **Working branch:** `SiWorkNet10` · **Frozen reference (recover old code here):** `Before_refactoring`
> Tracks the move from the legacy `SiNetProjectManager.sln` into the clean `SiNet.sln`.

**Status legend:** ⬜ Not started · 🟡 In progress · ✅ Replaced (old path retired) · 🔒 Frozen reference

A legacy path may be retired **only** when: the new path compiles, the UI flow works, tests
exist/updated, and its row below is marked **✅ Replaced**.

---

## Domains

| Domain | Legacy source (approx. size) | New home | Status | Notes |
| --- | --- | --- | --- | --- |
| Email / Google | `SiOffice.GoogleConnector/GoogleService.cs` (~3,369), `EmailManagementViewModel.cs` (~6,909) | `SiNet.Application` `IEmail*` ports → `SiNet.Infrastructure.Google` (**native Gmail API**) | 🟢 | **Accepted; native sign-in + real config done** (no `LegacyBridge`). Read-only; deferred: send/modify only. See sections below. |
| Workflow | `WorkflowManagementWindow.xaml.cs` (~3,547) | `SiNet.Application` `IWorkflow*` → App.Wpf VM + services | 🟡 | **Read slice extracted.** `WorkflowQueryService` + `ProjectWorkflowPolicyService` moved into `SiNet.Infrastructure.Sql` behind `IWorkflowQueryService` / `IProjectWorkflowPolicyService` ports (namespaces preserved). Writes/engine deferred. See section below. |
| Inspection | `FloatingInspectionViewModel.cs` (~5,384) | `SiNet.Application` `IInspection*` → services + VM | ⬜ | Split list/report/notes/photos. |
| ACC / Autodesk | `SiOffice.AutodeskConnector/Bim360Service.cs` (~3,231) | `SiNet.Application` `IAcc*` ports → `SiNet.Infrastructure.Autodesk` (+ `LegacyBridge`) | ⬜ | ACC remains source of truth; DB is cache/helper. |
| SQL / DbContext | `SiNetSQL` (`DbContext` ~1,948) | `SiNet.Infrastructure.Sql` via `IDbContextFactory<>` | 🟡 | **EF layer extracted** (models, configs, DbContext, factory, migrations) into clean `net10.0` module; legacy `SiNetSQL` references it. **Do not** edit migrations / `ModelSnapshot` / `*.Designer.cs`. See section below. |
| App startup / DI | `App.xaml.cs` (~1,806), `ConfigureServices()` (~690) | `SiNet.App.Composition` + `SiNet.App.Wpf` | ⬜ | Modular `AddSiNet*` extensions. |
| File system | `FileHelpers` / scattered IO | `SiNet.Application` `IFileStorage` → `SiNet.Infrastructure.FileSystem` | ⬜ | |
| Logging | scattered Serilog usage | `SiNet.Application` `IAppLogger` → `SiNet.Infrastructure.Logging` | ⬜ | |

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

## Recovery points

- **Frozen reference:** `Before_refactoring` 🔒 — never modify; restore any old file from here.
- **Working branch:** `SiWorkNet10`.
