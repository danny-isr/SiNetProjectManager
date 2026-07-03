# ACC Boundary

> **Status:** Slice 1 implemented - control-plane seam only (2026-07-03)  
> **Branch:** `SiWorkNet10`

This document records the current ACC / Autodesk boundary across the clean stack and the legacy
production host. It exists to separate:

- the **documented target architecture** for ACC,
- the **actual production runtime** still living in `SiNetProjectManagerV2` and legacy services, and
- the **current clean-side implementation status**, which is still mostly seam-only.

## 1. Executive Summary

- The clean architecture already defines a **minimal ACC seam** in `SiNet.Application`:
  `IAccProjectService`, `IAccDocumentService`, and `AccItemRef`.
- `SiNet.Infrastructure.Autodesk` now owns a **control-plane seam only**:
  mode resolution, remote health/diag probes, and local API-key diagnostics.
- `AddSiNetAutodesk()` now registers ACC control-plane services, but it still does **not**
  register project/document adapters or any write-heavy provisioning/file services.
- There is **no ACC LegacyBridge adapter** in `src/SiNet.LegacyBridge` today.
- The legacy production host still owns the real ACC behavior: token/bootstrap wiring, remote-vs-local
  provisioning, filing/file IO, metadata, inbox reconciliation, health checks, and admin probes.
- The most dangerous migration area is **not UI**; it is the **file-filing / metadata / provisioning**
  pipeline plus the runtime-sensitive `AccService:BaseUrl` split.
- The smallest safe first slice is the **ACC control plane**: remote adapter/config/health/secret
  diagnostics, not the actual filing/write path.

## 2. Clean-side Inventory

| Artifact | Role | Current status |
| --- | --- | --- |
| `src/SiNet.Application/Abstractions/Autodesk/IAccProjectService.cs` | Clean port for discovering ACC projects | Port only; no implementation |
| `src/SiNet.Application/Abstractions/Autodesk/IAccDocumentService.cs` | Clean port for ACC item lookup by project/folder/file name | Port only; no implementation |
| `src/SiNet.Application/Abstractions/Autodesk/AccItemRef.cs` | Value object for resolved ACC items | Present; not yet flowing through real adapter |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceModeProvider.cs` | Clean port for resolving local vs remote ACC service mode | Implemented in slice 1 |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceHealthProbe.cs` | Clean port for probing remote ACC service health | Implemented in slice 1 |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceDiagnosticsProbe.cs` | Clean port for safe remote ACC diagnostics | Implemented in slice 1 |
| `src/SiNet.Application/Abstractions/Autodesk/IAccServiceKeyDiagnostics.cs` | Clean port for local ACC API-key diagnostics | Implemented in slice 1 |
| `src/SiNet.Application/Abstractions/Autodesk/AccServiceControlPlaneDtos.cs` | Mode/health/diag/key-info value objects | Implemented in slice 1 |
| `src/SiNet.Infrastructure.Autodesk/AutodeskServiceCollectionExtensions.cs` | DI entry point for ACC module | Registers control-plane services only |
| `src/SiNet.Infrastructure.Autodesk/ConfigurationAccServiceModeProvider.cs` | Resolves `AccService:BaseUrl` mode through the existing host configuration seam | Implemented |
| `src/SiNet.Infrastructure.Autodesk/HttpAccServiceHealthProbe.cs` | Remote `/v1/acc/health` adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/HttpAccServiceDiagnosticsProbe.cs` | Remote `/v1/acc/diag` adapter | Implemented |
| `src/SiNet.Infrastructure.Autodesk/VaultAccServiceKeyDiagnostics.cs` | Local ACC API-key metadata adapter | Implemented |
| `src/SiNet.App.Wpf/Admin/Security/SecretSetupViewModel.cs` | First native UI consumer of the ACC control-plane seam | Implemented for read-only status/diag display |
| `src/SiNet.App.Wpf/Admin/Settings/SettingsViewModel.cs` | Native ACC settings consumer of the control-plane seam | Implemented for read-only runtime display beside stored ACC settings |
| `src/SiNet.LegacyBridge/LegacyBridgeServiceCollectionExtensions.cs` | Temporary bridge slot | No ACC bridge wired |

Implication: the clean Autodesk module is now a **real control-plane seam**, but not yet a
migrated ACC runtime.

## 3. Legacy / Production Inventory

### 3.1 Runtime-sensitive host wiring

Main production behavior still lives in:

- `SiNetProjectManagerV2/App.xaml.cs`
- `SiNetProjectManagerV2/Services/AppConfiguration.cs`

These files control:

- Autodesk token-provider creation from vault-backed secrets
- `AccService:BaseUrl` mode switching
- remote `SiOffice.AccService` registration vs local in-process provisioning
- API-key wiring for remote `AccService`
- custom TLS/self-signed behavior for approved internal ACC service hosts

Additional host note:

- `src/SiNet.App.Wpf/App.xaml.cs` now explicitly calls `AddSiNetSecrets()` after `AddSiNet()`.
  This host-level wiring is intentional: `SiNet.App.Composition` remains `net10.0`, while
  `SiNet.Infrastructure.Secrets` is Windows-targeted. The ACC control-plane needs vault-backed
  diagnostics, but that dependency cannot be pushed directly into the cross-platform composition
  project without breaking the build graph.

### 3.2 Main concerns still owned by legacy

| Concern | Main runtime ownership |
| --- | --- |
| Auth / token plumbing | `MyOffice.AutodeskConnector.ITokenProvider` wiring in `SiNetProjectManagerV2/App.xaml.cs` |
| Remote/local provisioning split | `AccService:BaseUrl` block in `SiNetProjectManagerV2/App.xaml.cs` |
| Remote provisioning adapters | `SiNetProjectManagerV2/Services/RemoteAccProjectProvisioningService.cs`, `SiNetProjectManagerV2/Services/RemoteAccInboxProvisioner.cs` |
| Internal ACC service diagnostics | `SiNetProjectManagerV2/Services/Health/InternalAccServiceHealthCheck.cs` |
| AccService key diagnostics | `src/SiNet.Infrastructure.Secrets/AccServiceSecretDiagnostics.cs` |
| Filing / refile / file client / file store | Legacy DI registrations in `SiNetProjectManagerV2/App.xaml.cs` |
| Metadata / inbox reconciliation / recovery | Legacy DI registrations in `SiNetProjectManagerV2/App.xaml.cs` |
| Admin settings / probes / service-mode UI | `SiNetProjectManagerV2/WPF Window/ManagementSettingsWindow.AccService.cs`, `SiNetProjectManagerV2/WPF Window/SecretSetupWindow.xaml.cs`, `SiNetProjectManagerV2/Dialogs/UserGroupManagementWindow.xaml.cs` |

## 4. Boundary Rules Already Established

The legacy ACC docs are mature and should remain the source of truth for behavior during migration.
The most important rules already encoded in docs are:

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

### Comparatively safe first slice

The smallest safe migration slice is the **ACC control plane**, not the write-heavy data path:

- `AccService:BaseUrl` config interpretation
- remote adapter interfaces
- remote service health / diagnostics
- secret setup + AccService API-key diagnostics
- service-mode status/probe UX

This area is still non-trivial, but it is much safer than migrating filing semantics first.

### Highest-risk areas

1. **File filing / refile / storage path**
   - This is where side effects, source-of-truth transitions, and file-placement policy live.
2. **Metadata + inbox reconciliation**
   - Tied to persisted ACC identifiers and status semantics.
3. **Provisioning behavior drift**
   - The remote-vs-local split is runtime-sensitive and includes vault, API key, and TLS behavior.
4. **Admin capability asymmetry**
   - Some probe/admin workflows are local-only in practice and not fully mirrored by remote adapters.

## 6. Recommended First ACC Slice

Recommended first migration slice for ACC:

### ACC control-plane slice

Scope:

- document and isolate the `AccService:BaseUrl` mode switch
- formalize mode/health/diagnostics/key-info as clean ports
- keep remote health/probe behavior explicit
- keep filing, file-store, metadata writes, and MoveToProject semantics untouched

Why this slice first:

- it respects the existing service boundary,
- it avoids immediate write-path risk,
- it creates a clean seam for later ACC work,
- and it does not require immediate parity across the whole file-filing stack.

## 7. Guardrails

Until a separately approved slice says otherwise:

1. **Do not** start with `IProjectFileFilingService` replacement.
2. **Do not** bypass `SiOffice.AccService` in service mode.
3. **Do not** invent a second parallel ACC orchestration path.
4. **Do not** move ACC business rules into `SiNet.Infrastructure.Autodesk`.
5. **Do not** treat the current clean module as "already migrated" just because ports exist.
6. **Do not** merge Drive/Google file concerns into the ACC connector slice.

## 8. Immediate Next Step

Slice 1 is now in place. The next useful ACC work item is:

- consume the new control-plane seam from native admin/status UI,
- keep `IAccProjectService`, `IAccDocumentService`, provisioning, inbox bootstrap, filing,
  metadata writes, and MoveToProject semantics deferred to later slices.
