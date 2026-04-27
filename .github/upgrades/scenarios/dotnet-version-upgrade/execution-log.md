
## [2026-04-27 17:38] 01-prerequisites

Verified .NET 10 SDK is available (10.0.202 installed alongside 9.0.308). No global.json files exist in any of the 4 repositories — nothing to update. Solution loads with .NET 10 SDK detected. Ready to proceed with TFM updates.


## [2026-04-27 17:40] 02-update-tfms

Updated TargetFramework in all 10 .csproj files from net8.0/net8.0-windows to net10.0/net10.0-windows. Preserved platform suffix per project (windows kept where present). Also corrected BenchmarkSuite1.csproj which incorrectly used plural `<TargetFrameworks>` for a single-targeting scenario — converted to singular `<TargetFramework>`. `dotnet restore` succeeds for all 10 projects across the 4 repositories with no NuGet errors.


## [2026-04-27 17:43] 03-update-packages

Bumped NuGet packages across 6 project files: Microsoft.EntityFrameworkCore family 8.0.12→10.0.7 (and InMemory, SqlServer, Design, Tools), Microsoft.Extensions.* family 8.0.x→10.0.7 (Configuration, DependencyInjection, Hosting.WindowsServices, Http, Logging, etc.), System.DirectoryServices.AccountManagement 8.0.1→10.0.7, Serilog.AspNetCore 8.0.3→10.0.0, Serilog.Extensions.Logging 8.0.0→10.0.0, xunit 2.6.6→2.9.3 (resolves deprecation), xunit.runner.visualstudio 2.5.6→2.8.2, Microsoft.NET.Test.Sdk 17.8.0→17.12.0. Resolved transitive downgrade conflicts: Serilog 3.1.1→4.2.0 (required by Serilog.Extensions.Logging 10), Serilog.Sinks.File 5.0.0/6.0.0→7.0.0 (required by Serilog.AspNetCore 10). Microsoft.Xaml.Behaviors.Wpf left at 1.1.135 — newer than the assessment-suggested 1.1.39 and confirmed compatible. `dotnet restore` succeeds for full solution with no errors.


## [2026-04-27 17:45] 04-fix-build-issues

Built full solution against net10.0/net10.0-windows. Only one real .NET 10 source-incompat issue surfaced: SYSLIB0060 in `SiNetProjectManagerV2/Services/SecretProvisioningService.cs` — the `Rfc2898DeriveBytes` constructor is obsolete in .NET 10. Replaced 2 ctor usages (export and import paths) with the static `Rfc2898DeriveBytes.Pbkdf2` method, preserving the same parameters (password, salt, iterations, SHA256, key size). Note: `dotnet build` from the CLI fails on `SiNetSQL.csproj` due to its `<COMReference Include="IWshRuntimeLibrary">` (Core MSBuild doesn't support `ResolveComReference`) — this is a pre-existing, environment-level limitation, not a .NET 10 issue. Building via Visual Studio's full MSBuild succeeds with 0 errors / 0 warnings across all 10 projects. The 2,704 binary-incompat issues flagged by assessment all resolved automatically on recompile, as expected.


## [2026-04-27 17:47] 05-run-tests

Ran both test projects against the upgraded net10.0-windows binaries via vstest.console (Test Explorer cache was stale and pointed to net8.0 paths). Results:\n- SiNetSQL.Tests: 33 passed, 2 skipped, 0 failed\n- SiNetSQL.E2ETests: 14 passed, 2 failed\n\nThe 2 E2E failures are environment/configuration issues unrelated to the .NET 10 upgrade: `GmailTokenBootstrapTest.Bootstrap_GmailTokens` fails because `credentials.json` is not present at the expected path, and `ManualAcceptanceTestSender.SendAcceptanceTestEmails` fails downstream because Gmail credentials are not bootstrapped. Both depend on external Google API credentials. No regressions detected from the framework upgrade — every test that doesn't require external Google credentials passes on net10.

