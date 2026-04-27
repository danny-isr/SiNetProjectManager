# .NET 8 → .NET 10 Upgrade Plan

## Overview

**Target**: Upgrade SiNetProjectManager solution from `.NET 8` to `.NET 10` (LTS).
**Scope**: 10 projects across 4 git repositories (`SiNetProjectManager_GitHub`, `SiNetSQL`, `SiOffice.AutodeskConnector`, `SiOffice.GoogleConnector`), uniform `net8.0` / `net8.0-windows` baseline, ~462K LOC.

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: 10 projects, uniform TFM baseline (`net8.0`), clear dependency structure (4 levels), no major API-breaking changes between .NET 8 and .NET 10, all NuGet packages have clear compatible target versions.

### Projects in Scope

**Repo: SiNetProjectManager_GitHub**
- `SiNetProjectManagerV2.csproj` (WPF, `net8.0-windows`)
- `SiMasterPlanWeb.csproj` (ClassLibrary, `net8.0`)
- `MasterPlan.SyncEngine.csproj` (DotNetCoreApp, `net8.0`)
- `SiOffice.AccService.csproj` (AspNetCore, `net8.0-windows`)
- `BenchmarkSuite1.csproj` (DotNetCoreApp, `net8.0-windows`)

**Repo: SiNetSQL**
- `SiNetSQL.csproj` (ClassLibrary, `net8.0-windows`)
- `SiNetSQL.Tests.csproj` (WPF/Test, `net8.0-windows`)
- `SiNetSQL.E2ETests.csproj` (DotNetCoreApp/Test, `net8.0-windows`)

**Repo: SiOffice.AutodeskConnector**
- `SiOffice.AutodeskConnector.csproj` (ClassLibrary, `net8.0`)

**Repo: SiOffice.GoogleConnector**
- `SiOffice.GoogleConnector.csproj` (WPF, `net8.0-windows`)

## Tasks

### 01-prerequisites: Verify SDK and tooling

Confirm .NET 10 SDK is installed and accessible to the build, and that any `global.json` files in the four repositories either don't pin an older SDK or are updated to allow .NET 10. Verify Visual Studio version supports .NET 10.

**Done when**: `dotnet --list-sdks` shows a 10.x SDK; no `global.json` blocks .NET 10; solution opens without SDK warnings.

---

### 02-update-tfms: Update target frameworks for all projects

Update the `<TargetFramework>` element in every project file from `net8.0` / `net8.0-windows` to `net10.0` / `net10.0-windows` (preserving the platform suffix). This is a uniform bump — no project requires multi-targeting.

Affects all 10 `.csproj` files across the 4 repositories.

**Done when**: Every project file declares `net10.0` or `net10.0-windows`; `dotnet restore` succeeds for the full solution.

---

### 03-update-packages: Update NuGet packages to .NET 10 versions

Bump packages flagged by the assessment to versions targeting `net10.0`:

- **Microsoft.EntityFrameworkCore** (and `.Design`, `.InMemory`, `.SqlServer`, `.Tools`): `8.0.12` → `10.0.x`
- **Microsoft.Extensions.*** (Configuration, DependencyInjection, Hosting.WindowsServices, Http, Logging, etc.): `8.0.x` → `10.0.x`
- **System.DirectoryServices.AccountManagement**: `8.0.1` → `10.0.x`
- **xunit**: `2.6.6` (deprecated) → latest stable `2.x` (or evaluate `xunit.v3` separately)
- **Microsoft.Xaml.Behaviors.Wpf**: assessment flags incompatibility — verify on NuGet whether current `1.1.135` already supports `net10.0-windows`; only act if a compatible newer version exists

Other packages (`Dapper`, `BenchmarkDotNet`, `Newtonsoft.Json`, `Serilog*`, `RestSharp`, `PDFsharp`, `Google.Apis.*`, `Microsoft.Data.SqlClient`, `PDFsharp`, `Microsoft.Web.WebView2`, `Extended.Wpf.Toolkit`, `Microsoft.SqlServer.SqlManagementObjects`, `Microsoft-WindowsAPICodePack-Shell`) were marked compatible — leave unless they cause build/runtime issues.

**Done when**: All flagged packages updated to .NET 10–compatible versions; `dotnet restore` succeeds; no incompatible-package warnings.

---

### 04-fix-build-issues: Resolve source-incompatibility and behavioral issues

Address the 43 source-incompatible (`Api.0002`) and 74 behavioral-change (`Api.0003`) issues identified by the assessment. Most are concentrated in `SiNetSQL`, `MasterPlan.SyncEngine`, `SiOffice.GoogleConnector`, and `SiOffice.AutodeskConnector`. Build the full solution and fix every compilation error in a single bounded pass; investigate each behavioral warning surfaced at runtime if encountered during smoke tests.

The 2,704 binary-incompatibility issues (`Api.0001`) typically resolve automatically on recompile and require no source changes — only act on those that surface as actual compiler errors.

**Done when**: `dotnet build SiNetProjectManager.sln` succeeds with 0 errors across all 10 projects; warnings related to deprecated APIs are reviewed (suppress only with justification).

---

### 05-run-tests: Validate with test suites

Run the two test projects (`SiNetSQL.Tests`, `SiNetSQL.E2ETests`) on the upgraded solution. Address any test failures introduced by the upgrade (typically driven by behavioral changes in EF Core, `System.Text.Json`, or SqlClient).

**Done when**: All tests pass on the upgraded solution; any new failures are either fixed or documented with rationale.
