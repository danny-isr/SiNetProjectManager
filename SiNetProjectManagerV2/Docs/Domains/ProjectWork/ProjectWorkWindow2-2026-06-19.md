# 📂 ProjectWork / Window 2

- **Updated date:** 19.06.2026
- **Status:** Active — Source of truth
- **Scope:** The `ProjectWorkView` screen, unified file and folder tree loading, Drag & Drop mechanics, ACC viewing via WebView2, and project-contextual commands.

## 1. Purpose
The **ProjectWorkView** screen is the main workspace where a user manages project folders and files.
- **Main UI responsibilities:**
  - Selecting a project and viewing a unified hierarchical tree of folders (from the filesystem and the DB).
  - Viewing alternatives and versions of identified project files.
  - Opening files, creating folders, renaming alternatives, and dragging and dropping files in and out.
  - Viewing project files through Autodesk Construction Cloud (ACC) in the left WebView2 area.
- **What the window is responsible for:**
  - Orchestrating the TreeView UI and the Viewer.
  - Listening to filesystem changes (`FileSystemWatcher`) and refreshing the local view accordingly.
- **What the window is not responsible for:**
  - It does not handle Workflow management logic.
  - It is not responsible for copying files from emails into the project (`MoveToProject`); this is the role of the `ProjectFileFilingService`.

## 2. Main files and classes
- **View:** `ProjectWorkView.xaml` (Contains the unified TreeView and WebView2 components).
- **Code-behind:** `ProjectWorkView.xaml.cs` (Contains Drag & Drop events and ViewModel binding).
- **ViewModel:** `ProjectWorkViewModel` (Responsible for loading projects, applying filters, listening to the filesystem, and building the tree).
- **Node classes:** 
  - `ProjectFolderNode` (Represents a folder)
  - `ProjectFileNode` (Represents a parent file definition)
  - `AlternativeNode` (Represents a file variant/copy)
  - `VersionNode` (Represents a specific physical version of the file)
- **Helper classes:** `FileHelpers` (CRUD operations on files), `FolderOpener` (opening and managing folders), `CompositeChildrenConverter` (for merging folders and files into a single tree).
- **Services / contexts:** `ActiveProjectContext` (Maintains the active project ID as a Singleton). The WebView2 model relies on `AccViewerUrl` navigation.

## 3. Architecture and UI layout
- **Project selection:** Via a smart ComboBox with search capabilities.
- **Filters:** Filtering by project type, status, and assigned user (mapped against `TypeOfProjectInProjects`).
- **Folder and file tree:** The right-hand TreeView displays a unified structure merging DB data and local FileServer folders.
- **ACC viewer area:** A WebView2 control on the left displays web content linked to ACC (supports multiple tabs).
- **Context Menu actions:** Dynamic commands based on node type (open, rename, delete, add alternative).

## 4. Main data flow and execution
1. **Project selection:** Selecting a project updates the `ActiveProjectContext`, cancels previous filesystem watchers (`StopWatchingAll`), and loads a new tree.
2. **Tree loading and folder scanning:** A recursive process builds folders from both the DB and the local disk (`LoadUnifiedTree`), filtering out unwanted extensions.
3. **File scanning and indexing (Naming Convention):** Powered by the `BaseFileVersion` class which extracts metadata (project, alternative, version) from the physical file name and maps it to a `ProjectFile` from the DB. A file that does not match the naming convention is considered an "unassigned" (external) file.
4. **Drag and drop behavior:** Utilizes `FileDropBehavior` for receiving dragged files. The system waits until the file is ready (`WaitUntilFileReadyAsync`), then associates it as an alternative/version based on the drop target context, or presents a dialog to create a new alternative (`AlternativeNameDialog`).
5. **Opening files and templating:** Double-clicking a version triggers `OpenFile`. Dragging a template creates a copy with the appropriate naming convention.

## 5. Relevant models
*The models are described here as they are currently utilized within the ProjectWork window.*
- `ProjectFolder`: Defines the hierarchical system folders (prior to scanning user-created folders).
- `ProjectFile`: Defines the basic metadata (file type) used to identify files from their naming convention.
- `ProjectAlternative`: Represents a recognized variant of a file (appearing as a parent node to versions). These fields are dynamically created or maintained as explained in the `ProjectFilesPrinciples`.
- `TypeOfProjectInProject`: Connects projects to their types and assigned workers (used for filtering).

> [!WARNING]
> **Important note regarding ProjectFileInstance:**
> In the legacy archive document, `ProjectFileInstance` was described as a DB table acting as a source of truth for every file placement. That model is **no longer active** in its old form (removed in Stage 9E.4). 
> Today, `ProjectFileInstance` is exclusively a **Runtime Projection** built dynamically when viewing the project, as described in `ProjectFilesPrinciples`. It does not exist as a permanent record in the database.

## 6. Integration with adjacent systems
- **Integration with ProjectFiles:** 
  The window reads files and creates alternatives based on the principles outlined in `ProjectFilesPrinciples`. File name construction relies on the naming convention, and resolution occurs in real-time against the FileServer (or ACC) without creating active PFI records in the DB.
- **Integration with ACC:**
  A `WebView2` viewer is active in the interface (supporting tabs). The preview relies on `AccViewerUrl`.
- **Integration with Workflow / Tasks:** 
  The window itself is only used for viewing and editing files; it **does not** transition Workflow statuses or close tasks. (As noted in the architecture, task management is handled in a separate window, such as WorkflowManagementWindow).

## 7. Known limitations / Needs review
- The `FileSystemWatcher` refresh mechanism is primarily designed for local network drives (FileServer). A future review is needed to determine how this refresh mechanism should integrate with ACC or Google Drive without direct filesystem listening (e.g., implementing a "Focused Refresh").
- Loading the folder tree can experience slight delays in heavy projects — performance monitoring of the `Task.Run` logic combined with heavy file creation may be necessary.

## 8. Dropped / cancelled / postponed
- Using the document `Docs/Archive/ProjectWork-Documentation.md` as an active source of truth — **cancelled**. The archived document is retained for historical purposes only; the active documentation is this document.
- Deleting the archive document — **not approved**. It is kept for historical reference.
- Copying the old archive document as-is — **cancelled**. The historical document contained outdated information (such as references to the `ProjectFileInstance` DB table) which was removed according to the `ProjectFilesPrinciples`.
- Changing code as part of documentation updates — **cancelled / not approved**. This documentation accurately reflects the existing codebase.
- Reviving old mechanisms from the archive — **not approved** without an explicit decision.
- Fixing mechanisms within Window 2 — **postponed** until gaps are identified and handled separately during user validation.
