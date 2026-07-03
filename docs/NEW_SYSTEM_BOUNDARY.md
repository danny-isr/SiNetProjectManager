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
- Open legacy windows (`ActionPermissionWindow`, legacy `UserManagementWindow`, legacy `AddUserWindow`, legacy `SecretSetupWindow`, …)
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
| Service | `SqlUserManagementService` → `IUserManagementService` via **`SiNetDbContext`** + `SiNet.Infrastructure.Sql.Entities` |

Native user SQL must **not** use `SiNetSQL.Data`, `SiNetSQL.Models`, or `SiNetSQLDbContext`. Legacy monolith EF remains in the same assembly for other slices until migrated.

Menu items **ניהול משתמשים** / **הוספת משתמש** in `NewShellFactory` are gated by `AppFeatureCodes.UsersManage`.

## Native action permissions admin (2026-07-03)

| Surface | Location |
| --- | --- |
| Action permissions | `ActionPermissionsWindow` + `ActionPermissionsView` + `ActionPermissionsViewModel` |
| Service | `SqlActionPermissionAdminService` → `IActionPermissionAdminService` via **`SiNetDbContext`** + `SiNet.Infrastructure.Sql.Entities` |

Menu item **הרשאות פעולה** in `NewShellFactory` is gated by `AppFeatureCodes.ActionPermissionsManage`.
Native permissions SQL must **not** use `SiNetSQL.Data`, `SiNetSQL.Models`, or `SiNetSQLDbContext`.

## Native secret setup (2026-07-03, implemented)

| Surface | Location |
| --- | --- |
| Keys and secrets | `SecretSetupWindow` + `SecretSetupView` + `SecretSetupViewModel` |
| Service | `CredentialVaultSecretSetupService` → `ISecretSetupService` in `SiNet.Infrastructure.Secrets` |
| Export / Import | `SecretProvisioningFileService` — encrypted `.secrets` (AES-256-CBC + PBKDF2, legacy `SNET` format) |
| AccService | `AccServiceSecretDiagnostics` — Generate (32-byte Base64) + Test (presence/format or network diag) |
| Google OAuth | `GoogleClientSecretsMaterializer` + `VaultGoogleClientSecretsPathProvider` |

Menu item **מפתחות וסודות** in `NewShellFactory` is gated by `AppFeatureCodes.SystemSettingsWrite`.
Opens native `SecretSetupWindow` — **not** legacy `SiNetProjectManagerV2.WPF_Window.SecretSetupWindow`.

**Credential Vault is the single source of truth** for secret values. Google OAuth follows:
Vault → materialized file (`%LocalAppData%/SiNet/Secrets/google-client-secrets.json`) → config fallback
only when Vault is empty (with explicit warning). The materialized file exists for consumers that still
require a filesystem path (e.g. `GmailClientProvider`); it is not a second source of truth.

Boundary tests: `NativeSecretSetupTests.cs`, `NativeSecretSetupGapTests.cs` (export encryption, import
catalog-only, preview without values, AccService generate/test, Google materializer LocalAppData-only,
no legacy window, no `SiNetSQL.MVVM`).

The general **הגדרות** menu item remains a disabled placeholder until a native system-settings surface
exists (distinct from keys/secrets).

## Native logging (Stage 4, 2026-07-03)

| Port | Adapter | Registration |
| --- | --- | --- |
| `IAppLogger` | `SerilogAppLogger` | `AddSiNetSerilogLogging()` in `AddSiNetNewSystemGraph()` |

New System modules inject `IAppLogger` only — **not** `SiNetSQL.Services.AppLogger` or Serilog types.
`SerilogAppLogger` forwards to the host's existing `Log.Logger` pipeline (one sink graph, no duplicate
logger). Scaffold `AddSiNet()` still uses `ConsoleAppLogger` for standalone dev.

See [`LOGGING.md`](./LOGGING.md). Boundary tests: `NewSystemLoggingBoundaryTests.cs`.

## Native settings (Stage 5 slice 2, 2026-07-03)

| Surface | Location |
| --- | --- |
| Settings | `SettingsWindow` + `SettingsView` + `SettingsViewModel` |
| Per-user JSON | `JsonAppSettingsService` → `IAppSettingsService` |
| Global DB | `SqlSystemSettingsService` → `ISystemSettingsQuery/CommandService` |
| Status colors | `SqlStatusColorSettingsService` → `IStatusColorSettingsService` |
| Runtime logging | `LegacyLoggingRuntimeApplier` → `ILoggingRuntimeApplier` |

Menu **הגדרות** gated by `AppFeatureCodes.SystemSettingsWrite`. Does **not** open legacy settings windows.

See [`SETTINGS.md`](./SETTINGS.md). Tests: `SettingsStage5BoundaryTests.cs`, `NativeSettingsSurfaceTests.cs`.

## Revoked pattern (do not extend)

| Pattern | Status |
| --- | --- |
| `IActionPermissionAdminWindowFactory` → legacy `ActionPermissionWindow` | **Removed** — use native `ActionPermissionsWindow` |
| Legacy `SecretSetupWindow` (`SiNetProjectManagerV2.WPF_Window`) | **Removed** from NewShell — use native `SecretSetupWindow` |
| `IUserManagementWindowFactory` → legacy `UserManagementWindow` | **Removed** |
| `IAddUserWindowFactory` → legacy `AddUserWindow` | **Removed** |
| `UserManagementPortAdapter` / SiNetSQL adapter for New System | **Not used** |
| Changes to `SiNetSQL.MVVM` for New System consumption | **Stopped** |

## Architecture tests

Enforced by `NewSystemBoundaryTests.cs`, `Admin/NewShellNativeUserAdminMenuTests.cs`, and
`Admin/NativeSecretSetupTests.cs` + `Admin/NativeSecretSetupGapTests.cs`:

- csproj / assembly must not reference SiNetSQL or V2
- Forbidden legacy identifiers (`SiNetSQL.MVVM`, `Dialogs.*Window`, legacy factories, legacy `SecretSetupWindow`)
- Native user menu allowed when opening `UserListWindow` / `AddUserDialogWindow`
- Native secret setup: encrypted export, catalog-only import, AccService generate/test, Google vault-first materializer

## Rebuild path (remaining)

| Capability | Target |
| --- | --- |
| User inline edit / `UpdateUsersAsync` | Extend native user admin + `SqlUserManagementService` |
| General system settings surface | Native settings UI (replaces disabled **הגדרות** placeholder in `NewShellFactory`) |

See also: [`APP_SHELL.md`](./APP_SHELL.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md).
