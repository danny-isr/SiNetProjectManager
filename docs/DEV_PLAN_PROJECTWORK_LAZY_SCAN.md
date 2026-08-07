# DEV-013 — ProjectWork lazy scan, unload on collapse, parallel IO

> **Title:** Lazy expand / unload collapse / presence probe / scoped rescan / DOP-4 parallel IO  
> **Date:** 05.08.2026  
> **Status:** On `release` tip @ `127dc0e` (App.Wpf **1.0.23**; feature on tip since earlier ships) -- **Needs Review:** operator/pilot verify (see [`DEV_BACKLOG.md`](./DEV_BACKLOG.md) §2b)
> **Scope:** `SiNet.App.Wpf` ProjectWork tree performance for large projects (~20k+ files)  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md`](./DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md) (DEV-012 colors/filing unchanged)

---

## Locked behavior

1. **Lazy on expand** — scan files and discover one child-folder level only when a folder is expanded.
2. **Unload on collapse** — remove File/Alt/Version nodes; keep folder skeleton + probe flags; re-scan on next expand.
3. **Presence probe** — cheap check for physical files (for gray/purple/green) without building version nodes.
4. **Parallel IO** — `SemaphoreSlim(4)` / DOP-4; enumerate off UI; integrate/unload on UI only.
5. **Watcher (expanded only, structural)** — `FileSystemWatcher` on **FullPath of each Expanded folder only** (`IncludeSubdirectories = false`). NotifyFilter = `FileName | DirectoryName` only; events = Created / Deleted / Renamed (not Changed / LastWrite). Debounce ~800ms with last-affected path → background reconcile of that open folder. Soft poll every ~20s for UNC: **folders + probe only** (no file-node rebuild). No watch of collapsed folders.
6. **Disk reconcile (differential)** — sync immediate disk subfolders (**add** user folders, **remove** missing `IsUserCreated`). On watcher path: **merge** file versions in-place (**add** missing, **drop** FileServer versions whose path disappeared) without `ClearFileNodes`, so file/alternative `IsExpanded` is preserved. Catalog (DB) folders stay in the tree when missing on disk; probe/color updates.

No EF/schema changes. DEV-012 rules unchanged.
