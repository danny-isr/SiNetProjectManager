# New System boundary (2026-07-02)

> **Decision:** New System is **not** a legacy host.

## Core statements

```text
New System is not a legacy host.
No legacy windows in NewShell.
No legacy ViewModels in App.Wpf.
No SiNetSQL.MVVM.
No V2 Dialogs.
Migration means rebuild into new architecture, not wrapping old windows.
```

New System admin capabilities (Action Permissions, User Management, Add User) will be built **only** as native `SiNet.App.Wpf` surfaces backed by `SiNet.Application` ports and `SiNet.Infrastructure.Sql` — never by opening legacy windows from the New System menu.

## Rule

`SiNet.App.Wpf` must **not**:

- Reference `SiNetSQL`, `SiNetProjectManagerV2`, or `SiNetSQL.MVVM` (project or assembly)
- Open legacy windows (`ActionPermissionWindow`, `UserManagementWindow`, `AddUserWindow`, …)
- Depend on legacy ViewModels, `SiNetProjectManagerV2.Dialogs`, or host window factories for admin UI

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

## Architecture tests (boundary hardening)

Enforced by `src/SiNet.App.Wpf.Tests/Boundary/NewSystemBoundaryTests.cs`:

| Guard | What it checks |
| --- | --- |
| csproj references | `SiNet.App.Wpf.csproj` has no `SiNetSQL` / `SiNetProjectManagerV2` project refs |
| assembly references | Loaded `SiNet.App.Wpf` assembly does not reference legacy assemblies |
| source scan | All `.cs` / `.xaml` under `src/SiNet.App.Wpf` exclude forbidden legacy identifiers |
| NewShellFactory | No legacy admin factories, feature gates, Hebrew admin menu labels, or legacy window types |
| factory policy | Only `IEmailWindowFactory` may exist as a window factory in App.Wpf (native surfaces) |

See also `Identity/NewShellAuthorizationArchitectureTests.cs` for authorization-port usage in the shell.

## Rebuild path (next slices)

| Capability | Target |
| --- | --- |
| Action Permissions Admin | New view + VM in `SiNet.App.Wpf`; write port in `Application`; SQL in `Infrastructure.Sql` |
| User Management | New view + VM in `SiNet.App.Wpf`; `IUserManagementService` impl in `Infrastructure.Sql` |

Menu items return only when the **native** New System surface exists.

## Temporary legacy adapters (read-only, host)

Until Infrastructure.Sql owns identity reads, the **V2 host** may still register read/query adapters for shell gating (`ICurrentUserContext`, `IAuthorizationQueryService`, …) when running New System mode. These do **not** open legacy UI.

See also: [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md), [`APP_SHELL.md`](./APP_SHELL.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md).
