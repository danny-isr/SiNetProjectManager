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
| Email / Google | `SiOffice.GoogleConnector/GoogleService.cs` (~3,369), `EmailManagementViewModel.cs` (~6,909) | `SiNet.Application` `IEmail*` ports → `SiNet.Infrastructure.Google` (+ `LegacyBridge`) | ⬜ | First domain to migrate. Remove WPF (`Brush`) from connector. |
| Workflow | `WorkflowManagementWindow.xaml.cs` (~3,548) | `SiNet.Application` `IWorkflow*` → App.Wpf VM + services | ⬜ | Extract DTOs and business logic out of code-behind. |
| Inspection | `FloatingInspectionViewModel.cs` (~5,384) | `SiNet.Application` `IInspection*` → services + VM | ⬜ | Split list/report/notes/photos. |
| ACC / Autodesk | `SiOffice.AutodeskConnector/Bim360Service.cs` (~3,231) | `SiNet.Application` `IAcc*` ports → `SiNet.Infrastructure.Autodesk` (+ `LegacyBridge`) | ⬜ | ACC remains source of truth; DB is cache/helper. |
| SQL / DbContext | `SiNetSQL` (`DbContext` ~1,948) | `SiNet.Infrastructure.Sql` via `IDbContextFactory<>` | ⬜ | **Do not** edit migrations / `ModelSnapshot` / `*.Designer.cs`. |
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

## Recovery points

- **Frozen reference:** `Before_refactoring` 🔒 — never modify; restore any old file from here.
- **Working branch:** `SiWorkNet10`.
