# ACC Boundary

> **Status:** Wave 1 fast-finish implemented - clean ACC runtime boundary closed for inbox + file-tree privileged flows (2026-07-04)  
> **Branch:** `SiWorkNet10`

This document records the current ACC / Autodesk boundary across the clean stack and the still-live
legacy runtime. It exists to separate:

- the **target architecture** for ACC,
- the **currently migrated clean seam**,
- the **remaining legacy/runtime ownership**, and
- the **explicit temporary overlap** while vertical cutovers are still in progress.

## 1. Executive Summary

- The clean ACC seam is no longer read-only. `SiNet.Application` now exposes:
  `IAccProjectService`, `IAccProjectCatalogService`, `IAccLiveProjectDiscoveryService`,
  `IAccProjectTreeSearchService`, `IAccLookupSeedService`, `IAccDocumentService`,
  `IAccFolderPathService`, `IAccFolderBrowserService`, `IAccInboxReconciliationService`, `IAccItemService`,
  `IAccFileUploadService`, `IAccFileDownloadService`, `IAccInboxBootstrapService`,
  and the ACC control-plane ports.
- `SiNet.Infrastructure.Autodesk` now owns:
  mode resolution, remote health/diag probes, local key diagnostics, read-side adapters,
  semantic upload/download adapters, and local/remote mode switching for all of those seams.
- `SiOffice.AccService` now exposes the full Wave 1 privileged endpoint set used by the clean
  ACC runtime: upload/download, folder-path resolve/ensure, item display/version/hide, and inbox bootstrap.
- The two remaining heavy legacy consumers have now been cut over in large slices:
  `EmailIngestionService` and the `AccFileStore` file-tree stack no longer own direct ACC
  folder ensure/probe/list logic in their active runtime paths.
- This does **not** mean ACC is fully migrated. The business orchestration for filing/refile/inbox
  handling still lives largely in `SiNetSQL`, even though the privileged ACC transfer path is now
  routed through clean ports.
- That temporary state is intentional for the current wave: the goal was to separate privileged
  transport first, not to finish the full application/domain extraction in one jump.

## 2. Clean-side Inventory

| Artifact | Role | Current status |
| --- | --- | --- |
| `src/SiNet.Application/Abstractions/Autodesk/IAccProjectService.cs` | Clean port for discovering known ACC project ids from system state | Implemented via local/remote read-only adapters |
| `src/SiNet.Application/Abstractions/Autodesk/IAccProjectCatalogService.cs` | Clean port for display/search-friendly ACC project catalog access | Implemented via local/remote adapters |
| `src/SiNet.Application/Abstractions/Autodesk/IAccLiveProjectDiscoveryService.cs` | Clean port for live Autodesk hub/project discovery | Implemented via local/remote adapters |
| `src/SiNet.Application/Abstractions/Autodesk/IAccProjectTreeSearchService.cs` | Clean port for folder-tree search beneath a known ACC project root | Implemented via local/remote adapters |
| `src/SiNet.Application/Abstractions/Autodesk/IAccLookupSeedService.cs` | Clean port for SQL-backed operator lookup seeds | Implemented locally |
| `src/SiNet.Application/Abstractions/Autodesk/IAccDocumentService.cs` | Clean port for ACC item lookup by project/folder/file name | Implemented via local/remote read-only adapters |
| `src/SiNet.Application/Abstractions/Autodesk/IAccInboxReconciliationService.cs` | Clean read-only contract for truth-based ACC inbox reconciliation | Implemented by existing runtime service in `SiNetSQL` |
| `src/SiNet.Application/Abstractions/Autodesk/IAccFolderPathService.cs` | Clean port for resolving / ensuring ACC folder lineages under a known root | Implemented in Wave 1 fast-finish |
| `src/SiNet.Application/Abstractions/Autodesk/IAccFolderBrowserService.cs` | Clean port for routine ACC folder browsing | Implemented in Wave 1 |
| `src/SiNet.Application/Abstractions/Autodesk/IAccItemService.cs` | Clean port for ACC item display/version/hide operations | Implemented in Wave 1 fast-finish |
| `src/SiNet.Application/Abstractions/Autodesk/IAccFileUploadService.cs` | Clean semantic port for ACC upload / new-version / same-source / snapshot flow | Implemented in Wave 1 |
| `src/SiNet.Application/Abstractions/Autodesk/IAccFileDownloadService.cs` | Clean port for ACC item download-to-temp | Implemented in Wave 1 |
| `src/SiNet.Application/Abstractions/Autodesk/IAccInboxBootstrapService.cs` | Clean port for privileged inbox bootstrap recovery | Implemented in Wave 1 |
| `src/SiNet.Application/Abstractions/Autodesk/AccItemRef.cs` | Value object for resolved ACC items | Flowing through the read-only document adapter |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceModeProvider.cs` | Clean port for resolving local vs remote ACC service mode | Implemented |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceHealthProbe.cs` | Clean port for probing remote ACC service health | Implemented |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceDiagnosticsProbe.cs` | Clean port for safe remote ACC diagnostics | Implemented |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceKeyDiagnostics.cs` | Clean port for local ACC API-key diagnostics | Implemented |
| `src/SiNet.Application/Abstractions/Autodesk/AccServiceControlPlaneDtos.cs` | Mode/health/diag/key-info value objects | Implemented |
| `src/SiNet.Infrastructure.Autodesk/AutodeskServiceCollectionExtensions.cs` | DI entry point for ACC module | Registers control-plane, folder/item, read-side, and transfer seams |
| `src/SiNet.Infrastructure.Autodesk/AutodeskLocalFileTransferServiceCollectionExtensions.cs` | Local-only registration helper for privileged transfer execution | Implemented for `SiOffice.AccService` |
| `src/SiNet.Infrastructure.Autodesk/ConfigurationAccServiceModeProvider.cs` | Resolves `AccService:BaseUrl` mode through the existing host configuration seam | Implemented |
| `src/SiNet.Infrastructure.Autodesk/HttpAccServiceHealthProbe.cs` | Remote `/v1/acc/health` adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/HttpAccServiceDiagnosticsProbe.cs` | Remote `/v1/acc/diag` adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/VaultAccServiceKeyDiagnostics.cs` | Local ACC API-key metadata adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccProjectCatalogService.cs` | Local-mode ACC project catalog adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccProjectCatalogService.cs` | Remote-mode ACC project catalog adapter via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccProjectCatalogService.cs` | Delegates ACC project catalog access by mode | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccLiveProjectDiscoveryService.cs` | Local-mode live Autodesk hub/project discovery adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccLiveProjectDiscoveryService.cs` | Remote-mode live Autodesk hub/project discovery adapter via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccLiveProjectDiscoveryService.cs` | Delegates live Autodesk discovery by mode | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccProjectService.cs` | Local-mode read-only project discovery from known SQL mappings/resources | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccProjectService.cs` | Remote-mode read-only project discovery via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccProjectService.cs` | Delegates read-only project discovery by `IAccServiceModeProvider.Mode` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccProjectTreeSearchService.cs` | Local-mode folder tree search beneath a known ACC project root | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccProjectTreeSearchService.cs` | Remote-mode folder tree search via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccProjectTreeSearchService.cs` | Delegates ACC project tree search by mode | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccLookupSeedService.cs` | Local SQL-backed operator lookup seed adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccDocumentService.cs` | Local-mode read-only item lookup | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccDocumentService.cs` | Remote-mode read-only item lookup via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccDocumentService.cs` | Delegates read-only item lookup by mode | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccFolderPathService.cs` | Local-mode ACC folder-path resolve/ensure | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccFolderPathService.cs` | Remote-mode ACC folder-path resolve/ensure via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccFolderPathService.cs` | Delegates ACC folder-path work by mode | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccItemService.cs` | Local-mode ACC item display/version/hide adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccItemService.cs` | Remote-mode ACC item display/version/hide adapter via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccItemService.cs` | Delegates ACC item operations by mode | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccFileUploadService.cs` | Local semantic ACC upload adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccFileUploadService.cs` | Remote semantic ACC upload adapter via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccFileUploadService.cs` | Delegates ACC upload by mode | Implemented |
| `src/SiNet.Infrastructure.Autodesk/LocalAccFileDownloadService.cs` | Local ACC download-to-temp adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/RemoteAccFileDownloadService.cs` | Remote ACC download-to-temp adapter via `SiOffice.AccService` | Implemented |
| `src/SiNet.Infrastructure.Autodesk/ModeSwitchingAccFileDownloadService.cs` | Delegates ACC download by mode | Implemented |
| `SiOffice.AccService/Endpoints/AccEndpoints.cs` | Service-mode ACC read + transfer endpoints | Implemented for read slices and Wave 1 transfer endpoints |
| `SiOffice.AccService/Program.cs` | Service host registration for in-process transfer execution | Now registers local ACC file transfer services |
| `src/SiNet.App.Wpf/Admin/Security/SecretSetupViewModel.cs` | Native UI consumer of the ACC control-plane seam | Implemented for runtime/diag display |
| `src/SiNet.App.Wpf/Admin/Settings/SettingsViewModel.cs` | Native ACC settings consumer of the control-plane seam | Implemented for runtime display beside stored settings |
| `src/SiNet.App.Wpf/Autodesk/AccControlPlaneStatusWindow.cs` | Native ACC runtime-status/operator surface | Implemented for status display, browse/search, manual item lookup, and explicit inbox-bootstrap ensure |
| `src/SiNet.LegacyBridge/LegacyBridgeServiceCollectionExtensions.cs` | Temporary bridge slot | No ACC bridge wired |

Implication: the clean Autodesk module now owns the **ACC runtime boundary and transfer seam**,
but not yet the whole business orchestration of every ACC-related workflow.

## 3. Legacy / Production Inventory

### 3.1 Runtime-sensitive host wiring

Main runtime-sensitive behavior still lives in:

- `SiNetProjectManagerV2/App.xaml.cs`
- `SiNetProjectManagerV2/Services/AppConfiguration.cs`

These files still control:

- Autodesk token-provider creation from vault-backed secrets
- `AccService:BaseUrl` mode switching
- remote `SiOffice.AccService` registration vs local in-process provisioning
- API-key wiring for remote `AccService`
- custom TLS/self-signed behavior for approved internal ACC service hosts

Additional host note:

- `src/SiNet.App.Wpf/App.xaml.cs` explicitly calls `AddSiNetSecrets()` after `AddSiNet()`.
  This host-level wiring is intentional: `SiNet.App.Composition` remains `net10.0`, while
  `SiNet.Infrastructure.Secrets` is Windows-targeted. ACC control-plane diagnostics need vault
  access, but that dependency still cannot be pushed into the cross-platform composition project.

### 3.2 Main concerns still owned by legacy

| Concern | Main runtime ownership today |
| --- | --- |
| Auth / token plumbing | `MyOffice.AutodeskConnector.ITokenProvider` wiring in `SiNetProjectManagerV2/App.xaml.cs` |
| Remote/local provisioning split | `AccService:BaseUrl` block in `SiNetProjectManagerV2/App.xaml.cs` |
| Remote provisioning adapters | `SiNetProjectManagerV2/Services/RemoteAccProjectProvisioningService.cs`, `SiNetProjectManagerV2/Services/RemoteAccInboxProvisioner.cs` |
| Internal ACC service diagnostics | `SiNetProjectManagerV2/Services/Health/InternalAccServiceHealthCheck.cs` |
| AccService key diagnostics | `src/SiNet.Infrastructure.Secrets/AccServiceSecretDiagnostics.cs` |
| ACC business orchestration around filing/refile/inbox | `SiNetSQL` services and handlers still own most orchestration logic |
| Remaining direct ACC transfer callers | Wave 1 caller cutovers now cover the known active inbox, filing, move-to-project, and file-tree privileged paths; remaining legacy ownership is mainly orchestration/provisioning and write-path ordering, not direct privileged transfer fallbacks in the touched consumers |
| Admin settings / probes / service-mode UI | `SiNetProjectManagerV2/WPF Window/ManagementSettingsWindow.AccService.cs`, `SiNetProjectManagerV2/WPF Window/SecretSetupWindow.xaml.cs`, `SiNetProjectManagerV2/Dialogs/UserGroupManagementWindow.xaml.cs` |

### 3.3 Why `SiNetSQL` is still being touched

`SiNetSQL` is still being touched because the current ACC write/runtime flows are **not fully moved**
into new clean application services yet. What has moved is the **privileged ACC transport boundary**:
the actual upload/download execution path is now behind clean ports and can route through
`SiOffice.AccService` in remote mode.

What has **not** moved yet is the higher-level workflow/business orchestration that decides:

- which inbox item to file,
- which slot/folder/path to target,
- how refile cleanup works,
- how MoveToProject coordinates metadata/task effects,
- and how the remaining file-tree/manual flows behave.

So the current wave still requires bounded edits inside `SiNetSQL` to re-point those runtime
consumers at the new clean ports. That is a **transitional migration step**, not the target state.

The stop line for this wave is now:

- legacy orchestration may still live in `SiNetSQL`,
- but the active inbox/file-tree/move/filing privileged ACC runtime paths must not call the connector directly for transfer, folder-path resolution/creation, routine browsing, or item inspection/lifecycle,
- and in service mode those flows must route through `SiOffice.AccService`.

## 4. Boundary Rules Already Established

The legacy ACC docs remain the behavioral source of truth during migration. The most important
rules are:

1. **ACC is the source of truth** for ACC-stored files and ACC-side metadata. The DB is cache/helper only.
2. **Never trust DB-only identifiers** to prove an ACC file exists; reconcile with ACC first.
3. **Never fabricate viewer URLs** from cached DB identifiers.
4. `SiOffice.AccService` is the **service-mode / privileged** boundary.
5. `SiOffice.AutodeskConnector` is a **technical connector only**, not a business engine.
6. `AccInboxReconciliationService` verifies inbox reality/status only; it must not become upload/filing/workflow logic.
7. When `AccService:BaseUrl` is configured, remote clients must route privileged ACC work through
   `SiOffice.AccService`; no silent parallel local privileged path.
8. UI/WPF must not call the connector directly for business decisions.

Primary legacy source of truth:

- `SiNetProjectManagerV2/Docs/Domains/ACC/AccSystemPrinciples-2026-05-26.md`

## 5. What Is Safe vs Risky

### Current safe migration shape

The currently approved ACC migration shape is:

- isolate the `AccService:BaseUrl` mode switch,
- formalize mode/health/diagnostics/key-info as clean ports,
- formalize ACC upload/download as **semantic** clean ports,
- keep privileged transfer and service-mode transport behind the clean seam,
- cut callers over incrementally,
- but avoid re-designing the whole ACC business engine in the same wave.

This is still safer than attempting a full one-shot rewrite of:

- filing semantics,
- inbox business rules,
- metadata lifecycle,
- provisioning,
- and task/workflow side effects

all at once.

### Highest-risk areas

1. **Full filing / refile / storage orchestration**
   - Side effects, source-of-truth transitions, and file-placement policy still live here.
2. **Metadata + inbox reconciliation**
   - Tied to persisted ACC identifiers, lock/move status, and recovery semantics.
3. **Provisioning behavior drift**
   - The remote-vs-local split is runtime-sensitive and includes vault, API key, and TLS behavior.
4. **Admin capability asymmetry**
   - Some probe/admin workflows are local-only in practice and not fully mirrored by remote adapters.

## 6. Wave 1 Status

Wave 1 now contains the following implemented pieces:

1. **Control-plane seam**
   - Mode, health, diagnostics, and key-info ports/adapters.
2. **Read slices**
   - Known ACC project discovery and item lookup.
3. **Folder/item runtime seams**
   - `IAccFolderPathService`, `IAccFolderBrowserService`, `IAccItemService`, and `IAccInboxBootstrapService` plus local/remote/mode-switching adapters.
4. **Transfer seam**
   - `IAccFileUploadService` and `IAccFileDownloadService` plus local/remote/mode-switching adapters.
5. **Service endpoints**
   - Upload/download, folder-path, item, and inbox-bootstrap endpoints in `SiOffice.AccService`.
6. **First consumer cutovers**
   - `ProjectFileFilingService` upload path.
   - `ProjectFileRefileService` inbox temp download path.
   - `MoveToProjectProcessActionHandler` inbox temp download path.
7. **Big inbox cutover**
   - `EmailIngestionService` now routes folder ensure/probe through `IAccFolderPathService`, remaining folder reads/dedup through `IAccFolderBrowserService`, bootstrap recovery through `IAccInboxBootstrapService`, and uploads through `IAccFileUploadService` without a direct ACC upload fallback.
8. **Big file-tree cutover**
   - `AccFileStore` now routes path ensure/probe through `IAccFolderPathService`, routine folder listing through `IAccFolderBrowserService`, item hide/display/version through `IAccItemService`, metadata reads through `IAccItemMetadataService`, and uploads/downloads through the clean transfer ports.
9. **Inbox/background transfer cutovers**
   - `AccFileSyncService` now downloads from Inbox and uploads to project folders through the clean transfer ports.
   - `AttachmentTaggingService` ZIP companion-metadata JSON reads/writes now use the clean transfer ports.
   - `AccInboxReconciliationService` ZIP companion-metadata JSON reads now use the clean download port.
10. **Inbox read/navigation cutover**
   - `AttachmentTaggingService` and `AccInboxReconciliationService` now prefer `IAccFolderBrowserService` for inbox folder browsing / ZIP companion-metadata lookup, so those reads also route through `SiOffice.AccService` in remote mode.
11. **Post-Wave-1 cleanup**
   - `MoveToProjectProcessActionHandler` now requires the clean download/upload/browser seams directly; the internal compatibility constructor and legacy ACC transfer fallbacks have been retired.
   - `AttachmentTaggingService`, `AccInboxReconciliationService`, and the touched inbox metadata-repair paths now require DI-provided ACC metadata/transfer services instead of constructing ad-hoc legacy fallbacks at runtime.
   - `ProjectFileRefileService` now uses `IAccFileDownloadService` and `IAccItemService` directly; the compatibility constructor and legacy ACC download fallback have been retired.
12. **A3 reconciliation contract extraction**
   - `IAccInboxReconciliationService` and its DTOs now live in `SiNet.Application.Abstractions.Autodesk`.
   - `AccInboxReconciliationService` remains the runtime implementation for now, but it now implements the Application contract instead of owning a legacy-local interface.
   - Active consumers in the host / VM / MoveToProject paths now resolve the Application seam.
13. **A4 server-only map closure**
   - `docs/ACC_CONTROL_PLANE.md` is now the authoritative classification for what remains
     read-only/operator-safe versus server-only/deferred.
   - No additional contract extraction is required before future read-only window work; mirrored
     ACC service constants remain acceptable temporary glue until a write-heavy or broader service
     consumer slice needs more.

Wave 1 is considered **closed at the runtime boundary**. What remains is the next phase:
extracting more orchestration/provisioning behavior out of `SiNetSQL` without reopening direct
connector ownership in the cut-over runtime paths.

## 7. Guardrails

Until a separately approved slice says otherwise:

1. **Do not** bypass `SiOffice.AccService` in service mode.
2. **Do not** invent a second parallel ACC orchestration path.
3. **Do not** move raw ACC business rules into `SiNet.Infrastructure.Autodesk`.
4. **Do not** declare ACC "fully migrated" just because the transfer ports exist.
5. **Do not** merge Drive/Google concerns into the ACC seam.
6. **Do not** touch EF migrations / `ModelSnapshot` / `*.Designer.cs` while working through ACC cutovers.

## 8. Immediate Next Step

The next ACC work is no longer "finish the Wave 1 seams" - that boundary is now in place.
The next useful slices are:

- extract remaining orchestration/provisioning responsibilities out of `SiNetSQL` where it now makes sense architecturally,
- keep moving reconciliation-adjacent consumers off local DB/cache assumptions and onto the Application reconciliation seam,
- keep retiring orchestration-era compatibility code only after each replacement seam is verified and documented,
- and keep the rule stable that service-mode privileged ACC work continues to route through `SiOffice.AccService`.
