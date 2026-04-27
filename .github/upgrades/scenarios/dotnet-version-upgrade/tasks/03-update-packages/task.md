# 03-update-packages: Update NuGet packages to .NET 10 versions

Bump packages flagged by the assessment to versions targeting `net10.0`:

- **Microsoft.EntityFrameworkCore** (and `.Design`, `.InMemory`, `.SqlServer`, `.Tools`): `8.0.12` → `10.0.x`
- **Microsoft.Extensions.*** (Configuration, DependencyInjection, Hosting.WindowsServices, Http, Logging, etc.): `8.0.x` → `10.0.x`
- **System.DirectoryServices.AccountManagement**: `8.0.1` → `10.0.x`
- **xunit**: `2.6.6` (deprecated) → latest stable `2.x` (or evaluate `xunit.v3` separately)
- **Microsoft.Xaml.Behaviors.Wpf**: assessment flags incompatibility — verify on NuGet whether current `1.1.135` already supports `net10.0-windows`; only act if a compatible newer version exists

Other packages (`Dapper`, `BenchmarkDotNet`, `Newtonsoft.Json`, `Serilog*`, `RestSharp`, `PDFsharp`, `Google.Apis.*`, `Microsoft.Data.SqlClient`, `PDFsharp`, `Microsoft.Web.WebView2`, `Extended.Wpf.Toolkit`, `Microsoft.SqlServer.SqlManagementObjects`, `Microsoft-WindowsAPICodePack-Shell`) were marked compatible — leave unless they cause build/runtime issues.

**Done when**: All flagged packages updated to .NET 10–compatible versions; `dotnet restore` succeeds; no incompatible-package warnings.
