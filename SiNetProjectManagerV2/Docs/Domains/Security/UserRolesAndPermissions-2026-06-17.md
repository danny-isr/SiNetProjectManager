# User Roles and Permissions Principles

- **Decision date / Updated date:** 17.06.2026
- **Status:** Active — source of truth for application security levels and ACC permissions.
- **Scope:** Application-level user authorization roles and Autodesk Construction Cloud (ACC) project-level permissions.

## 1. Application-Level User Roles (`AppUserRole`)
Application authorization is managed hierarchically using the `AppUserRole` enum stored in the `SIUser.Role` database column. A higher value inherits the rights of all lower values:

| Role | Database Value | Description / Permissions |
| --- | --- | --- |
| **`Unauthorized`** | `0` | Default state for new or deactivated users. No access to the application. |
| **`Employee`** | `1` | Regular employee. Standard access to core features: viewing and updating tasks, reading/linking emails, and managing project files. |
| **`Management`** | `2` | Manager access. Inherits `Employee` rights and adds access to financial data, project performance reports, and employee management tools. |
| **`Administrator`** | `3` | System administrator. Full system control, including editing system-wide configurations, DB settings, user management, and roles assignment. |

## 2. Autodesk Construction Cloud User Types (`AccUserType`)
Access to physical project files inside Autodesk Construction Cloud (ACC) is mapped via the `AccUserType` enum, which dictates the user's provisioned scope inside ACC projects:

| ACC User Type | Enum Value | Description / Scope |
| --- | --- | --- |
| **`NoAccUser`** | `0` | Default. The user is not provisioned with ACC access. |
| **`Engineer`** | `1` | Standard engineer-level access. Allows viewing, downloading, and uploading project documents. |
| **`Admin`** | `2` | Project Administrator. Full control over ACC integrations, folder provisioning, and metadata mapping. |

## 3. Core Security & Lifecycle Policies
1. **User Deactivation Safety:** Before toggling a user to inactive (`IsActive = false`), the system must check for open tasks (`OpenTaskCount > 0`). Administrators must be presented with a warning showing the count of open tasks assigned to that user before they can proceed.
2. **Implicit Fallback for Task Assignment:**
   - Single-member groups: Automatically assign the task to the sole member.
   - Multi-member groups: Assign the task to the group's default assignee. If not defined, prompt the administrator/system to select an assignee.
3. **Role Validation:** Any attempt to update a user's role or ACC type must be validated against database constraints. Changing roles requires `Administrator` privileges.
