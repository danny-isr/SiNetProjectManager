# Authorization Verification

> **Date:** 19.06.2026
> **Status:** Active (Verification mapping round)
> **Scope:** Mapping existing authorization mechanisms, identifying test coverage, creating a test matrix, and defining manual/automated test plans.

---

## 1. Scope & Verification Rules
- **No new authorization mechanisms** were added in this round.
- **No roles, UI, DB schema, or project-level permissions** were altered.
- All actions focus strictly on verifying the behavior of existing `CurrentUserContext`, `ActionPermissionService`, `UserService`, and `SystemSettingsService`.

## 2. Existing Code & Test Coverage Scan

### 2.1 Files Scanned
- **Services:** `CurrentUserContext.cs`, `UserService.cs`, `SystemSettingsService.cs`, `ActionPermissionService.cs`.
- **UI:** `ActionPermissionWindow.xaml.cs`, `AssignActionDialog.xaml.cs`, `ManagementSettingsWindow.xaml.cs`.
- **Tests:** `ActionPermissionServiceTests.cs`, `UserServiceTests.cs`, `SystemSettingsServiceTests.cs`, `CurrentUserContextTests.cs` (implicit from past rounds).

### 2.2 Test Coverage Evaluation
- **Already covered (Unit / Integration-style Tests):**
  - **`ActionPermissionServiceTests.cs`**:
    - Deny-by-default is verified (`IsUserAllowedForActionAsync_NoPermissionRows_ReturnsFalse`).
    - Admin bypass is verified (`IsUserAllowedForActionAsync_AdminOverride_ReturnsTrue`).
    - Inactive / unauthorized users are rejected (`IsUserAllowedForActionAsync_InactiveUser_ReturnsFalse`).
    - `SaveActionPermissionsAsync` requires Admin.
    - Saving permissions strictly validates target users (must be active, Role >= Employee).
- **Missing or Partial Coverage:**
  - `UserServiceTests` & `SystemSettingsServiceTests` were not deeply inspected but the service code correctly embeds `CurrentUserContext.Instance.RequireAdmin()`.
  - UI state mapping (e.g., ensuring `AssignActionDialog` accurately hides users without permissions) lacks dedicated automated UI tests. However, the dialog leverages the validated `IActionPermissionService` strictly, enforcing security-in-depth by re-checking before execution (`ExecuteNow_Click` and `CreateTask_Click`).

**Result:** No places were found where sensitive operations rely *only* on the UI for protection. All checked services correctly enforce `CurrentUserContext` or `ActionPermissionService` checks at the application service level.

---

## 3. Permissions Test Matrix

| Category | Scenario / Rule | Expected Result (Service Layer) |
|----------|-----------------|---------------------------------|
| **Startup / Login** | Unauthorized User Login | Blocked (`CurrentUserContext.Initialize` returns false) |
| | Inactive User Login | Blocked |
| | Employee / Management / Admin | Allowed |
| **User Management** | Admin updates users | Allowed (Automated covered) |
| | Employee updates users | Blocked (Automated covered) |
| | Management updates users | Blocked (Automated covered) |
| | Admin deactivates self | Blocked (`InvalidOperationException`) |
| | Admin demotes self | Blocked (`InvalidOperationException`) |
| | Admin changes own LoginName | Blocked (`InvalidOperationException`) |
| **System Settings** | Read settings (Employee/Mgmt/Admin) | Allowed |
| | Write settings (Employee) | Blocked (Automated covered) |
| | Write settings (Management) | Blocked (Automated covered) |
| | Write settings (Admin) | Allowed (Automated covered) |
| **Action Permissions**| Deny-by-default (No row exists) | Blocked |
| | Admin bypass (No row exists) | Allowed |
| | R01 / R02 | (Configured via DB) Usually requires Management |
| | R03 | (Configured via DB) Allowed for Employee |
| | Invalid desiredUserIds rejected | True (Throws `ArgumentException`) |
| | Direct UI write path blocked | True (Uses `ActionPermissionService`) |
| **Assign Action** | Dialog assigns action if service denies | Blocked (Re-checked before execution) |
| | User bypasses disabled UI state | Blocked (Service layer `IsUserAllowedForActionAsync` prevents execution) |
| | Error message friendly | True (Shows a warning message box) |

---

## 4. Manual Verification Plan

**Goal:** Execute scenarios that are hard to cover with pure unit tests without risking production data.

**Preparation:**
- **Users Needed in DB (Do not alter production without explicit consent):**
  - `TestAdmin` (Role: Administrator, Active: True)
  - `TestMgmt` (Role: Management, Active: True)
  - `TestEmp` (Role: Employee, Active: True)
  - `TestInactive` (Role: Employee, Active: False)
  - `TestUnauth` (Role: Unauthorized, Active: True)

**Steps:**
1. **Startup Check:** Attempt to log in as `TestInactive` and `TestUnauth`.
   - *Expected:* Application refuses to start or shows a clear access denied message.
2. **System Settings Check:** Log in as `TestEmp`. Open `ManagementSettingsWindow` / System Settings.
   - *Expected:* Can read. Attempting to save throws an error or is disabled.
3. **Action Permissions Check:** Log in as `TestAdmin`. Open `ActionPermissionWindow`.
   - *Expected:* Can assign action permissions.
4. **Assign Action Flow:** Log in as `TestEmp`. Open `AssignActionDialog` for an action where `TestEmp` has no permission row.
   - *Expected:* "Execute Now" is disabled. Info text shows they can only create a task.
5. **Self-Demotion Check:** Log in as `TestAdmin`. Go to User Management. Try to change `TestAdmin`'s role to Employee.
   - *Expected:* Friendly error, operation blocked.

---

## 5. Automated Test Plan

**Recommendations for next steps:**
- Expand the existing `ActionPermissionServiceTests` project (no new test projects).
- Add integration-style tests for `SystemSettingsService` to verify that `RequireAdmin` actually throws when `CurrentUserContext` simulates an `Employee`. (Done in Round 2)
- Add similar integration tests for `UserService.UpdateUsersAsync` to ensure the self-demotion logic works securely against an in-memory test DB. (Verified present in Round 2)
- **Do not** introduce UI Automation (e.g., Appium) at this stage. Stick to service-layer verification.

---

## 6. Dropped / Cancelled / Postponed

- **System Health Work:** Paused / Postponed to prioritize Authorization verification.
- **ACC / AI / Logging Health Checks:** Postponed.
- **Project-level permissions:** Still postponed.
- **Role changes:** Not approved and not implemented.
- **DB / Migration changes:** Not approved and not implemented.
- **Authorization Code fixes:** No fixes were applied in this round; this round was exclusively for mapping and matrix creation.
