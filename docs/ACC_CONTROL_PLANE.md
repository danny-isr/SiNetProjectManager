# ACC Control Plane

> **Status:** Stage A1 stabilized and reconciled to code, with the first native A3 consumer anchored in the status window (2026-07-04)  
> **Scope:** mode + health + diagnostics + key metadata + catalog/discovery/search/browse/read-only lookup + read-only inbox reconciliation, with inbox bootstrap classified as an admin-adjacent operator action rather than general ACC runtime orchestration

This document records the **current code truth** for the native ACC control-plane and adjacent
read/discovery surface. It is intentionally **narrower than the full ACC runtime** and is the
approved first implementation slice for the fast refactor path:

- control-plane and operator visibility first,
- read/discovery next,
- reconciliation after that,
- provisioning later,
- and only then filing / metadata-write / move-heavy runtime work.

This slice does **not** migrate full provisioning, filing, metadata writes, or workflow-coupled ACC
orchestration.

## 1. What The Current Slice Includes

### Clean ports and DTOs

- `IAccServiceModeProvider`
- `IAccServiceHealthProbe`
- `IAccServiceDiagnosticsProbe`
- `IAccServiceKeyDiagnostics`
- `IAccProjectCatalogService`
- `IAccLiveProjectDiscoveryService`
- `IAccProjectService`
- `IAccProjectTreeSearchService`
- `IAccLookupSeedService`
- `IAccDocumentService`
- `IAccFolderBrowserService`
- `IAccInboxBootstrapService` *(admin-adjacent operator action; not a general business-runtime write seam)*
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
- `LocalAccProjectCatalogService`
- `RemoteAccProjectCatalogService`
- `ModeSwitchingAccProjectCatalogService`
- `LocalAccLiveProjectDiscoveryService`
- `RemoteAccLiveProjectDiscoveryService`
- `ModeSwitchingAccLiveProjectDiscoveryService`
- `LocalAccProjectService`
- `RemoteAccProjectService`
- `ModeSwitchingAccProjectService`
- `LocalAccFolderBrowserService`
- `RemoteAccFolderBrowserService`
- `ModeSwitchingAccFolderBrowserService`
- `LocalAccProjectTreeSearchService`
- `RemoteAccProjectTreeSearchService`
- `ModeSwitchingAccProjectTreeSearchService`
- `LocalAccLookupSeedService`
- `Bim360AccFolderItemsReader`
- `LocalAccDocumentService`
- `RemoteAccDocumentService`
- `ModeSwitchingAccDocumentService`
- `LocalAccInboxBootstrapService`
- `RemoteAccInboxBootstrapService`
- `ModeSwitchingAccInboxBootstrapService`

### DI wiring

- `AddSiNetAutodesk()` now registers the control-plane, catalog/discovery/search, folder browse,
  document lookup, inbox bootstrap, and transfer/runtime seams while still **excluding** legacy
  provisioning/filing services.
- `SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs` wires:
  - `AddSiNetSecrets()`
  - `AddSiNetAutodesk()`
  - `AddSiNetNewSystemWpf()`
  into the legacy production host graph.
- The host-local `IAccInboxBootstrapLocalExecutor` now lives in
  `SiNetProjectManagerV2/Services/LegacyHostLocalAccInboxBootstrapExecutor.cs` on purpose, so
  `src/SiNet.App.Wpf` no longer references `SiNetSQL` or `SiOffice.AutodeskConnector` just to
  support a temporary privileged bootstrap path.
- `src/SiNet.App.Wpf/App.xaml.cs` explicitly calls `AddSiNetSecrets()` after `AddSiNet()` so the
  WPF harness can resolve vault-backed ACC key diagnostics and any vault-backed host configuration
  used by the native ACC status surface.
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
- The same status window now also hosts a **native read-only inbox reconciliation panel** backed by
  `IAccInboxReconciliationService`, so operators can inspect ACC truth for a concrete inbox message
  and push a selected attachment directly into the lookup/browse tester without opening a legacy
  email surface.
- The same status window also exposes `IAccInboxBootstrapService` as an **explicit operator/admin**
  ensure action. This is intentionally classified as adjacent to control-plane/operator tooling, not
  as a migrated filing/provisioning orchestration path.

## 2. Runtime Rules

### Mode resolution

- If `ISecretSetupHostConfiguration.AccServiceBaseUrl` is null/empty/whitespace:
  `Mode = Local`, `BaseUrl = null`.
- If `AccServiceBaseUrl` is present:
  `Mode = Remote`, and `BaseUrl` is trimmed plus normalized without a trailing slash.

### Remote probes

- Health uses `GET {BaseUrl}/v1/acc/health`
- Diagnostics uses `GET {BaseUrl}/v1/acc/diag`
- Read-side project discovery uses `GET {BaseUrl}/v1/acc/projects/ids`
- Project catalog uses `GET {BaseUrl}/v1/acc/projects/catalog`
- Live Autodesk hub/project discovery uses:
  - `GET {BaseUrl}/v1/acc/live/hubs`
  - `GET {BaseUrl}/v1/acc/live/hubs/{hubId}/projects`
- Read-only folder browse / search uses:
  - `GET {BaseUrl}/v1/acc/projects/{projectId}/folders/browse`
  - `GET {BaseUrl}/v1/acc/projects/{projectId}/folders/search`
- Read-only item lookup uses
  `GET {BaseUrl}/v1/acc/projects/{projectId}/folders/{folderId}/items/resolve?fileName=...`
- Inbox bootstrap ensure uses `POST {BaseUrl}/v1/acc/inbox/ensure` when remote mode is active.
- Health timeout: 5 seconds
- Diagnostics timeout: 10 seconds

### Project discovery rules

- `IAccProjectService` returns the distinct set of **known ACC project IDs** already recorded in the
  shared SQL database.
- Local mode reads the union of `ProjectAccMapping.AccProjectId` and `AccSystemResource.AccProjectId`.
- Remote mode reads the same union through `SiOffice.AccService`.
- This slice is intentionally **not** a live Autodesk account scan and does not enumerate every
  project visible to the connector.
- `IAccProjectCatalogService` is the display/search-friendly catalog surface for operators and native
  status/settings consumers.
- `IAccLiveProjectDiscoveryService` is the explicit live Autodesk scan surface; it is separate from
  the known-project-ID surface so the app never silently substitutes one source of truth for the
  other.
- `IAccLookupSeedService` exists to prefill operator tooling from SQL-backed inbox context only; it
  is a convenience/input-seeding surface, **not** a proof-of-existence source.

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
- member reconciliation / project-user bootstrap orchestration
- `IProjectFileFilingService`
- end-to-end `ProjectFileRefileService` write semantics and broader refile orchestration
- `MoveToProject` / ACC move metadata flows
- metadata writes or reconciliation writes

Native consumers currently exist for:

- status/diagnostics display inside Secret Setup,
- the ACC system-settings tab,
- the dedicated ACC status window,
- operator lookup/prefill/search/browse flows,
- operator read-only inbox reconciliation flows,
- and the explicit inbox-bootstrap ensure action in the status window.

They do **not** replace provisioning, filing, metadata-write, or remote privileged business-runtime
ownership.

## 5. Tests

Offline coverage for this ACC slice lives primarily in:

- `src/SiNet.App.Wpf.Tests/Autodesk/AccControlPlaneTests.cs`
- `src/SiNet.App.Wpf.Tests/Autodesk/AccControlPlaneStatusWindowTests.cs`
- `src/SiNet.App.Wpf.Tests/Autodesk/NewShellAccStatusMenuTests.cs`
- `src/SiNet.App.Wpf.Tests/Admin/NativeSettingsSurfaceTests.cs`

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
- source-level coverage for the live-discovery / browse / search / inbox-ensure endpoint set
- WPF harness secret wiring via `AddSiNetSecrets()`
- shell/menu wiring for the dedicated ACC status window
- status-window read-only document lookup over `IAccDocumentService`
- status-window DB-backed prefill for manual document lookup (`IAccLookupSeedService`)
- status-window read-only inbox reconciliation over `IAccInboxReconciliationService`
- status-window inbox bootstrap execution path
- settings-tab runtime-only read-only document lookup over `IAccDocumentService`

## 6. Next Slice

The next ACC steps after this control-plane stabilization should be:

1. **Stage A2 — Read / Discovery**
   - keep expanding native/admin consumers of catalog, discovery, browse, and lookup seams,
   - but do not introduce privileged write behavior.
2. **Stage A3 — Reconciliation**
   - introduce an explicit ACC-truth reconciliation contract and DTOs,
   - keep it read-only in behavior,
   - and do not let DB-only identifiers become proof of ACC state.
3. **Only after A2/A3:** approach provisioning, filing, and metadata-write slices separately.
