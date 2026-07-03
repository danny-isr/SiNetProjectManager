# ACC Control Plane

> **Status:** Slice 3 implemented (2026-07-03)  
> **Scope:** mode + health + diagnostics + local key metadata + read-only document lookup + read-only project discovery

This document describes the native ACC control-plane seam plus the first read-only ACC document
lookup slice that now exist in the clean stack. It is intentionally narrower than the full ACC
runtime. It does **not** migrate provisioning, inbox bootstrap, filing, metadata writes, or any
side-effect-heavy document flows.

## 1. What The Current Slice Includes

### Clean ports

- `IAccServiceModeProvider`
- `IAccServiceHealthProbe`
- `IAccServiceDiagnosticsProbe`
- `IAccServiceKeyDiagnostics`
- `IAccProjectService`
- `IAccDocumentService`
- `AccServiceMode`
- `AccServiceHealthState`
- `AccServiceHealthResult`
- `AccServiceDiagnosticsResult`
- `AccServiceKeyInfo`
- `AccItemRef`

### Infrastructure adapters

- `ConfigurationAccServiceModeProvider`
- `HttpAccServiceHealthProbe`
- `HttpAccServiceDiagnosticsProbe`
- `VaultAccServiceKeyDiagnostics`
- `AccServiceHttpClientConfigurator`
- `LocalAccProjectService`
- `RemoteAccProjectService`
- `ModeSwitchingAccProjectService`
- `Bim360AccFolderItemsReader`
- `LocalAccDocumentService`
- `RemoteAccDocumentService`
- `ModeSwitchingAccDocumentService`

### DI wiring

- `AddSiNetAutodesk()` now registers the control-plane services plus `IAccProjectService` and
  `IAccDocumentService`.
- `src/SiNet.App.Wpf/App.xaml.cs` explicitly calls `AddSiNetSecrets()` after `AddSiNet()` so the
  WPF harness can resolve vault-backed ACC key diagnostics.
- `src/SiNet.App.Wpf/Admin/Security/SecretSetupViewModel.cs` consumes the seam and exposes a
  read-only ACC panel in `SecretSetupView` for mode, endpoint, key metadata, health, and diag state.
- `src/SiNet.App.Wpf/Admin/Settings/SettingsViewModel.cs` consumes the same seam in the
  `ACC (גלובלי)` tab for current-process runtime status, while keeping the stored
  `AccService.BaseUrl` field separate. The same tab now also hosts a manual runtime-only
  `IAccDocumentService` lookup tester (`projectId + folderId + fileName`) plus copy/open actions
  for the live-derived ACC Docs URL.
- `src/SiNet.App.Wpf/Autodesk/AccControlPlaneStatusWindow.cs` exposes a dedicated shell-opened
  runtime-only ACC status window built on the same presenter/control, and now hosts a manual
  read-only `IAccDocumentService` lookup tester (`projectId + folderId + fileName`) plus a
  docs URL preview generated only from the live-resolved ACC identifiers, including copy/open
  actions. The same window can now prefill the tester from recent SQL-backed
  `EmailInboxMessage + EmailInboxAttachment` candidates so operators do not need to invent ACC ids
  by hand.

## 2. Runtime Rules

### Mode resolution

- If `ISecretSetupHostConfiguration.AccServiceBaseUrl` is null/empty/whitespace:
  `Mode = Local`, `BaseUrl = null`.
- If `AccServiceBaseUrl` is present:
  `Mode = Remote`, and `BaseUrl` is trimmed plus normalized without a trailing slash.

### Remote probes

- Health uses `GET {BaseUrl}/v1/acc/health`
- Diagnostics uses `GET {BaseUrl}/v1/acc/diag`
- Read-only project discovery uses `GET {BaseUrl}/v1/acc/projects/ids`
- Read-only item lookup uses
  `GET {BaseUrl}/v1/acc/projects/{projectId}/folders/{folderId}/items/resolve?fileName=...`
- Health timeout: 5 seconds
- Diagnostics timeout: 10 seconds

### Project discovery rules

- `IAccProjectService` returns the distinct set of **known ACC project IDs** already recorded in the
  shared SQL database.
- Local mode reads the union of `ProjectAccMapping.AccProjectId` and `AccSystemResource.AccProjectId`.
- Remote mode reads the same union through `SiOffice.AccService`.
- This slice is intentionally **not** a live Autodesk account scan and does not enumerate every
  project visible to the connector.

### TLS policy

The HTTP handler accepts self-signed certificates only for approved internal hosts:

- exact hosts: `SI-WIN-2K19`, `localhost`, `127.0.0.1`
- suffixes: `.si-eng.local`
- IP prefixes: `192.168.`

This list lives in `AccServiceControlPlaneOptions` and can be overridden by host code later if
needed.

## 3. Contract Note

The privileged-service contract is still the legacy/frozen `AccServiceContracts` surface.
For now, the clean Autodesk module mirrors the `/v1` prefix and the API-key header internally in
`AccServiceContractConstants`.

Why this is mirrored instead of referenced directly:

- `SiNet.Infrastructure.Autodesk` is `net10.0`
- the canonical contract currently lives in a Windows-targeted graph
- directly referencing that graph from the clean cross-platform module breaks restore/build

Until the contract is extracted to a neutral assembly, keep the mirrored constant aligned with the
legacy source of truth.

## 4. What Is Still Deferred

This slice does **not** implement:

- remote project provisioning
- remote inbox provisioning
- `IProjectFileFilingService`
- metadata writes or reconciliation writes

Native consumers currently exist only for **status/diagnostics display** inside Secret Setup, the
ACC system-settings tab, and the dedicated ACC status window. They do not change provisioning,
filing, or remote privileged-write behavior.

## 5. Tests

Offline coverage for this ACC slice lives in:

- `src/SiNet.App.Wpf.Tests/Autodesk/AccControlPlaneTests.cs`

Current checks cover:

- local vs remote mode resolution
- trailing-slash normalization
- `/v1/acc/health` endpoint construction
- success and failure mapping for health
- JSON mapping for diagnostics
- safe local API-key hashing
- local read-only project discovery from `ProjectAccMapping` + `AccSystemResource`
- remote read-only project discovery: `/v1/acc/projects/ids` request shape, API-key header, and
  JSON mapping
- local read-only item lookup: found vs not found behavior
- remote read-only item lookup: `/v1/acc/projects/.../items/resolve` request shape, API-key header,
  and JSON mapping
- DI guardrails: `IAccProjectService` and `IAccDocumentService` are registered, while provisioning,
  inbox, and filing are still not registered
- source-level coverage for the new read-only `SiOffice.AccService` endpoint
- WPF harness secret wiring via `AddSiNetSecrets()`
- shell/menu wiring for the dedicated ACC status window
- status-window read-only document lookup over `IAccDocumentService`
- status-window DB-backed prefill for manual document lookup (`IAccLookupSeedService`)
- settings-tab runtime-only read-only document lookup over `IAccDocumentService`

## 6. Next Slice

The next ACC step should stay read-safe:

- either add the first native consumer of `IAccDocumentService` / `IAccProjectService`,
- or extend the current status-window tester into a richer open/preview flow,
- or, if the product really needs it, introduce a separate live Autodesk project-enumeration slice,
- while keeping provisioning, inbox bootstrap, filing, and metadata writes deferred.
