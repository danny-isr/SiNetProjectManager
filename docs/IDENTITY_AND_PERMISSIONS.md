# Identity and Permissions Target

> **Status:** Active target specification (documentation slice — 2026-07-02)  
> **Scope:** Application identity, roles, action permissions, user management, and shell/menu availability for the **New System** refactor.  
> **Read together with:** [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md), [`APP_SHELL.md`](./APP_SHELL.md), [`AI_DEVELOPMENT_GUIDE.md`](./AI_DEVELOPMENT_GUIDE.md), [`MIGRATION_MAP.md`](./MIGRATION_MAP.md), [`PROJECTS.md`](./PROJECTS.md).

This document is the **active source of truth** for identity and authorization **target behavior**.  
It does **not** authorize code, schema, or login-flow changes until explicitly approved in a follow-up slice.

---

## 1. Purpose

The New System is extracting features from the legacy WPF host into clean Application ports and WPF surfaces. Identity and permissions are cross-cutting: almost every migrated surface eventually needs to know **who** is acting and **whether** an operation is allowed.

Goals of this document:

1. **Separate concerns** that are often conflated in legacy code:
   - **Current User** (attribution / context)
   - **Authentication** (who signed in)
   - **Role authorization** (hierarchical app role)
   - **Action permissions** (per-action user allow-lists)
   - **User management** (administrative CRUD on `SIUser`)
   - **ACC permissions** (Autodesk project file access — separate domain)
2. **Preserve proven legacy rules** where they are verified in code and tests — without copying legacy singletons into new WPF view models.
3. **Define Application ports** for future slices so authorization moves behind services, not scattered `if (IsAdmin)` checks.
4. **Document gaps** between legacy docs, legacy UI, and actual service enforcement — without guessing.

**Out of scope for this slice:** UI implementation, login changes, role changes, new ActionCodes, DB/schema/migrations, or wiring authorization into `NewShellViewModel`.

---

## 2. Current State Summary

### 2.1 New System (refactored stack)

| Piece | Location | Today |
| --- | --- | --- |
| **Current User port** | `src/SiNet.Application/Identity/ICurrentUserContext.cs` | Minimal: `int? UserId` only. Runtime-only; not persisted; **not** an authorization authority. |
| **Host adapter** | `SiNetProjectManagerV2/Services/CurrentUserContextAdapter.cs` | Forwards `UserId` from legacy `CurrentUserContext.Instance.CurrentUserId`. Registered in V2 DI as singleton. |
| **Shell display** | `NewShellFactory.DescribeUser` → `NewShellViewModel.CurrentUserDisplay` | Shows `משתמש #{id}` when `UserId` is present; otherwise `משתמש לא ידוע`. Shell does **not** check roles or permissions. |
| **Authorization ports** | — | **None yet** in `SiNet.Application` for roles, action permissions, or user management. |

### 2.2 Legacy host (production authority today)

| Piece | Location | Role |
| --- | --- | --- |
| **Authentication + role context** | `SiNetSQL/Services/CurrentUserContext.cs` | Singleton. Windows identity → `SIUser` lookup → role helpers (`IsAdmin`, `IsManagement`, `IsEmployee`). Startup gate in Legacy path. |
| **User CRUD** | `SiNetSQL/Services/Users/UserService.cs` | Admin-only writes; self-protection rules on update. |
| **Action permissions** | `SiNetSQL/Services/Authorization/ActionPermissionService.cs` | Deny-by-default; Administrator bypass; Admin-only save. |
| **System settings writes** | `SiNetSQL/Services/SystemSettingsService.cs` | Read: any authenticated user (after startup). Write: `RequireAdmin()`. |
| **UI menu gates** | `SiNetProjectManagerV2/MainWindow.xaml.cs` | `RequireAdminAccess` / `RequireManagementAccess` before opening sensitive windows. **UX only** — services re-check. |
| **Action permission admin UI** | `ActionPermissionWindow.xaml.cs` | Admin gate at window open + `ActionPermissionService.SaveActionPermissionsAsync` enforces Admin. |
| **Assign action UI** | `AssignActionDialog.xaml.cs` | Loads authorized users via service; re-checks `IsUserAllowedForActionAsync` before execution. |

### 2.3 Startup paths (important gap)

| Path | Authorization |
| --- | --- |
| **Legacy** (`RunLegacyStartup`) | Calls `AuthorizeCurrentUser()` → `CurrentUserContext.Initialize(db)` → blocks inactive / missing / `Unauthorized` users. |
| **New System** (`RunNewSystemStartup`) | Calls `AuthorizeCurrentUser()` after optional DEBUG role selector (same rules as Legacy). `ICurrentUserContext.UserId` populated when auth succeeds. |

### 2.4 Legacy documentation vs code (explicit discrepancies)

| Topic | Legacy doc says | Code actually does |
| --- | --- | --- |
| **Management + user management** | `UserRolesAndPermissions-2026-06-17.md`: Management inherits Employee and adds "employee management tools". | `UserService.AddUserAsync` / `UpdateUsersAsync` call `RequireAdmin()`. `MainWindow` opens User Management via `RequireAdminAccess`. **User CRUD is Administrator-only** in enforced code. |
| **Management inheritance** | Doc + enum comments: hierarchical inheritance. | **Confirmed in code:** comparisons use `Role >= AppUserRole.X` (`IsManagement`, `IsAdmin`, action permission Employee floor). |
| **Action permission default** | Doc + service XML: deny-by-default. | **Confirmed:** empty permission rows → denied (except Administrator bypass). |
| **Administrator bypass** | Doc + tests: Admin bypasses action checks. | **Confirmed:** `user.Role >= AppUserRole.Administrator` → allowed. |

When this target doc and legacy marketing-style role descriptions conflict, **service-layer code + unit tests win** until Product explicitly revises roles.

---

## 3. Concepts

### 3.1 Current User

**Definition:** The authenticated application user for this process/session, represented minimally as **`SIUser.Id`** when known.

**New System port:** `ICurrentUserContext.UserId`

| Rule | Detail |
| --- | --- |
| **Source** | Legacy: `CurrentUserContext.Instance.CurrentUserId` after successful `Initialize`. New host adapter exposes the same id through the port. |
| **Null meaning** | No authenticated application user is known. Callers must treat this as **unknown**, not as anonymous/system. |
| **Never invent** | Callers must **not** substitute `0`, `-1`, or a default employee id when `UserId` is null. Fall back to explicit user input or block the operation. |
| **Attribution use** | Recording who completed a task, who filed an email, audit fields, R03 self-service paths, status-color personalization, etc. |
| **Not for authorization** | `ICurrentUserContext` must **not** decide whether an action is allowed. No `IsAdmin` on this port. |
| **Not persisted by port** | The port reflects host runtime state; it does not write `SIUser` or session stores. |
| **Display** | Shell/surfaces may show friendly name, but that comes from a **future profile/read service**, not from widening the port ad hoc. |

### 3.2 Authentication

**Definition:** Binding the Windows interactive identity to a row in **`dbo.SIUser`** and rejecting users who may not use the app.

**Legacy algorithm** (`CurrentUserContext.Initialize`):

1. Read `WindowsIdentity.GetCurrent().Name`.
2. Match `SIUser.LoginName` (case-insensitive; fallback: username suffix after `\`).
3. Reject if row missing → `HasAccess = false`.
4. Reject if `IsActive == false`.
5. Reject if `Role == Unauthorized`.
6. Otherwise success; user is at least **Employee** (any role ≥ Employee passed step 5).

**Target (unchanged until approved):**

| Scenario | Legacy behavior | New System target |
| --- | --- | --- |
| User not in `SIUser` | Startup blocked (Legacy); message box. | **Open question:** should New System block too? |
| Inactive user | Blocked at startup. | Same rule when auth is wired. |
| `Unauthorized` role | Blocked at startup. | Same rule when auth is wired. |
| Windows identity source | Remains authoritative for now. | No username/password login in scope. |
| DEBUG test mode | `DebugAuthorizationRoleSelectorWindow` can mutate DB role for manual tests only. | Not a production auth mechanism. |

**Important:** Do **not** change login behavior in a code slice until this document is approved **and** the startup-path decision (§10 Q4) is resolved.

### 3.3 App Roles

**Storage:** `SIUser.Role` → `AppUserRole` enum (`src/SiNet.Infrastructure.Sql/Models/AppUserRole.cs`).

**Hierarchy:** Numeric, **inclusive upward** — higher role satisfies lower thresholds:

```text
Unauthorized = 0   → no app access
Employee     = 1   → standard work
Management   = 2   → inherits Employee capabilities + management-only features
Administrator= 3   → inherits Management + Employee + admin-only operations
```

**Enforcement pattern (legacy, to preserve):**

```csharp
Role >= AppUserRole.Management   // "full access" / management tools
Role >= AppUserRole.Administrator // admin-only services
```

#### Capability matrix (target — aligned to **enforced** code)

| Capability | Employee | Management | Administrator |
| --- | --- | --- | --- |
| Core work (tasks, email, files, project views) | ✅ | ✅ | ✅ |
| Open/create project UI entry (`RequireManagementAccess`) | ❌ | ✅ | ✅ |
| Reports / templates / management reports (`RequireManagementAccess`) | ❌ | ✅ | ✅ |
| Read system settings | ✅ | ✅ | ✅ |
| Write system settings | ❌ | ❌ | ✅ (`SystemSettingsService.SetAsync`) |
| Add / edit users, change roles, ACC type, active flag | ❌ | ❌ | ✅ (`UserService`) |
| Manage action permissions | ❌ | ❌ | ✅ (`ActionPermissionService.Save…`) |
| Bypass action-permission allow-list | ❌ | ❌ | ✅ (runtime check in `IsUserAllowedForActionAsync`) |

> **Note:** Legacy prose that assigns "employee management" to Management is **not** reflected in user CRUD enforcement. Treat that doc line as stale/marketing until Product redefines it.

**Unauthorized** remains a stored role meaning "exists in DB but must not log in" — not the same as "unknown Windows user".

### 3.4 Action Permissions

**Definition:** Fine-grained allow-list: which **`SIUser.Id`** values may execute a specific **ActionCode**.

**Storage:** `ActionPermission` rows (`ActionCode`, `UserId`, `IsActive`, …) — see Infrastructure.Sql model.

**ActionCode source today:** string names of `ActionFollowUp` enum values, e.g.:

| ActionCode | Display (he-IL) |
| --- | --- |
| `NewProjectDialog` | יצירת פרויקט חדש |
| `ProjectPicker` | שיוך לפרויקט קיים |
| `TaskCreationDialog` | יצירת / שיוך משימה |
| `FileImportDialog` | ייבוא קבצים |
| `DecisionDialog` | העברה להחלטה |
| `DisciplineDialog` | הוספת תחום |
| `WorkflowAdvanceDialog` | קידום תהליך |

Configured in `ActionPermissionWindow` static list; persisted per user via `SaveActionPermissionsAsync`.

#### Action Permission vs Role Authorization

| | **Role authorization** | **Action permission** |
| --- | --- | --- |
| **Question** | "Is this user Management/Admin?" | "Is this user on the allow-list for action X?" |
| **Scope** | Broad feature classes | Specific workflow/email follow-up actions |
| **Default** | Deny below threshold (`Require*` throws / UI blocks) | **Deny-by-default** if no active rows for action |
| **Admin** | `Administrator` role | **Bypass:** Administrators allowed even with no rows |
| **Grant mechanism** | Change `SIUser.Role` (Admin-only) | Admin UI / `SaveActionPermissionsAsync` |
| **Subject** | Current user's role | **Per target user id** (executor), e.g. "can *this employee* run FileImport?" |

**Target rules (preserve legacy semantics):**

- **Deny-by-default** for non-Administrators when zero active permission rows exist for an ActionCode.
- **Administrator bypass** always allowed (including inactive/unauthorized checks skipped only after user is validated as Admin in DB).
- Permission rows reference **`UserId` only** — not roles — in the current model.
- Inactive users or `Role < Employee` cannot be granted action permissions (validated on save).
- **Do not filter by DisplayName** — only `ActionCode` + `UserId`.

**Future consideration (not in model today):** role-based or group-based action grants. Document only; do not implement without approval.

### 3.5 User Management

**Legacy surfaces:** `UserManagementViewModel`, `AddUserViewModel`, `MainWindow` menu entries.

**Service authority:** `IUserService` / `UserService`

| Operation | Who may perform (enforced) | Notes |
| --- | --- | --- |
| List users + open-task counts | Readable by UI after login; service has no read gate beyond DB access | VM assumes admin UI entry already gated |
| **Add user** | **Administrator** | `AddUserAsync` → `RequireAdmin()` |
| **Update users** (name, email, login, role, active, ACC type, MasterPlan link) | **Administrator** | `UpdateUsersAsync` → `RequireAdmin()` |
| Change another user's role / active / ACC type | **Administrator** | — |
| **Self-protection** (admin editing self) | Allowed with restrictions | Cannot deactivate self; cannot demote self below Admin; cannot change own `LoginName` |

**Deactivation policy (legacy UI + doc):**

- Before toggling `IsActive` off, UI warns if `OpenTaskCount > 0` (`UserManagementViewModel`).
- Service does not auto-block deactivation on open tasks — warning is UX; admin may proceed.

**ACC type (`AccUserType`):** separate from app role — controls ACC provisioning tier (`NoAccUser`, `Engineer`, `Admin`). Changing it triggers ACC membership reconciliation (`UserService.UpdateUsersAsync`).

### 3.6 Shell / Menu Availability

**New System shell** (`NewShellViewModel`, `NewShellFactory`):

| Does | Does not |
| --- | --- |
| Shows current user display string (context only) | Decide authorization |
| Lists **migrated-only** menu items with `IsAvailable` | Scan/copy legacy menu |
| Opens surfaces via DI/factories | Embed `CurrentUserContext.Instance.IsAdmin` checks |

**Target pattern for menu/feature visibility:**

```text
AuthorizationQueryService (future port)
        ↓
Shell / ViewModel sets MenuItem.IsAvailable or IsEnabled
        ↓
Opened surface / Application service re-checks before mutating state
```

**Legacy MainWindow pattern (reference, not to copy into shell VM):**

- `RequireAdminAccess` → sensitive admin tools.
- `RequireManagementAccess` → project creation, templates, reports.
- Many items have **no** gate (Employee-accessible work surfaces).

**UI hide vs disable:** **Open question** (§10 Q5). Target doc default: prefer **hide** unavailable menu entries for migrated shell items once a port exists; legacy often shows a message on click instead.

---

## 4. Source of Truth

| Concern | Source of truth (today) | New System direction |
| --- | --- | --- |
| **Who is logged in** | `CurrentUserContext` after `Initialize` (Legacy startup) | Same data via host adapter until native auth slice |
| **UserId for attribution** | `ICurrentUserContext.UserId` | Keep minimal port |
| **Role definitions** | `AppUserRole` enum + `SIUser.Role` column | Preserve values; no rename without migration slice |
| **Role checks** | `CurrentUserContext` helpers + service `Require*` | `IAuthorizationQueryService` (P3) |
| **Action permission rows** | `ActionPermission` table + `ActionPermissionService` | `IActionPermissionQueryService` (P4) |
| **ActionCode catalog** | `ActionFollowUp` names + `ActionPermissionWindow` display map | `ActionPermissionCodes` (P4) |
| **User records** | `SIUser` table via `UserService` | `IUserManagementService` (P5) |
| **ACC access tier** | `SIUser.AccUserType` + ACC bootstrap services | Remains **separate** from app Role (§10 Q8) |
| **System settings** | `SystemSettings` table + `SystemSettingsService` | Admin write rule preserved |

**WPF rule:** View models and views bind to **DTOs and ports**, never to `Siuser` EF entities in the New System stack.

---

## 5. Target Rules

1. **Separation:** Current User ≠ Authorization. Attribution uses `ICurrentUserContext`; permission checks use dedicated services.
2. **Service authority:** Every sensitive mutation or workflow execution must be authorized in the **Application/Infrastructure service**, even if the UI already hid the button.
3. **UI is UX:** Hiding/disabling controls improves usability; it is **not** sufficient protection.
4. **No invented identity:** Null `UserId` stays null until real authentication binds a user.
5. **Preserve hierarchy:** Keep `AppUserRole` numeric hierarchy and `>=` comparisons unless Product approves a breaking change.
6. **Preserve action semantics:** Deny-by-default + Administrator bypass + user-id allow-list.
7. **Shell neutrality:** `NewShellViewModel` / `NewShellFactory` must not accumulate role checks; they consume authorization **query** results from ports when added.
8. **Legacy bridge, not legacy leak:** Adapters may read legacy singletons at the composition root; feature code depends on Application ports only.
9. **ACC isolation:** ACC provisioning permissions (`AccUserType`, ACC APIs) are not substitutes for app Role or Action Permissions.
10. **Documentation-first:** Identity/authorization behavior changes require updating this file before code.

---

## 6. Legacy Compatibility

### 6.1 What stays in legacy temporarily

| Legacy component | Until replaced by |
| --- | --- |
| `CurrentUserContext` singleton | Host auth initialization + `ICurrentUserProfileService` (optional) |
| `CurrentUserContextAdapter` | Native Infrastructure identity adapter |
| `UserService`, `ActionPermissionService`, `SystemSettingsService` | Application-layer ports + Sql implementations |
| `MainWindow` menu gates | `IAuthorizationQueryService` consumed by New Shell |
| Admin/management windows (`AddUserWindow`, …) | Migrated WPF surfaces (future slices) |
| `UserManagementWindow` | New System menu via `IUserManagementWindowFactory` (with P6 admin menu slice) |
| `ActionPermissionWindow` | New System menu via `IActionPermissionAdminWindowFactory` (P6); visual clone deferred |

### 6.2 What New System must not do yet

- Copy `RequireAdminAccess` helpers into `NewShellViewModel`.
- Re-implement permission SQL in WPF.
- Change `ActionCode` strings or `ActionFollowUp` enum names (breaks DB rows).
- Add schema/migrations for groups/project-permissions.

### 6.3 Bridge wiring (today)

```text
Windows Identity
    → CurrentUserContext.Initialize (Legacy startup only today)
        → CurrentUserContextAdapter
            → ICurrentUserContext.UserId
                → feature surfaces (attribution only)
```

Authorization checks use Application ports (`IAuthorizationQueryService`, `IActionPermissionQueryService`) via host adapters; legacy services remain source of truth until native implementations exist.

---

## 7. Application Ports

### 7.1 Existing (keep)

```csharp
namespace SiNet.Application.Identity;

public interface ICurrentUserContext
{
    int? UserId { get; }
}
```

**Contract:** runtime-only; nullable; never authorization authority.

### 7.2 Implemented — profile read (P2)

```csharp
public sealed record CurrentUserProfileDto(
    int UserId,
    string DisplayName,
    string? LoginName,
    AppRole Role,
    bool IsActive,
    int? MasterPlanEmployeeId);

public interface ICurrentUserProfileService
{
    Task<CurrentUserProfileDto?> GetCurrentUserAsync(
        CancellationToken cancellationToken = default);
}
```

**Host adapter:** `SiNetProjectManagerV2/Services/LegacyCurrentUserProfileService.cs` reads the authenticated legacy `CurrentUserContext` singleton (no WPF → EF). Shell display uses `CurrentUserProfileDisplay.Format`.

### 7.3 Implemented — role / feature authorization queries (P3)

```csharp
public interface IAuthorizationQueryService
{
    Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, ...);
    Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, ...);
}
```

**Constants:** `AppFeatureCodes` — stable feature code strings.

**Mapping:** `AppFeatureAuthorization` — minimum role per feature; unknown codes throw `ArgumentException` (deny-by-default, never silent allow).

| FeatureCode (`AppFeatureCodes`) | Minimum role |
| --- | --- |
| `ShellOpenEmailSurface` | Employee |
| `ShellOpenInspectionSurface` | Employee |
| `ProjectCreate` | Management |
| `ReportsManagement` | Management |
| `SystemSettingsWrite` | Administrator |
| `UsersManage` | Administrator |
| `ActionPermissionsManage` | Administrator |

**Host adapter:** `SiNetProjectManagerV2/Services/LegacyAuthorizationQueryService.cs` → legacy `CurrentUserContext` (hierarchical `Role >= required`).

**NewShell:** `NewShellFactory.BuildMigratedOnlyMenu()` resolves item visibility via `IAuthorizationQueryService` + `AppFeatureCodes` — no `CurrentUserContext` / `IsAdmin` in `SiNet.App.Wpf`.

### 7.4 Implemented — action permission queries (P4)

```csharp
public interface IActionPermissionQueryService
{
    Task<bool> CanUserExecuteActionAsync(string actionCode, int userId, ...);
    Task<bool> CanCurrentUserExecuteActionAsync(string actionCode, ...);
    Task<IReadOnlyList<UserRefDto>> GetAuthorizedUsersForActionAsync(string actionCode, ...);
}
```

**DTO:** `UserRefDto` (`UserId`, `DisplayName`, `LoginName`).

**Constants:** `ActionPermissionCodes` — documents legacy `ActionFollowUp` enum names; unknown codes are not approved silently (deny-by-default via empty permission rows).

**Host adapter:** `SiNetProjectManagerV2/Services/LegacyActionPermissionQueryService.cs` → legacy `ActionPermissionService` (deny-by-default, Administrator bypass, inactive/unauthorized denied).

**Current user:** `CanCurrentUserExecuteActionAsync` uses `ICurrentUserContext.UserId`; returns `false` when `UserId` is `null` — never invents an id.

**UI:** Action permission **management** window remains the legacy `ActionPermissionWindow` implementation; the New System shell opens it via `IActionPermissionAdminWindowFactory` (P6) when `ActionPermissions.Manage` is authorized.

### 7.5 Implemented — user management (P5)

```csharp
public interface IUserManagementService
{
    Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(...);
    Task AddUserAsync(CreateUserCommand command, ...);
    Task UpdateUsersAsync(IReadOnlyList<UpdateUserCommand> updates, ...);
}
```

**DTOs:** `UserSummaryDto`, `CreateUserCommand`, `UpdateUserCommand`, `AppAccUserType` (mirrors legacy `AccUserType`).

**Host adapter (shared):** `SiNetSQL/Services/Users/UserManagementPortAdapter.cs` → legacy `UserService`.

**UI:** `UserManagementViewModel` and `AddUserViewModel` route user operations through `IUserManagementService` via `UserManagementPortAdapter` (constructors still accept `IUserService` for legacy DI). New System shell opens admin windows via host factories when authorized.

### 7.6 DI registration direction

Modular host extensions (P7):

```text
AddSiNetIdentityLegacyAdapters()   → ICurrentUserContext, profile, authorization, action permission, user management ports
AddSiNetNewSystemGraph()           → project context + shell + admin window factories
```

Full container split (`AddSiNetClean` vs `AddSiNetWithLegacyBridge`) remains deferred per `ARCHITECTURE_TARGET.md`.

Layering:

```text
SiNet.Application          → ports + DTOs + enums
SiNetSQL                   → UserManagementPortAdapter (shared legacy adapter)
SiNetProjectManagerV2      → composition root + host-only adapters (CurrentUserContext, action permission)
SiNet.App.Wpf              → consumes ports only; no EF
```

**Do not implement all ports in one slice.** Pick the smallest port needed by the next migrated feature.

---

## 8. Migration Plan

Phased, documentation-driven slices:

| Phase | Deliverable | DB/UI impact |
| --- | --- | --- |
| **P0 — this slice** | `docs/IDENTITY_AND_PERMISSIONS.md` (this file) | None |
| **P1 — auth parity decision** | New System calls `AuthorizeCurrentUser` before shell | ✅ Implemented |
| **P1.5 — DEBUG role selector** | Shared `DebugAuthorizationRoleSelectorWindow` on New + Legacy paths | ✅ Implemented |
| **P2 — profile display** | `ICurrentUserProfileService` + shell display | ✅ Implemented |
| **P3 — authorization queries** | `IAuthorizationQueryService` + NewShell menu gating | ✅ Implemented |
| **P4 — action permission port** | `IActionPermissionQueryService`; migrated surfaces that execute actions use it | ✅ Implemented (read-only port + adapter; admin UI still legacy) |
| **P5 — user management port** | `IUserManagementService`; migrate User Management UI | ✅ Implemented (port + adapter; admin UI still legacy) |
| **P6 — action permission admin UI** | `ActionPermissionWindow` in New System menu via factory | ✅ Implemented (legacy window; host factory) |
| **P7 — composition split** | Modular DI: `AddSiNetIdentityLegacyAdapters`, `AddSiNetNewSystemGraph` | ✅ Partial (host extensions; full clean/legacy container split deferred) |

Each phase ends with: tests on service behavior, doc/code alignment check, explicit note in [`MIGRATION_MAP.md`](./MIGRATION_MAP.md).

**Not planned until approved:** project-level permissions, team/group grants, role-based action permissions, non-Windows authentication.

---

## 9. Guardrails

**Always (identity/authorization slices):**

- No DB writes except through existing approved services until a migration slice says otherwise.
- No schema / migrations / ModelSnapshot changes in read/query ports.
- No new ActionCodes without Product + data migration plan.
- No authorization logic in `NewShellViewModel` beyond binding `IsAvailable` from a port.
- No widening `ICurrentUserContext` with role flags — use profile/authorization ports.
- Re-check permissions in services before sensitive operations.

**Never in New System WPF:**

```csharp
if (CurrentUserContext.Instance.IsAdmin) { ... }  // direct singleton in ViewModel
```

**Preferred:**

```csharp
if (await _authorization.CanCurrentUserAccessFeatureAsync("Users.Manage", ct)) { ... }
// AND UserService still calls RequireAdmin() on write
```

---

## 10. Open Questions

Do **not** guess — resolve with Product / legacy owner before implementation.

| # | Question | Current evidence |
| --- | --- | --- |
| **Q1** | Does **Management** inherit **all** Employee capabilities in every screen? | Enum hierarchy says yes (`>= Employee`). Some UI gates use Management-only or Admin-only explicitly; no counter-example found for inherited Employee features. |
| **Q2** | Does **Administrator** always bypass Action Permissions? | **Yes** in `ActionPermissionService` + tests. |
| **Q3** | Are Action Permissions **user-only**, or will Role/Group grants be added? | **User-only today** (`ActionPermission.UserId`). Groups/project grants: **future only**. |
| **Q4** | Should **New System** block startup with legacy `AuthorizeCurrentUser` **now**? | **Yes (P1 implemented):** New System calls `AuthorizeCurrentUser()` after DB connection + DI; shutdown on failure. |
| **Q5** | Should unauthorized shell menu items be **hidden** or **disabled**? | Legacy MainWindow mostly shows message on click. Target leans **hide** for New Shell once port exists. |
| **Q6** | **ProjectSelector User filter** — `SIUser.Id`, `ProjectAssignments.AssignedToId`, or `Project.Worker` name? | Legacy Email filter uses `ProjectAssignments.AssignedToId`. DTO today carries `AssignedUserName` (worker string) only; filter **deferred** (`PROJECTS.md` §6). |
| **Q7** | **Project-level permissions** — exist or future? | Legacy AuthorizationVerification doc: **postponed**. No app-wide project ACL in scanned code. **Future only.** |
| **Q8** | Are **ACC permissions** separate from Application permissions? | **Yes.** `AccUserType` + ACC bootstrap/reconciler are a parallel concern to `AppUserRole` / Action Permissions. |
| **Q9** | Should Management ever manage users (per 2026-06-17 doc)? | Code enforces **Admin-only** user CRUD. Confirm whether doc or code should change. |
| **Q10** | When New System runs without auth, may migrated surfaces perform **writes**? | **Fail-closed:** query ports return `false`/empty when unauthenticated (`ICurrentUserContext.UserId == null`). Legacy write services (`UserService`, `ActionPermissionService.Save…`) call `CurrentUserContext.RequireAdmin()` / `RequireAdmin()` and throw `UnauthorizedAccessException` when there is no authenticated admin — never anonymous writes. |

---

## Appendix A — Files scanned for this document

**New / active docs & code**

- `src/SiNet.Application/Identity/ICurrentUserContext.cs`
- `SiNetProjectManagerV2/Services/CurrentUserContextAdapter.cs`
- `SiNetProjectManagerV2/App.xaml.cs` (startup + DI registration)
- `src/SiNet.App.Wpf/Shell/NewShellFactory.cs`, `NewShellViewModel.cs`
- `docs/APP_SHELL.md`, `docs/ARCHITECTURE_TARGET.md`, `docs/AI_DEVELOPMENT_GUIDE.md`, `docs/MIGRATION_MAP.md`, `docs/PROJECTS.md`

**Legacy behavior & docs**

- `SiNetSQL/Services/CurrentUserContext.cs`
- `SiNetSQL/Services/Users/UserService.cs`, `IUserService.cs`
- `SiNetSQL/Services/Authorization/ActionPermissionService.cs`, `IActionPermissionService.cs`
- `SiNetSQL/Services/SystemSettingsService.cs`
- `SiNetSQL/MVVM/UserManagementViewModel.cs`
- `SiNetProjectManagerV2/MainWindow.xaml.cs` (menu gates)
- `SiNetProjectManagerV2/Dialogs/ActionPermissionWindow.xaml.cs`
- `SiNetProjectManagerV2/Dialogs/AssignActionDialog.xaml.cs`
- `SiNetProjectManagerV2/Docs/Domains/Security/UserRolesAndPermissions-2026-06-17.md`
- `SiNetProjectManagerV2/Docs/Domains/Authorization/AuthorizationVerification-2026-06-19.md`
- `src/SiNet.Infrastructure.Sql/Models/AppUserRole.cs`, `AccUserType.cs`, `ActionPermission.cs`
- Tests: see **Appendix C — Identity test inventory**

---

## Appendix C — Identity test inventory

**GitHub repo (`SiNetProjectManager_GitHub`) — `src/SiNet.App.Wpf.Tests/Identity/`**

| File | Covers |
| --- | --- |
| `AppFeatureAuthorizationTests.cs` | Role hierarchy, feature access by role, unknown feature throws |
| `AppFeatureCodesCoverageTests.cs` | Feature × role matrix; all codes registered |
| `AppRoleEnumParityTests.cs` | `AppRole` ↔ `AppUserRole`, `AppAccUserType` ↔ `AccUserType` |
| `AuthorizationQueryServiceStubTests.cs` | Unauthenticated deny; unknown feature |
| `ActionPermissionQueryServiceStubTests.cs` | Null current user; `ActionPermissionCodes` |
| `NewShellAuthorizationArchitectureTests.cs` | No SiNetSQL in Wpf; factory/feature wiring |
| `CurrentUserProfileDisplayTests.cs` | Profile display formatting |

**SiNetSQL repo (sibling) — integration tests**

| File | Covers |
| --- | --- |
| `SiNetSQL.Tests/Services/Authorization/ActionPermissionServiceTests.cs` | Deny-by-default, admin bypass |
| `SiNetSQL.Tests/Services/Authorization/LegacyActionPermissionQueryServiceTests.cs` | P4 adapter parity |
| `SiNetSQL.Tests/Services/Users/UserServiceTests.cs` | Admin-only writes, self-protection |
| `SiNetSQL.Tests/Services/Users/UserManagementPortAdapterTests.cs` | P5 port adapter + lookup methods |

---

## Appendix B — Related deferred items

- **ProjectSelector User filter** — see [`PROJECTS.md`](./PROJECTS.md) §6 (`AssignedUserId` deferred).
- **Composition root split** — see [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md) / [`APP_SHELL.md`](./APP_SHELL.md) §5.
- **Debug authorization test mode** — DEBUG-only; not part of production target.
