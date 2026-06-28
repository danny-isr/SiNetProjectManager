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

---

## Recovery points

- **Frozen reference:** `Before_refactoring` 🔒 — never modify; restore any old file from here.
- **Working branch:** `SiWorkNet10`.
