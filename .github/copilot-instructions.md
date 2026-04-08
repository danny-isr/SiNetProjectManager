# GitHub Copilot Instructions - C# Desktop Project

## 1. Safety & Stability (CRITICAL)
- **Do Not Break Working Code:** Never delete or refactor operational functions unless specifically instructed.
- **Dead Code Removal:** Only suggest deleting unused code after 100% certain analysis (no references, no functional impact).
- **Risk Assessment:** If a change might break existing logic, STOP and research a safer alternative. Present risks to the user before proceeding.

## 2. Task Management
- **Task Priority Rule:** New open tasks must be appended to the END of the queue (Max+1), NOT inserted at Priority 1. Reopened tasks also go to the end of the queue. On close: clear priority and re-rank to close the gap.

## 3. Core Principles & Efficiency
- **DRY (Don't Repeat Yourself):** Zero tolerance for code duplication. Logic must be centralized.
- **Single Source of Truth:** Reuse existing tools. Do not re-implement existing processes.
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