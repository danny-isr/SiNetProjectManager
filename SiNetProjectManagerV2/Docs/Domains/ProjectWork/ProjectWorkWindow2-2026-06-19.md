# ProjectWork / Window 2

- **Updated date:** 19.06.2026
- **Status:** Active — Source of truth
- **Scope:** The `ProjectWorkView` screen, unified file and folder tree loading, Drag & Drop mechanics, ACC viewing via WebView2, project-contextual commands, and the manual ACC integration capabilities.

## 1. Purpose
The **ProjectWorkView** screen is the main workspace where a user manages project folders and files.

## 2. Main UI responsibilities
- Selecting a project and viewing a unified hierarchical tree of folders.
- Viewing alternatives and versions of identified project files.
- Opening files, creating folders, renaming alternatives, and managing files through Drag & Drop.
- Viewing project files through Autodesk Construction Cloud (ACC) in the multi-tab viewer panel.
- The window is **not** responsible for Workflow management logic.
- The window is **not** responsible for copying files from emails into the project (`MoveToProject`); this is the role of the `ProjectFileFilingService`.

## 3. Main files and classes
- **View:** `ProjectWorkView.xaml` (Contains the unified TreeView and ACC viewer components).
- **Code-behind:** `ProjectWorkView.xaml.cs` (Contains Drag & Drop events, pop-out/dock viewer mechanisms, and ViewModel binding).
- **ViewModel:** `ProjectWorkViewModel` (Responsible for loading projects, applying filters, watching the filesystem, handling ACC manual logic, and building the tree).
- **Node classes:** 
  - `ProjectFolderNode` (Represents a folder)
  - `ProjectFileNode` (Represents a parent file definition)
  - `AlternativeNode` (Represents a file variant/copy)
  - `VersionNode` (Represents a specific physical version of the file)
- **Helper classes:** `FileHelpers` (CRUD operations on files), `FolderOpener` (opening and managing folders), `CompositeChildrenConverter` (for merging folders and files into a single tree).
- **Services / contexts:** `ActiveProjectContext` (Maintains the active project ID as a Singleton), `FileIndexService`, and specific `IFileStore` implementations.

## 4. Architecture and data flow

### 4.1. Project selection and filtering
- **Project selection:** Handled via a smart ComboBox with search capabilities. Selecting a project updates the `ActiveProjectContext`, cancels previous filesystem watchers, and initiates the loading of a new tree.
- **Filters:** Allows filtering by project type, status, and assigned user (mapped against `TypeOfProjectInProjects`).

### 4.2. Unified tree behavior
- **Folder and file tree:** The tree panel displays a unified structure merging DB data and local storage folders.
- **Context Menu actions:** Dynamic commands based on node type (open, rename, delete, add alternative, replace, manual ACC uploads).

### 4.3. File scanning and indexing
- The system scans directories and uses `FileIndexService` with storage-specific `IFileStore` implementations.
- A `ScannedFile` is created during scanning. Its parsed identity (`sf.Parsed`) determines the project, alternative, and version (utilizing the internal parser).
- `IntegrateScannedFile` handles the alignment between the physical scanned file, the DB-defined `ProjectFile`, and the storage destination handling.

### 4.4. File watching and refresh
- A `FileSystemWatcher` watches the file-server roots for changes.
- Metadata companion/sidecar files are explicitly ignored during the watching process.
- Changes detected by the watcher schedule a debounced in-place rescan.
- The tree is not completely rebuilt from scratch during these rescans; the in-place rescan carefully preserves the TreeView expansion state for a seamless user experience.

### 4.5. Drag and drop behavior
- **Drop on `ProjectFileNode`:** Creates or selects a new alternative through the existing alternative dialog.
- **Drop on `AlternativeNode`:** Adds the dropped file as a new version to the existing alternative.
- **Drop on `VersionNode`:** Starts a replace flow through the `FileReplaceService`.
- **ACC-backed versions:** If a replace drop occurs on an ACC-backed version, the system utilizes the dedicated ACC replace path.

### 4.6. ACC viewer behavior
The ACC viewer operates in the viewer panel alongside the tree panel. It provides a multi-tab interface rather than a single static view:
- **Tabs management:** Driven by `AccViewerTabs`, `SelectedAccTab`, and `HasAccTabs`.
- **Persistent WebView2:** There is one persistent WebView2 instance per tab to preserve navigation state.
- **Tab Strip UI:** Provides a tabbed interface for active views.
- **Tab triggers:** Tabs are opened or closed through interactions with an `AlternativeNode` and a checkbox on alternatives containing ACC versions.
- **Limits:** The maximum number of open tabs is enforced via the `SystemSettingKeys.AccViewerMaxTabs` setting.
- **Pop-out / Dock:** The viewer supports pop-out and dock behaviors managed through `AccDockToggleButton_Click`, `PopOutAccViewer`, and `DockAccViewer`.

### 4.7. Status and diagnostics
The bottom status bar provides continuous diagnostics and operational awareness:
- **Scan status / progress:** Displays the current state of file indexing.
- **Upload status:** Shows ongoing uploads and the in-flight upload count.
- **Badges:** 
  - **Extension conflict badge:** Warns if file extension rules are violated.
  - **ACC metadata issue badge:** Highlights discrepancies or errors in ACC-related metadata.
- **Interactivity:** Tooltips and double-click actions on badges provide extended diagnostic information.

## 5. Known limitations / Needs review
- **Manual ACC workflows:** The ViewModel contains manual ACC workflows (e.g., uploading an unfiled/user-folder file to ACC, marking with manual upload metadata, restoring orphan manual ACC uploads). These actions require further review to determine if they should remain exposed in the active context menus or be integrated into automated flows.

## 6. Dropped / cancelled / postponed
- Using the document `Docs/Archive/ProjectWork-Documentation.md` as an active source of truth — **cancelled**. The archived document is retained for historical purposes only.
- Writing active documentation in Hebrew — **cancelled**. All active repository documentation must be in English.
- Patch-note style documentation or "latest changes" blocks — **cancelled**. Documentation must be written as a coherent source of truth.
- Deleting the archive document — **not approved**. It is kept for historical reference.
- Copying the old archive document as-is — **cancelled**.
- Changing code as part of documentation alignment rounds — **not approved**.
- Reviving old mechanisms from the archive — **not approved** without an explicit decision.

> [!WARNING]
> **ProjectFileInstance Usage Rule:**
> `ProjectFileInstance` is **not the source of truth** for physical file existence and must not be treated as a permanent per-file placement registry. The active ProjectFiles principles define it as a runtime projection of the selected project's file state. Legacy database fields or compatibility properties may still exist, but Window 2 must not rely on `ProjectFileInstance` as the authoritative placement model.

## 7. Manual verification checklist
When testing or validating the behavior of the ProjectWork window, ensure the following use cases function correctly:
- [ ] Project selection and filters update the tree.
- [ ] Tree loading completes without freezing the UI.
- [ ] Folder scanning accurately reflects DB and filesystem folders.
- [ ] User-created folders are displayed correctly.
- [ ] Unassigned files are categorized correctly based on naming conventions.
- [ ] File open triggers the correct associated application.
- [ ] Drag/drop to a project file node triggers alternative creation.
- [ ] Drag/drop to an alternative node adds a version.
- [ ] Drag/drop to a version node triggers the file replace flow.
- [ ] ACC tab open/close functionality works from the alternative node checkbox.
- [ ] ACC pop-out/dock viewer functionality.
- [ ] Scan status updates correctly on the bottom bar.
- [ ] Upload status and in-flight count display accurately.
- [ ] Extension conflict badge appears when relevant.
- [ ] ACC metadata badge functions correctly.
