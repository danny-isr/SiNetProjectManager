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
- **ViewModel:** `ProjectWorkViewModel` (Responsible for loading projects, applying filters, watching the filesystem, handling manual ACC logic, and building the tree. Implements `IActiveFileQueryService` and `IFileOpenService`).
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
- **Refresh projects:** The UI provides a refresh button/command that refreshes the project list on demand.

### 4.2. Unified tree behavior and sorting
- **Folder and file tree:** The tree panel displays a unified structure merging DB data and local storage folders.
- **Sorting behavior:** 
  - Versions are sorted by version number, placing the newest/highest version first.
  - Alternatives are sorted by the newest version date once integrated into the tree.

### 4.3. Context menu actions by node type
The tree nodes provide context-specific actions:
- **ProjectFolderNode:** 
  - Open folder.
  - Create folder.
  - Rename folder (for user-created folders).
  - Delete folder.
- **ProjectFileNode:** 
  - Add alternative.
  - Add alternative from template (available when `TemplateLocation` exists and conditions allow).
  - Accept/copy external file.
  - File-level Open With preference.
- **AlternativeNode:** 
  - Open latest version.
  - Rename alternative.
  - Delete alternative.
  - ACC tab checkbox behavior (toggles the ACC viewer tab for ACC-backed alternatives).
- **VersionNode:** 
  - Open version / open file.
  - Delete version / delete file.
  - Copy path or ACC URL.
  - Extract ZIP (when relevant to the file extension).
  - Upload to ACC (conditionally shown for local FileServer-backed versions).
  - Restore orphan from ACC (conditionally shown for orphan manual ACC uploads).
  - Delete orphan from ACC.
  - Version-level Open With preference.

### 4.4. File scanning and indexing
- The system scans directories and uses `FileIndexService` with storage-specific `IFileStore` implementations.
- A `ScannedFile` is created during scanning. Its parsed identity (`sf.Parsed`) determines the project, alternative, and version.
- `IntegrateScannedFile` handles the alignment between the physical scanned file, the DB-defined `ProjectFile`, and the storage destination handling.

### 4.5. Sidecar metadata
Sidecar metadata files are JSON companion files used behind the scenes. They are utilized for:
- Storing "Open With" preferences.
- Keeping the original dropped/source filename.
- Persisting file snapshot metadata.
- Tracking source file names.
- Explicitly excluding the sidecar/metadata companion files from watchers and extension conflict checks.

### 4.6. File watching and refresh
- A `FileSystemWatcher` watches the file-server roots for changes.
- Sidecar metadata files are explicitly ignored during the watching process.
- Changes detected by the watcher schedule a debounced in-place rescan.
- The tree is not completely rebuilt from scratch during these rescans; the in-place rescan carefully preserves the TreeView expansion state for a seamless user experience.

### 4.7. File placement / upload pipeline
When adding an alternative or version, the system utilizes an active pipeline:
1. The canonical file name is built through the naming convention.
2. A local staging copy is created in the project folder.
3. For the **FileServer** destination, the file remains local and is subsequently opened.
4. For **ACC** or **GoogleDrive**, the upload is performed through the matching `IFileStore`.
5. The in-flight upload state is marked and tracked through the `FileIndexService`.
6. An optimistic `VersionNode` is added to the tree after successful placement/upload.
7. The user receives a clear success or error indication upon completion.

### 4.8. Extension conflict prevention
Before placement or upload proceeds, the system executes an extension conflict prevention check:
- It checks whether the target store already contains a file with the same base-name but a different extension.
- If a conflict is detected, the upload/save operation is blocked and the user receives a warning.
- Sidecar metadata files are explicitly excluded from this conflict check.

### 4.9. Drag and drop behavior
- **Drop on `ProjectFileNode`:** Creates or selects a new alternative through the existing alternative dialog.
- **Drop on `AlternativeNode`:** Adds the dropped file as a new version to the existing alternative.
- **Drop on `VersionNode` (Version replace flow):** Starts a replace flow through the `FileReplaceService`.
  - For a **FileServer-backed** version, the path involves replacing the local file.
  - For an **ACC-backed** version, the system uses the dedicated ACC replace path.
  - This is not a blind overwrite; possible outcomes include cancel, no change, overwrite, renamed replace, or failure.
  - The tree is refreshed successfully after a completed replacement.

### 4.10. File opening behavior and Open With overrides
The system governs how files are opened:
- The default opening behavior follows the file/version's storage destination.
- **FileServer-backed** files open locally.
- **ACC-backed** files open in the ACC viewer when an `AccViewerUrl` exists.
- **Overrides:** A file-level "Open With" preference exists on the `ProjectFileNode`, and a version-level preference exists on the `VersionNode`.
- These preferences are persisted through sidecar metadata via the `FileOpenServiceRegistry`.

### 4.11. Storage destinations and storage badges
The system natively recognizes multiple storage destinations:
- **FileServer**, **ACC**, and **GoogleDrive**.
- Destinations are represented with specific storage destination icons/labels.
- **Storage badges** appear on the tree nodes. Crucially, the color of these badges reflects the *actual scanned/uploaded state* of the files, not merely the configured destination. 
- If no version is found, the badge displays a grey / unavailable state.

### 4.12. ACC viewer behavior
The ACC viewer operates in the viewer panel alongside the tree panel. It provides a multi-tab interface:
- **Tabs management:** Driven by `AccViewerTabs`, `SelectedAccTab`, and `HasAccTabs`.
- **Persistent WebView2:** There is one persistent WebView2 instance per tab to preserve navigation state.
- **Tab Strip UI:** Provides a tabbed interface for active views.
- **Tab triggers:** Tabs are opened or closed through interactions with an `AlternativeNode` and a checkbox on alternatives containing ACC versions.
- **Limits:** The maximum number of open tabs is enforced via the `SystemSettingKeys.AccViewerMaxTabs` setting.
- **Pop-out / Dock:** The viewer supports pop-out and dock behaviors managed through `AccDockToggleButton_Click`, `PopOutAccViewer`, and `DockAccViewer`.

### 4.13. Active file query and file open service exposure
After the tree is loaded, the `ProjectWorkViewModel` registers its availability through the existing registries:
- It implements and exposes `IActiveFileQueryService` and `IFileOpenService`.
- This allows other windows and features to query and open files directly from the currently loaded ProjectWork tree.
- Workflow completion remains firmly outside ProjectWork and does not use these queries to advance states.

### 4.14. Status and diagnostics
The bottom status bar provides continuous diagnostics and operational awareness:
- **Scan status / progress:** Displays the current state of file indexing.
- **Upload status:** Shows ongoing uploads and the in-flight upload count.
- **Badges:** 
  - **Extension conflict badge:** Warns if file extension rules are violated.
  - **ACC metadata issue badge:** Highlights discrepancies or errors in ACC-related metadata.
- **Interactivity:** Tooltips and double-click actions on badges provide extended diagnostic information.

## 5. Active manual behavior and Needs review
- **Manual ACC workflows:** The ViewModel currently exposes manual ACC workflows conditionally:
  - Local FileServer-backed versions can show an "Upload to ACC" action.
  - Orphan manual ACC uploads can show "Restore" or "Delete" actions.
- **Needs Review:** While active now, there is an open product decision regarding whether these manual workflows should remain user-facing long term, or whether they should be integrated into a more formal, automated flow.

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
- [ ] Refresh projects command updates the project list.
- [ ] Tree loading completes without freezing the UI.
- [ ] Folder scanning accurately reflects DB and filesystem folders.
- [ ] User-created folders are displayed correctly.
- [ ] Unassigned files are categorized correctly based on naming conventions.
- [ ] File open triggers the correct associated application.
- [ ] File-level Open With preference functions properly.
- [ ] Version-level Open With preference functions properly.
- [ ] Storage badge correctness matches state for FileServer / ACC / GoogleDrive.
- [ ] Sidecar metadata persists Open With choices correctly where practical.
- [ ] Extension conflict prevention blocks placement when extensions mismatch.
- [ ] Drag/drop to a project file node triggers alternative creation.
- [ ] Drag/drop to an alternative node adds a version.
- [ ] Drag/drop to a version node triggers the file replace flow for FileServer versions.
- [ ] Drag/drop to a version node triggers the file replace flow for ACC versions.
- [ ] Upload to ACC conditional action appears and functions when applicable.
- [ ] Restore orphan from ACC conditional action functions when applicable.
- [ ] ACC tab open/close functionality works from the alternative node checkbox.
- [ ] ACC pop-out/dock viewer functionality.
- [ ] Scan status updates correctly on the bottom bar.
- [ ] Upload status and in-flight count display accurately.
- [ ] Extension conflict badge appears when relevant.
- [ ] ACC metadata badge functions correctly.
