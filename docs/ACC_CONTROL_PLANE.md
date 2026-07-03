# ACC Control Plane

> **Status:** Slice 1 implemented (2026-07-03)  
> **Scope:** mode + health + diagnostics + local key metadata only

This document describes the native ACC control-plane seam that now exists in the clean stack.
It is intentionally narrower than the full ACC runtime. It does **not** migrate provisioning,
inbox bootstrap, filing, metadata writes, or any side-effect-heavy document flows.

## 1. What Slice 1 Includes

### Clean ports

- `IAccServiceModeProvider`
- `IAccServiceHealthProbe`
- `IAccServiceDiagnosticsProbe`
- `IAccServiceKeyDiagnostics`
- `AccServiceMode`
- `AccServiceHealthState`
- `AccServiceHealthResult`
- `AccServiceDiagnosticsResult`
- `AccServiceKeyInfo`

### Infrastructure adapters

- `ConfigurationAccServiceModeProvider`
- `HttpAccServiceHealthProbe`
- `HttpAccServiceDiagnosticsProbe`
- `VaultAccServiceKeyDiagnostics`
- `AccServiceHttpClientConfigurator`

### DI wiring

- `AddSiNetAutodesk()` now registers the control-plane services only.
- `src/SiNet.App.Wpf/App.xaml.cs` explicitly calls `AddSiNetSecrets()` after `AddSiNet()` so the
  WPF harness can resolve vault-backed ACC key diagnostics.
- `src/SiNet.App.Wpf/Admin/Security/SecretSetupViewModel.cs` consumes the seam and exposes a
  read-only ACC panel in `SecretSetupView` for mode, endpoint, key metadata, health, and diag state.
- `src/SiNet.App.Wpf/Admin/Settings/SettingsViewModel.cs` consumes the same seam in the
  `ACC (גלובלי)` tab for current-process runtime status, while keeping the stored
  `AccService.BaseUrl` field separate.

## 2. Runtime Rules

### Mode resolution

- If `ISecretSetupHostConfiguration.AccServiceBaseUrl` is null/empty/whitespace:
  `Mode = Local`, `BaseUrl = null`.
- If `AccServiceBaseUrl` is present:
  `Mode = Remote`, and `BaseUrl` is trimmed plus normalized without a trailing slash.

### Remote probes

- Health uses `GET {BaseUrl}/v1/acc/health`
- Diagnostics uses `GET {BaseUrl}/v1/acc/diag`
- Health timeout: 5 seconds
- Diagnostics timeout: 10 seconds

### TLS policy

The HTTP handler accepts self-signed certificates only for approved internal hosts:

- exact hosts: `SI-WIN-2K19`, `localhost`, `127.0.0.1`
- suffixes: `.si-eng.local`
- IP prefixes: `192.168.`

This list lives in `AccServiceControlPlaneOptions` and can be overridden by host code later if
needed.

## 3. Contract Note

The privileged-service contract is still the legacy/frozen `AccServiceContracts` surface.
For now, the clean control-plane mirrors the `/v1` prefix internally in
`AccServiceContractConstants`.

Why this is mirrored instead of referenced directly:

- `SiNet.Infrastructure.Autodesk` is `net10.0`
- the canonical contract currently lives in a Windows-targeted graph
- directly referencing that graph from the clean cross-platform module breaks restore/build

Until the contract is extracted to a neutral assembly, keep the mirrored constant aligned with the
legacy source of truth.

## 4. What Is Still Deferred

This slice does **not** implement:

- `IAccProjectService`
- `IAccDocumentService`
- remote project provisioning
- remote inbox provisioning
- `IProjectFileFilingService`
- metadata writes or reconciliation writes
- native UI consumption of the control-plane seam

Native consumers currently exist only for **status/diagnostics display** inside Secret Setup and
the ACC system-settings tab. They do not change provisioning, filing, or remote privileged-write
behavior.

## 5. Tests

Offline coverage for slice 1 lives in:

- `src/SiNet.App.Wpf.Tests/Autodesk/AccControlPlaneTests.cs`

Current checks cover:

- local vs remote mode resolution
- trailing-slash normalization
- `/v1/acc/health` endpoint construction
- success and failure mapping for health
- JSON mapping for diagnostics
- safe local API-key hashing
- DI guardrails: no `IAccProjectService`, `IAccDocumentService`, provisioning, inbox, or filing registrations
- WPF harness secret wiring via `AddSiNetSecrets()`

## 6. Next Slice

The next ACC step should expand this into broader native admin/status UI while keeping the
write-heavy ACC pipeline deferred until a separate approved slice.
