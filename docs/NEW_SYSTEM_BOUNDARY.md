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

User Management and Add User are implemented as **native** `SiNet.App.Wpf.Admin.Users` surfaces backed by `SqlUserManagementService` in Infrastructure.Sql — not legacy `UserManagementWindow` / `AddUserWindow` or `SiNetSQL.MVVM`.

## Rule

`SiNet.App.Wpf` must **not**:

- Reference `SiNetSQL`, `SiNetProjectManagerV2`, or `SiNetSQL.MVVM` (project or assembly)
- Open legacy windows (`ActionPermissionWindow`, legacy `UserManagementWindow`, legacy `AddUserWindow`, …)
- Depend on legacy ViewModels or `SiNetProjectManagerV2.Dialogs`

Legacy startup (`StartupMode.Legacy` → `SiNetProjectManagerV2.MainWindow`) may continue to use legacy UI unchanged.

## Allowed stack for New System

```text
SiNet.Application          → ports, DTOs, commands
SiNet.Infrastructure.Sql   → DB implementations (no WPF, no ViewModels)
SiNet.App.Wpf              → views, viewmodels, shell, navigation
```

## Native user admin (2026-07-02)

| Surface | Location |
| --- | --- |
| User list | `UserListWindow` + `UserManagementView` + `UserManagementViewModel` |
| Add user | `AddUserDialogWindow` + `AddUserView` + `AddUserViewModel` |
| Service | `SqlUserManagementService` → `IUserManagementService` |

Menu items **ניהול משתמשים** / **הוספת משתמש** in `NewShellFactory` are gated by `AppFeatureCodes.UsersManage`.

## Revoked pattern (do not extend)

| Pattern | Status |
| --- | --- |
| `IActionPermissionAdminWindowFactory` → legacy `ActionPermissionWindow` | **Removed** |
| `IUserManagementWindowFactory` → legacy `UserManagementWindow` | **Removed** |
| `IAddUserWindowFactory` → legacy `AddUserWindow` | **Removed** |
| `UserManagementPortAdapter` / SiNetSQL adapter for New System | **Not used** |
| Changes to `SiNetSQL.MVVM` for New System consumption | **Stopped** |

## Architecture tests

Enforced by `NewSystemBoundaryTests.cs` and `Admin/NewShellNativeUserAdminMenuTests.cs`:

- csproj / assembly must not reference SiNetSQL or V2
- Forbidden legacy identifiers (`SiNetSQL.MVVM`, `Dialogs.*Window`, legacy factories)
- Native user menu allowed when opening `UserListWindow` / `AddUserDialogWindow`

## Rebuild path (remaining)

| Capability | Target |
| --- | --- |
| Action Permissions Admin | New view + VM in `SiNet.App.Wpf`; SQL in `Infrastructure.Sql` |
| User inline edit / `UpdateUsersAsync` | Extend native user admin + `SqlUserManagementService` |

See also: [`APP_SHELL.md`](./APP_SHELL.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md).
