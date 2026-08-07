# DEV-012 — ProjectWork: disk-overlay folders, colors, delete empty user folders

> **Title:** Show manual disk folders/files in «בעבודה 2», purple/gray colors, delete only empty user folders  
> **Date:** 05.08.2026  
> **Status:** On `release` tip @ `3bfe152` (App.Wpf line through **1.0.22**) — **Needs Review:** operator/pilot verify (see [`DEV_BACKLOG.md`](./DEV_BACKLOG.md) §2b)
> **Scope:** `SiNet.App.Wpf` ProjectWork tree — merge filesystem directories with DB catalog; no EF schema change  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: ProjectWorkWindow2 domain doc, Archive `IsUserCreated`, DEV-003 recover plan

---

## 1. Product rules (locked)

| Concept | Definition |
| --- | --- |
| Catalog / project folder | Row in `ProjectFolders` (FolderId > 0) |
| User / manual folder | Physical directory under a resolved FileServer path without a matching child catalog title (`IsUserCreated = true`, synthetic negative FolderId) |
| Empty folder | No physical files anywhere in the subtree (empty child folders are not content) |
| Unfiled file | Physical file that does not match a ProjectFile (Type, Number) in that folder — existing Unfiled bucket; display tag «קובץ שאינו שייך לפרויקט» |

### Colors

| Folder kind | State | Color |
| --- | --- | --- |
| Catalog | Non-empty | Existing blue / green / orange |
| Catalog | Empty | Gray |
| User | Non-empty | Purple (`SiTreeUserFolderBrush`) |
| User | Empty | Gray |

### Delete folder (folders only)

Allowed only when both: `IsUserCreated` and empty (recursive). Never delete catalog folders (even empty). No new file-delete UI in this slice.

### Create folder (tree context menu)

Creates a disk directory only (user folder). Does not insert `ProjectFolders`. `IProjectFolderWriteService` remains for catalog/admin but is not used from the ProjectWork tree menu.

---

## 2. Load pipeline

1. DB skeleton via `IProjectFileQueryService.GetProjectFileTreeAsync`
2. Resolve FileServer paths
3. `DiscoverDiskFoldersRecursive` — enumerate subdirs; unmatched titles become user nodes
4. Scan files for catalog targets and user folder paths
5. `IntegrateScannedFile` → filed or Unfiled bucket
6. `RescanAsync` / post create-delete refresh re-runs disk discovery

---

## 3. Acceptance

- Manual non-empty → purple; manual empty → gray + deletable
- Catalog empty → gray, not deletable; catalog non-empty → existing colors
- Nested manual folders at all levels; files inside them shown
- Unfiled tag for unmatched files in catalog or user folders
- Refresh after create/delete updates the full tree
