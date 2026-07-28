# Standalone New System host — target state

> Status: **Approved — slice 1 implemented (bootable standalone shell)**  
> Date: 2026-07-28  
> Decision context: operator chose **standalone New System host** as the first step toward
> removing runtime dependence on `SiNetSQL` / `SiNetProjectManagerV2`.
>
> Locked decisions (2026-07-28):
> 1. New System = `SiNet.App.Wpf.exe` only; Legacy = `SiNetProjectManagerV2.exe` only.
> 2. V2 New System path: **deprecated + logged** (not deleted in slice 1).
> 3. Surfaces without a native adapter: **hidden from the shell menu** until reimplemented.
>
> Slice 1 delivery:
> - Entry: `src/SiNet.App.Wpf/App.xaml.cs` → vault gate → schema gate → Windows user auth →
>   `INewShellFactory.CreateShellAsync()` / `NewShellWindow`
> - Composition: `AddSiNetStandaloneHost` (`StandaloneHostServiceCollectionExtensions`)
> - Identity/schema: `AddSiNetIdentitySql` in `SiNet.Infrastructure.Sql`
> - Launch: `dotnet run --project src/SiNet.App.Wpf`

## Goal

The process that operators call "New System" must be a **self-contained WPF host**:

- Entry point: `SiNet.App.Wpf` (`OutputType=WinExe`)
- Composition: `AddSiNet(SiNetHostMode.StandaloneNew, …)` + `AddSiNetSecrets()` + SQL from vault
- Shell: `INewShellFactory` / `NewShellWindow` (not V2 `MainWindow`, not Legacy mode picker inside V2)
- **No** `ProjectReference` to `SiNetSQL` or `SiNetProjectManagerV2`
- **No** LegacyBridge registration
- **No** startup path that opens `WPF_Window.SecretSetupWindow` / other V2 dialogs

`SiNetProjectManagerV2` remains available temporarily as **Legacy mode only** (frozen strangler),
not as the New System process host.

## Current reality (why we are not there yet)

| Piece | Today |
| --- | --- |
| Operator entry | `SiNetProjectManagerV2.exe` → mode picker → `RunNewSystemStartup` |
| New System DI | Built inside V2 via `AddSiNet(V2Hybrid)` + V2-only host adapters |
| Shell | `NewShellFactory` resolved from V2's `ServiceProvider` |
| Standalone harness | `SiNet.App.Wpf/App.xaml.cs` exists but opens scaffold `MainWindow`, not production shell; incomplete startup (no mode picker parity, limited schema/vault UX) |
| Host-bound ports | Many ports still implemented by V2 types (`V2Inspection*`, `V2ProjectWorkSurfaceHost`, `LegacyLoggingRuntimeApplier`, AD lookup, health source, …) |

`SiNetHostMode.StandaloneNew` already exists in Composition and **does not** register LegacyBridge.
The missing work is making that mode the **real** production host.

## Target process shape

```text
SiNet.App.Wpf.exe
  → BuildConfiguration (own appsettings + env; DB overrides for AccService BaseUrl/pins)
  → AddSiNet(StandaloneNew) + AddSiNetSecrets + vault SQL factory
  → Native vault/DB gate (native Secret Setup / provisioning only)
  → Schema gate
  → Authorize current user
  → INewShellFactory.CreateShellAsync() → NewShellWindow
```

`StartupModeSelectionWindow` may move into App.Wpf, or be dropped if this exe **is** New System
only (Legacy stays on V2.exe). **Recommendation:** this exe = New System only; Legacy remains
`SiNetProjectManagerV2.exe` with no New System path (or New System path redirects / is removed later).

## Scope for the first implementation slice (proposed)

**In scope (slice 1 — "bootable standalone shell"):**

1. Promote `SiNet.App.Wpf` App startup to production shape:
   - Serilog / central logging via existing native ports
   - Vault readiness + native Secret Setup when incomplete
   - SQL `IDbContextFactory` from vault (already partially present)
   - Schema validation before shell
   - Resolve `INewShellFactory` and show `NewShellWindow`
2. Register in App.Wpf (or a small `SiNet.App.Wpf` composition helper) every host adapter that
   today lives only under V2 **when a native/no-op/SQL implementation already exists**; for ports
   that have only V2 implementations, register explicit **closed/no-op** adapters that fail
   clearly in the UI (no silent V2 calls).
3. Document launch: Debug profile / shortcut for `SiNet.App.Wpf` as New System; V2 = Legacy.
4. Boundary tests: App.Wpf csproj must not reference SiNetSQL/V2; startup source must not open
   legacy SecretSetup window.

**Out of scope for slice 1 (follow-on):**

- AccService decoupling from SiNetSQL (separate track)
- MasterPlan Shared de-vendor
- Renaming `SiNetSQL.*` namespaces inside Infrastructure.Sql
- Feature parity for every V2 host adapter (Inspection export hosts, etc.) — those surfaces stay
  menu-hidden or show "not available in standalone" until ports are reimplemented
- Deleting V2 New System path immediately (deprecate first; remove after pilot)

## Host adapters — policy

| Port category | Slice 1 policy |
| --- | --- |
| Already have SQL/native impl | Register those |
| V2-only, non-critical for pilot menu | No-op / "unavailable" adapter + keep menu gated |
| V2-only, required for pilot | Must be reimplemented or ported before claiming parity — listed in a checklist before cutover |

Pilot menu surfaces from `NEW_SYSTEM_BOUNDARY.md` remain the acceptance bar; anything that still
needs V2 adapters is either reimplemented or explicitly deferred with UI messaging.

## Non-goals

- Rewriting AccService in this slice
- Forcing MasterPlan off Shared copies in this slice
- History rewrite / DB schema changes
- Making V2 and App.Wpf share one exe

## Success criteria (slice 1)

1. `dotnet run --project src/SiNet.App.Wpf` starts NewShell without loading SiNetSQL assembly.
2. Vault + schema gates work with native UI only.
3. Core pilot menus open (Email, Settings, Secret Setup, ACC status as already native).
4. V2 New System path marked deprecated in docs; default developer launch doc points at App.Wpf.
5. Build/tests green; new boundary tests enforce no SiNetSQL/V2 references from App.Wpf.

## Decisions (locked)

1. **Exe strategy:** New System = only `SiNet.App.Wpf.exe`; Legacy = only V2.exe.
2. **V2 New System path:** deprecate + log in slice 1; remove in a later slice after pilot.
3. **Missing native adapters:** hide those menu entries until a native implementation exists.
