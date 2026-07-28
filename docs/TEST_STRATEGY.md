# Standalone New System — Test Strategy

> **Status:** Active (2026-07-29)  
> **Host:** `SiNet.App.Wpf.exe` (`SiNetHostMode.StandaloneNew`)  
> **Pilot envelope:** [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md)  
> **Manual checklist (operator-only):** [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md)

This document defines the **test layers** for the limited standalone pilot: what runs in CI,
what can be run locally with secrets, and what always stays manual.

---

## 1. Layers

| Layer | Where | Runs in CI | Needs secrets / network |
| --- | --- | --- | --- |
| **L1** Unit / ViewModel + stubs | `src/SiNet.App.Wpf.Tests`, Google.Tests, LegacyBridge.Tests | Yes | No |
| **L2** Boundary / source guards | `src/SiNet.App.Wpf.Tests/Boundary`, `Shell`, docs asserts | Yes | No |
| **L3** Composition smoke (offline) | `Composition/StandaloneHostCompositionTests`, menu gating, startup guards | Yes | No |
| **L4** Live smoke (env-gated) | `src/SiNet.App.Wpf.Tests/Live` | Skipped (no env) | Yes |
| **L5** Manual operator checklist | `manual-tests/STANDALONE_PILOT_SMOKE.md` | No | Yes + human UI |

```text
L1 Unit/VM → L2 Boundary → L3 Composition (CI) → L4 Live (optional) → L5 Manual
```

---

## 2. CI gate (official)

Workflow: [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) on `SiNet.sln`:

```powershell
pwsh ./build/fetch-siblings.ps1
dotnet restore SiNet.sln
dotnet build SiNet.sln --configuration Debug --no-restore
dotnet build SiNet.sln --configuration Release --no-restore
dotnet test src/SiNet.App.Wpf.Tests/SiNet.App.Wpf.Tests.csproj --configuration Release --no-build
dotnet test src/SiNet.Infrastructure.Google.Tests/SiNet.Infrastructure.Google.Tests.csproj --configuration Release --no-build
dotnet test src/SiNet.LegacyBridge.Tests/SiNet.LegacyBridge.Tests.csproj --configuration Release --no-build
pwsh ./build/secret-scan.ps1
```

Local agent gate (host + primary tests):

```powershell
dotnet build SiNetProjectManagerV2\SiNetProjectManagerV2.csproj
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj
```

Live tests (`Category=LiveSmoke`) are **skipped** unless `SINET_LIVE_SMOKE=1` — they do not fail CI.

---

## 3. Offline automation that replaces checklist steps

| Manual concern | Automated pre-check |
| --- | --- |
| DI loads / ports resolve | `StandaloneHostCompositionTests` |
| Menu visible per feature gate | `NewShellReleaseMenuGatingTests` |
| DEBUG harness not in Release source path | same + `#if DEBUG` source guards |
| Startup order: vault → schema → auth → shell | `StandaloneStartupSequenceTests` |
| Email ACC button/status text states | `EmailAccSelectionHandlerStatusTests` |
| No duplicate DI registrations (StandaloneNew) | composition tests |

---

## 4. Live smoke (optional, local)

Requires a developer machine with real vault/DB/AccService (and optionally a restored Gmail token).

```powershell
$env:SINET_LIVE_SMOKE = "1"
# Optional overrides:
# $env:SINET_LIVE_SQL_CONNECTION = "Server=...;Database=...;..."
# $env:SINET_LIVE_ACC_BASEURL = "https://localhost:8443"

dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj --filter "Category=LiveSmoke"
```

| Test class | What it checks |
| --- | --- |
| `SqlConnectivityLiveTests` | SQL connect + `IDatabaseSchemaGate` present |
| `VaultLiveTests` | AccService API key metadata readable; raw secret not logged in diagnostics |
| `AccServiceHealthLiveTests` | `GET /v1/acc/health` (no key) + `GET /v1/acc/diag` (with key) |
| `AccModeLiveTests` | Mode is Remote when BaseUrl configured |
| `GmailSilentRestoreLiveTests` | `TryRestoreSessionAsync` only — never opens a browser; Skip if no token |

**Environment defaults:** AccService `https://localhost:8443`; SQL from `SINET_LIVE_SQL_CONNECTION` or vault key `SiNet/ConnectionStrings/SiNetDatabase`.

---

## 5. Always manual (never automated here)

- MultiStart launch of `SiNet.App.Wpf.exe` + AccService process
- Interactive OAuth consent / first-time Google login
- WebView2 Jumbo / WeTransfer download UX
- Real ACC Inbox upload, Move, recovery against live ACC projects
- MasterPlan R01–R03 write to real Google Sheets
- Visual navigation polish (project selector, layout, Hebrew copy)

Those remain in [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md).

---

## 6. Related docs

| Doc | Role |
| --- | --- |
| [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md) | Pilot envelope + verification pointer |
| [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md) | Host composition |
| [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) | Email ACC N1–N3 |
| [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md) | AccService Local/Remote |
| Superseded: [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) | Replaced by STANDALONE_PILOT_SMOKE |
| Superseded: [`manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md`](./manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md) | Folded into STANDALONE_PILOT_SMOKE § Email ACC |
