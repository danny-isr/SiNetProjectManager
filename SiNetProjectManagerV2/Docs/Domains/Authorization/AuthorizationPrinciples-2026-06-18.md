# Authorization Principles

- **Decision date / Updated date:** 18.06.2026
- **Status:** Active — source of truth for internal application authorization.
- **Scope:** Internal application-level user authorization roles, action permissions, and security enforcement.

## 1. Purpose
Define the internal application authorization model for SiNet Project Manager. This document is the source of truth for:
- Which users may enter the application.
- What roles exist.
- What each role may do.
- How action permissions work.
- What is considered a sensitive operation.
- Where authorization must be enforced.
- How denied actions are surfaced and audited.

## 2. Scope
This document covers internal application authorization only. It does not replace Deployment / external authorization documentation for:
- Autodesk OAuth.
- Google OAuth.
- Service secrets.
- Token stores.
- WebView2 session profiles.
- `SiOffice.AccService` deployment authorization.

## 3. Core Principle: Deny by Default
The application authorization model is deny-by-default. A user must not receive access just because:
- A menu item is visible.
- A button is reachable.
- A window can be opened.
- No explicit restriction exists.
- An action has no permission rows configured.

If there is no explicit permission, the action is blocked. If an action should be open to all employees, that must be configured explicitly.

## 4. UI Visibility is Not Authorization
UI visibility is only a convenience / usability layer. Hiding or showing a menu item, button, tab, or window is not sufficient authorization. Every sensitive operation must be guarded at the service / business-operation level.

If an operation can be reached through a ViewModel, dialog, workflow, suggested action, background flow, task completion, direct command, or future UI path, the operation itself must still validate authorization.

## 5. Existing Mechanisms Must be Reused
Before creating any new authorization mechanism, check the existing mechanisms and extend them where appropriate. Existing mechanisms / concepts include:
- `CurrentUserContext`
- `AppUserRole`
- `Siusers`
- `ActionPermission`
- `UserService`
- Existing workflow / action handler dispatcher mechanisms

Do not create a parallel authorization system unless there is a documented reason and explicit approval.

## 6. User Roles
Internal roles are defined hierarchically. A higher role inherits the rights of lower roles:
- **`Unauthorized`:** Has no application access.
- **`Employee`:** The regular authorized user role.
- **`Management`:** Includes `Employee` permissions.
- **`Administrator`:** Includes `Management` and `Employee` permissions.

## 7. Login Requirements
A user may enter the application only if all of the following are true:
1. The user is identified through Windows Authentication.
2. The user exists in the `Siusers` table.
3. The user is active.
4. The user has a valid role other than `Unauthorized`.

If any condition fails, startup is blocked, a clear access-denied message is shown, and the event is logged / audited.

## 8. Role Responsibilities

### Employee
- **Allowed:**
  - Enter the application.
  - Use basic work areas.
  - View projects in phase 1.
  - Work with assigned tasks.
  - Work with emails and files only through allowed actions.
  - Execute actions explicitly allowed for the user.
  - View reports allowed for all users.
- **Not allowed:**
  - Manage users.
  - Change roles.
  - Change permissions.
  - Change system settings.
  - Create or update projects unless explicitly allowed.
  - Perform administrative workflow changes.
  - Access management-only reports.

### Management
- **Allowed:**
  - Everything an `Employee` can do.
  - Create projects.
  - Update project details.
  - Perform management-level project operations.
  - View management reports.
  - Advance workflows when allowed by role or action permission.
- **Not allowed:**
  - Manage users.
  - Change user roles.
  - Activate or deactivate users.
  - Manage action permissions.
  - Change system-wide settings.
  - Manage secrets or system credentials.

### Administrator
- **Allowed:**
  - Full application access.
  - Manage users.
  - Change roles.
  - Activate or deactivate users.
  - Manage action permissions.
  - Manage system settings.
  - Manage workflow policies.
  - Manage secrets / credentials / system connections.
  - Perform maintenance operations.

*Note: Even Administrator actions must still pass explicit authorization checks and must be audited.*

## 9. Action Permissions
In addition to role-based permissions, the application supports action-level permissions.
- **Example Action Codes:** `CreateProject`, `UpdateProject`, `CloseProject`, `ManageUsers`, `ManageActionPermissions`, `ManageSystemSettings`, `MoveEmailToProject`, `CreateTask`, `ImportFiles`, `ReplaceFileVersion`, `AdvanceWorkflow`, `ManageWorkflowPolicy`.
- **Rules:**
  - **Undefined action:** Blocked.
  - **Explicitly open to all employees:** Allowed for `Employee` and above.
  - **Explicitly assigned to users:** Allowed only for those users.
  - **Administrator:** Allowed by override, unless a future special rule explicitly blocks it.
  
*Note: The behavior "no permission rows exist, therefore everyone is allowed" is not an approved target behavior.*

## 10. Phase 1 Project Visibility
Project-level permissions are postponed to a future phase.
- **Phase 1 Rule:** All active `Employee` users may view all projects. However, project-changing operations are restricted by role and action permissions.
- **Future Phase Considerations:** User sees only projects assigned to them, user sees projects where they have tasks, external users see only explicitly assigned projects, confidential projects are restricted.

## 11. Sensitive Operations
The following operations require service-level authorization:
- **User management:** Add user, update user, change role, change `IsActive`, change `LoginName`, change ACC user type, deactivate user. (Required permission: **Administrator only**).
- **Action permission management:** Add permission, remove permission, open action to all users, restrict action, change action permission policy. (Required permission: **Administrator only**).
- **Project management:** Create project, update project, close project, change project status, change major project relationships, change project type, change project folder structure. (Required permission: **Management or Administrator**, or explicit action permission).
- **Tasks:** `Employee` can work on own tasks. `Management` can manage project tasks. `Administrator` can manage all tasks. Workflow-bound tasks should be changed only through the agreed workflow / completion / action-handler mechanism.
- **Emails:** `Employee` may use only explicitly allowed email actions. `Management` may perform management-level email actions. `Administrator` can perform all email actions.
- **Files:** Viewing is allowed in phase 1 according to project visibility; changing files requires action permission, `Management`, or `Administrator` depending on the operation.
- **Workflow:** `Employee` may advance only allowed / assigned workflow actions. `Management` may perform management workflow operations. `Administrator` manages workflow definitions and policies.
- **Reports:** `R01`, `R02`, Financial, or Management reports require **Management and Administrator**. `R03` is allowed for **Employee, Management, Administrator**. Report settings require **Administrator** unless otherwise decided.

## 12. Inactive Users
When a user is inactive:
- They cannot log in.
- They should not appear in new assignment lists or new action-permission assignment lists.
- Their historical tasks and actions remain for audit / history.
- They should not be physically deleted by default.

## 13. Audit Requirements
Every sensitive action must be auditable. The audit should include: acting user, Windows login, timestamp, machine / session if available, action name, target entity, previous value, new value, success / failure, and denial reason when blocked.
Audit is required for: successful login, blocked login, role change, `IsActive` change, user creation / update, action permission change, project creation / update / close, project status change, workflow advancement, workflow policy change, file import, ACC upload, system setting change, and credential / secret configuration change.

## 14. User-Facing Denial Message
When an action is blocked, show a clear non-technical message:
> “You do not have permission to perform this action. If you believe this is a mistake, contact the system administrator.”

Do not expose technical details to the user; these should be written to logs only.

## 15. What We Do Not Do Now
- Do not implement code changes in this documentation round.
- Do not add database migrations or change schema.
- Do not edit `ModelSnapshot` or delete code.
- Do not create a parallel authorization mechanism.
- Do not introduce new fallback behavior.
- Do not implement project-level permissions in this phase.

## 16. Dropped / Cancelled / Postponed
- Treating UI visibility as sufficient authorization — dropped / not approved.
- Treating “no action permission rows” as open access — dropped as target behavior / candidate for code alignment.
- Project-level permissions — postponed to a future phase.
- External-user permissions — postponed to a future phase.
- Confidential per-project restrictions — postponed to a future phase.
- Parallel authorization mechanism — not approved.
- Authorization-only changes inside ViewModels — not approved; authorization must be enforced at service / business-operation level.

## 17. Cross-Links / References
- [Docs/README.md](../../README.md)
- [ArchitecturePrinciples-2026-05-26.md](../Architecture/ArchitecturePrinciples-2026-05-26.md)
- [ServiceCatalog-2026-05-26.md](../Architecture/ServiceCatalog-2026-05-26.md)
- [UiPrinciples-2026-05-26.md](../UI/UiPrinciples-2026-05-26.md)
- [WorkflowPrinciples-2026-05-26.md](../Workflow/WorkflowPrinciples-2026-05-26.md)
- [DiagnosticsPrinciples-2026-05-26.md](../Diagnostics/DiagnosticsPrinciples-2026-05-26.md)
- [DeploymentPrinciples-2026-05-26.md](../Deployment/DeploymentPrinciples-2026-05-26.md)
