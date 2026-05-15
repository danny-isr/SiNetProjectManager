# GitHub Copilot Instructions - C# Desktop Project
## Commit Messages

- Use [Conventional Commits](https://www.conventionalcommits.org/) format: `<type>(<scope>): <description>`
- Valid types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `ci`, `perf`
- Use scope to indicate the area of the codebase (e.g., `calc`, `tests`, `ci`)
- Write the subject line in imperative mood, lowercase, and keep it under 72 characters
- Do not end the subject line with a period
- Reference related GitHub issues in the footer when applicable (e.g., `Closes #42`)
- If a commit introduces a breaking change, include `BREAKING CHANGE:` in the footer

### Examples
## 1. Safety & Stability (CRITICAL)
- **Do Not Break Working Code:** Never delete or refactor operational functions unless specifically instructed.
- **Dead Code Removal:** Only suggest deleting unused code after 100% certain analysis (no references, no functional impact).
- **Risk Assessment:** If a change might break existing logic, STOP and research a safer alternative. Present risks to the user before proceeding.

## 2. Task Management
- **Task Priority Rule:** New open tasks must be appended to the END of the queue (Max+1), NOT inserted at Priority 1. Reopened tasks also go to the end of the queue. On close: clear priority and re-rank to close the gap.
- **UserGroup Task Assignment:** 
  - If a group has 1 member, auto-assign the task to that person.
  - If a group has multiple members, use the group's default assignee.
  - If no default assignee is set, the user must pick from the group members.
  - Each group should have a default assignee setting.
- **Group Assignment Notification:** If a workflow starts and can't create a task because the assigned group has no members, notify the user immediately. The workflow should not proceed without someone being assigned. Show a clear message about what's missing (empty groups, no default assignee).

## 3. Core Principles & Efficiency
- **DRY (Don't Repeat Yourself):** Zero tolerance for code duplication. Logic must be centralized.
- **Single Source of Truth:** Reuse existing tools. Do not re-implement existing processes.
- **Centralized Formatting:** Use centralized helper functions for building label names/formats instead of scattered inline formatting. Maintain a single source of truth for formatting patterns.
- **Resource Management:** Use `ReadOnlySpan<T>` for data parsing and `ValueTask` for high-frequency async methods.
- **CancellationToken:** Always implement and propagate `CancellationToken` in async methods.

## 4. Configuration & Variables
- **No Hard-coding:** Centralize all settings. Design variables to be easily moved to an app settings/configuration file later.
- **Global Consistency:** Use a structured approach (like a Settings Service) to manage global values instead of scattered static variables.

## 5. Multi-threading & UI
- **Responsive UI:** Never block the UI thread. Use `async/await` for all I/O or heavy computations.
- **Thread Safety:** Ensure thread-safe access to shared resources using `SemaphoreSlim` or `lock`.
- **Background Work:** Offload CPU-intensive tasks using `Task.Run()`.

## 6. Database & Migrations (STRICT PROTOCOL)
- **No Direct Migrations:** Copilot is strictly forbidden from executing migrations automatically.
- **Manual Migration Files:** Never manually edit migration files. Only change model/configuration files and let EF Core generate the migration.
- **PMC Commands:** Provide the exact command for 'Package Manager Console' (e.g., `Add-Migration [Name] -Context [Context]`).
- **Workflow:** Stop after providing the command and wait for user confirmation.

## 7. Architecture & Style
- **Dependency Injection:** Mandatory use of DI for all services and settings.
- **Modern C#:** Use C# 12 features (like Primary Constructors) when working in .NET 8+.
- **Naming:** PascalCase for classes/methods, _camelCase for private fields.

## 8. Data Extraction from Reports
- **Migration Imports:** Handle FINAL/filled reports, not templates. There are no `<<...>>` tags in these exported sheets; tags are replaced with actual values and background colors during export.
- **Data Extraction Process:** 
  - Scan the TEMPLATE to get cell-position-to-section mapping.
  - Read the FINAL report at those positions to get actual text and background color.
  - Map colors to statuses.

## 9. File Versioning Policy
- **Version Naming Convention:** The `Version` segment in the file naming convention `(ProjectNumber)-ProjectType-FileNumber-Alternative-Version-Name.ext` is NOT used as an actual version tracker. New files always get Version=1. Existing files with Version=2+ keep their name as-is (it's part of their identity).
- **Version Management:** No new versions are ever created through the system. ACC handles its own versioning natively — files are uploaded with their full original name, and ACC manages version history internally. The tree structure (Folder → File → Alternative → Version) remains unchanged.

## 10. Project Management
- **Default Office Management Project ID:** The default project ID for Office Management is 136, not 126. This ID is used for project-independent workflows.
- **Workflow File Classification:** Filing of files can only happen AFTER project creation. During the "פתיחת פרויקט" stage, the user should open the source email to view attachments (already uploaded to ACC Inbox), but actual filing occurs after the project exists. The task should instruct the user to open the email, review files, and create the project before filing.