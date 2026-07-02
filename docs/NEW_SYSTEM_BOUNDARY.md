# New System boundary (2026-07-02)

> **Decision:** New System is **not** a legacy host. Migration means re-implementing capabilities in the new architecture — not wrapping old windows.

## Rule

`SiNet.App.Wpf` must **not**:

- Reference `SiNetSQL`, `SiNetProjectManagerV2`, or `SiNetSQL.MVVM`
- Open legacy windows (`ActionPermissionWindow`, `UserManagementWindow`, `AddUserWindow`, …)
- Depend on legacy ViewModels or host window factories

Legacy startup (`StartupMode.Legacy` → `SiNetProjectManagerV2.MainWindow`) may continue to use legacy UI unchanged.

## Allowed stack for New System

```text
SiNet.Application          → ports, DTOs, commands
SiNet.Infrastructure.Sql   → DB implementations (no WPF, no ViewModels)
SiNet.App.Wpf              → views, viewmodels, shell, navigation
```

## Revoked pattern (do not extend)

| Pattern | Status |
| --- | --- |
| `IActionPermissionAdminWindowFactory` → legacy `ActionPermissionWindow` | **Removed** from New System menu |
| `IUserManagementWindowFactory` → legacy `UserManagementWindow` | **Removed** |
| `IAddUserWindowFactory` → legacy `AddUserWindow` | **Removed** |
| `UserManagementPortAdapter` in V2 DI for New System | **Removed** from `AddSiNetIdentityLegacyAdapters` |
| Changes to `SiNetSQL.MVVM` for New System consumption | **Stopped** |

## Rebuild path (next slices)

| Capability | Target |
| --- | --- |
| Action Permissions Admin | New view + VM in `SiNet.App.Wpf`; write port in `Application`; SQL in `Infrastructure.Sql` |
| User Management | New view + VM in `SiNet.App.Wpf`; `IUserManagementService` impl in `Infrastructure.Sql` |

Menu items return only when the **native** New System surface exists.

## Temporary legacy adapters (read-only, host)

Until Infrastructure.Sql owns identity reads, the **V2 host** may still register read/query adapters for shell gating (`ICurrentUserContext`, `IAuthorizationQueryService`, …) when running New System mode. These do **not** open legacy UI.

See also: [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md), [`APP_SHELL.md`](./APP_SHELL.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md).
