# Standalone New System host — target state

> Status: **Approved — slice 2 implemented (pilot parity)**  
> Date: 2026-07-28  
> Decision context: operator chose **standalone New System host** as the first step toward
> removing runtime dependence on `SiNetSQL` / `SiNetProjectManagerV2`.
>
> Locked decisions (2026-07-28):
> 1. New System = `SiNet.App.Wpf.exe` only; Legacy = `SiNetProjectManagerV2.exe` only.
> 2. V2 New System path: **deprecated + logged** (not deleted in slice 1).
> 3. Surfaces without a native adapter: **hidden from the shell menu** until reimplemented.
>
> Slice 1 delivery (done):
> - Entry: `src/SiNet.App.Wpf/App.xaml.cs` → vault gate → schema gate → Windows user auth →
>   `INewShellFactory.CreateShellAsync()` / `NewShellWindow`
> - Composition: `AddSiNetStandaloneHost` (`StandaloneHostServiceCollectionExtensions`)
> - Identity/schema: `AddSiNetIdentitySql` in `SiNet.Infrastructure.Sql`
> - Launch: `dotnet run --project src/SiNet.App.Wpf`
>
> Local MultiStart (Visual Studio): `SiNet.sln` includes `SiOffice.AccService`. Shared profile
> `SiNet.slnLaunch` → **New System + AccService** starts AccService then `SiNet.App.Wpf`
> (enable *Multi-Project Launch Profiles* in VS Preview Features if the dropdown is missing).
>
> Limited production pilot envelope (what may be exposed to internal users):
> [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md).

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

## Target process shape

```text
SiNet.App.Wpf.exe
  → BuildConfiguration (own appsettings + env; DB overrides for AccService BaseUrl/pins)
  → AddSiNet(StandaloneNew) + AddSiNetSecrets + vault SQL factory
  → Native vault/DB gate (native Secret Setup / provisioning only)
  → Schema gate
  → Authorize current user
  → Apply AccService BaseUrl/pins from system settings
  → INewShellFactory.CreateShellAsync() → NewShellWindow
```

## Slice 2 — pilot parity (in scope)

Strengthen the standalone host so pilot menus have working ACC/AD/Inspection-list glue
without V2 runtime adapters:

1. Vault-backed `ITokenProvider` (Autodesk client id/secret) for local ACC readers
2. AccService BaseUrl + TLS pins loaded from system settings into host config at startup
3. Vault AD + MasterPlan connection providers; shared `ActiveDirectoryUserLookupService`
   (moved out of V2)
4. Native `GoogleDriveInspectionTemplateCatalog` (list templates only — not Sheets create/export)
5. Register `IProjectWorkSurfaceHost` → `ProjectWorkSurfaceHost`

**Risk / effort:** Medium; no DB schema changes; V2 New System path unchanged.

**ACC inbox ensure (slice 2):** prefer AccService **Remote** (default `AccService:BaseUrl`).

## Slice 2b — Local ACC Inbox bootstrap (opt-in)

Single-machine / offline-friendly Local mode for inbox ensure without AccService HTTP:

1. `AccBootstrapLocalInboxBootstrapExecutor` in `SiNet.Infrastructure.AccBootstrap`
   (same `AccBootstrapService.EnsureOfficeInboxAsync` path as V2 / AccService in-process).
2. Registered only from `AddSiNet(SiNetHostMode.StandaloneNew)` via
   `AddSiNetAccInboxBootstrapLocal()` — **not** from `V2Hybrid` (V2 keeps
   `LegacyHostLocalAccInboxBootstrapExecutor`).
3. **Remote remains default** — standalone `appsettings.json` keeps
   `AccService:BaseUrl = https://localhost:8443`. Local activates only when BaseUrl is
   empty/whitespace (`ConfigurationAccServiceModeProvider`).
4. No new feature flag. App.Wpf still has **no** ProjectReference to SiNetSQL / V2;
   Composition references AccBootstrap.

**Risk / effort:** Low–medium; no DB schema; MultiStart Remote path unchanged.

## Slice 2 — out of scope

- Inspection Sheets create/export parity
- `IExternalHealthCheckSource`, `AddSiNetAi`
- AccService ProjectReference removal from SiNetSQL (track **B**; B1 vault/logging done — see [`ACC_SERVICE_DECOUPLING.md`](./ACC_SERVICE_DECOUPLING.md))
- Delete V2 New System startup / delete `LegacyHostLocalAccInboxBootstrapExecutor`
- MasterPlan Shared de-vendor / rename `SiNetSQL.*` namespaces
- Changing standalone default BaseUrl to empty (Local-by-default)

## Parallel track — AccService decoupling (B)

Standalone host slice 2 does **not** remove AccService's SiNetSQL reference. That work is
track B (`ACC_SERVICE_DECOUPLING.md`). B1 moves AccService vault + central logging onto
clean Infrastructure modules while keeping SiNetSQL for provisioning/EF.

## Host adapters — policy

| Port category | Slice 2 policy |
| --- | --- |
| Already have SQL/native impl | Register those |
| Pilot-needed (token, AD, template list, ProjectWork iface) | Native/vault implementations |
| Local Acc inbox bootstrap | Slice 2b — registered on StandaloneNew; active when BaseUrl empty |
| Inspection export | Deferred — stub / menu-gated |
| Other V2-only | Keep no-op / menu-gated |

Pilot menu surfaces from `NEW_SYSTEM_BOUNDARY.md` remain the acceptance bar.

## Success criteria (slice 2)

1. Standalone DI resolves vault `ITokenProvider` and Acc host config from system settings.
2. Add User can search AD when vault/domain configured (not Null lookup).
3. Inspection window lists Drive templates when folder id + Google session available.
4. `IProjectWorkSurfaceHost` resolves to native `ProjectWorkSurfaceHost`.
5. Build/tests green; no SiNetSQL/V2 ProjectReference from App.Wpf.

## Decisions (locked)

1. **Exe strategy:** New System = only `SiNet.App.Wpf.exe`; Legacy = only V2.exe.
2. **V2 New System path:** deprecate + log; remove in a later slice after pilot.
3. **Missing native adapters:** hide those menu entries until a native implementation exists.
4. **Local AccBootstrap:** prefer AccService Remote by default; StandaloneNew registers
   `IAccInboxBootstrapLocalExecutor` from AccBootstrap so Local works when BaseUrl is cleared.
   App.Wpf does not reference AccBootstrap directly (Composition does).
