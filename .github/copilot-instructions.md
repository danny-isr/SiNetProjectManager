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
- **No Direct Migrations:** Copilot/agents are strictly forbidden from executing migrations automatically (`Add-Migration`, `Update-Database`, `dotnet ef migrations *`, `dotnet ef database update`, efbundle).
- **Migration files are immutable:** Never create, edit, rewrite, patch, delete, or "fix" files under `Migrations/` (including Designer and ModelSnapshot). Only change model/configuration; the developer runs `dotnet ef migrations add`. If apply fails, diagnose only — do not patch `Up`/`Down`.
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
- **ACC Inbox Tagging & Metadata:** 
  - Treat ACC and ACC custom attributes as the source of truth; DB fields are cache/helper only.
  - **Mailbox project association (separate concern):** Gmail project labels are the source of truth for “email filed to project” (`IsFiledToProject`). Do **not** treat SQL `EmailInboxMessage.ProjectId` / thread mapping as proof the message is filed. See `docs/EMAIL_ACC_SOURCE_OF_TRUTH.md` and `EmailSystemPrinciples` §6.6. Label modify for filing/triage is allowed; Gmail is not a Storage Destination for file content.
  - Write ACC metadata before updating the DB cache. If the ACC metadata write fails, roll back any DB cache updates.
  - On metadata read failures, warn and continue using the ProjectFileInstanceId legacy fallback; do not fail the overall process.
  - Log metadata-read failures and fallback usage for later reconciliation.
  - Do not treat metadata or custom-attribute read failures as proof that the physical file is missing. Require ACC reconciliation to verify physical existence in ACC before marking files as missing or building viewer/opening data.
  - Do not build an ACC Viewer URL from DB identifiers as a fallback. ACC reconciliation must verify the file exists in ACC and provide opening/viewing data; do not derive or fabricate viewer URLs from cached DB identifiers.
  - ACC Inbox layout is centralized in `AccInboxLayout`: message folders use `MSG_`, message-folder files are `00_Email.pdf` and `manifest.json`, and regular attachments are stored under the `Attachments` child folder.
  - `AccInboxReconciliationService`, `ShowAttachmentInAccAsync`, and `MoveToProjectProcessActionHandler` must resolve file existence from ACC layout-aware lookup results, not DB-only identifiers.
  - The shortened move-target alternative attribute name is `SiInbox.Move.TargetAltId`; do not reintroduce `SiInbox.Move.TargetProjectAlternativeId`.
  - For ACC Inbox/reconciliation/open/MoveToProject closure work, update documentation and code only within this domain. Treat ACC as the source of truth and the DB strictly as a cache/helper.
  - Do not leave active DB-only open/move fallbacks in place; disable or clearly mark legacy fallbacks and enable them only after an explicit safety review.
  - Do not change schema, migrations, ModelSnapshot, TokenProvider, Bim360Service, service architecture, or unrelated areas as part of Inbox/reconciliation/MoveToProject changes.

  - Round 9: MoveToProject outcome enrichment (backward compatibility)
    - Keep MoveToProject outcome enrichment backward compatible with existing systems.
    - Preserve all existing properties, including `ProjectFileInstanceId`; do not remove or rename them.
    - Add only nullable or default-valued fields for new enrichment data.
    - Avoid any schema or migration changes, including ModelSnapshot edits.
    - Do not change refile flows, broad UI/inspection behavior, `UpsertInstanceAsync`, or the `ProjectFileInstance` model/table/foreign-key layout.
    - Use application-level handling (nullable fields and fallback logic) to support new fields while guaranteeing no breaking schema or model changes.
    - Do not change TokenProvider, Bim360Service, service architecture, or unrelated areas when implementing enrichment changes.
  - **ACC physical-existence source of truth (MoveToProject and similar flows)**
    - In MoveToProject and similar flows, the ACC item / version / folder is the source of truth for the physical existence of a file.
    - `ProjectFileInstanceId` is a runtime projection / legacy fallback and is NOT a persisted source of truth.
    - Do not add a new mandatory dependency on `ProjectFileInstanceId` for filed-state, task-completion, or open/view decisions.
    - Do not introduce parallel fallback mechanisms; reuse the existing ACC reconciliation / `AccItemId` path.
    - Task-completion reporting (e.g. `ReviewMaterialFiled`) must be able to fire from ACC state alone, even when no new `ProjectFileInstance` is created in the current run.
  - ACC Inbox custom attribute definition provisioning (STRICT POLICY)
    - Implement only a small, approved fix in the Inbox provisioning path.
    - Create/ensure SiInbox.* definitions only in the ACC Inbox project/folder via the existing Bim360Service.EnsureCustomAttributeDefinitionsAsync.
    - Do not auto-create or provision definitions from SetItemCustomAttributesAsync.
    - Do not modify schema, migrations, ModelSnapshot, ProjectFileInstance, Refile/MoveToProject flows, or UI as part of this fix.
    - Keep provisioning changes limited in scope; avoid broader provisioning or schema changes.

### SiOffice / ACC Service Boundary (STRICT POLICY)
- `SiOffice.AccService` is the central service boundary for ACC operations in remote/service mode.
- Remote WPF clients should call the service instead of running local privileged ACC orchestration when `AccService:BaseUrl` is configured.
- Office Inbox ensure is exposed through the service endpoint and should be treated as the central remote provisioning path.
- Do not change service architecture, TokenProvider, Bim360Service, or authentication flows without explicit approval for that scope.
- Do not add startup-time browser authorization or unrelated ACC bootstrap behavior as part of Inbox/reconciliation/open/MoveToProject work.

## 10. Project Management
- **Default Office Management Project ID:** The default project ID for Office Management is 136, not 126. This ID is used for project-independent workflows.
- **Workflow File Classification:** Filing of files can only happen AFTER project creation. During the "פתיחת פרויקט" stage, the user should open the source email to view attachments (already uploaded to ACC Inbox), but actual filing occurs after the project exists. The task should instruct the user to open the email, review files, and create the project before filing.

## 11. Documentation-Driven Development Workflow

**The documentation is the source of truth.** All work follows this cycle:

1. **Update documentation first** — Before changing code, update the relevant 
   Principles document or gap register to reflect the desired state.
2. **Implement to match documentation** — Write/modify code to align with 
   the documented principles.
3. **Test the implementation** — Verify the code works as documented.
4. **If issues arise, fix documentation first** — When testing reveals a 
   gap or incorrect assumption, update the documentation to be more precise, 
   then fix the code to match.

**Key rule:** Never leave code that contradicts documentation. If code must 
differ from docs temporarily, add an explicit gap entry with status and timeline.

**Copilot behavior:** When asked to implement a feature or fix:
- First check if relevant documentation exists in `Docs\Domains\` or `Docs\Decisions\`
- If documentation is missing or unclear, propose documentation updates before code
- After code changes, verify documentation alignment per §6a in `Docs\README.md`