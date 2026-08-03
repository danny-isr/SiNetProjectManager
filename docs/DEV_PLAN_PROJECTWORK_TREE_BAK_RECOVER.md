# DEV-003 — ProjectWork file tree: .bak exclude, recover UX, folder ignore, expand preserve, collapse-all

> **Title:** ProjectWork («עבודה» / עץ קבצים) — wishlist for development  
> **Date:** 03.08.2026  
> **Status:** Planning (implementation on `development`)  
> **Scope:** `SiNet.App.Wpf` ProjectWork tree + file scan (`FileServerFileStore` / `FileServerSidecarMetadata`). Operator wishlist from PROD pilot. **Documentation only in this round — no code.**  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md),  
> `SiNetProjectManagerV2/Docs/Domains/ProjectWork/ProjectWorkWindow2-2026-06-19.md`,  
> Archive (legacy rules): `SiNetProjectManagerV2/Docs/Archive/ProjectWork-Documentation.md` §6.4–6.5

---

## 1. Why / product intent

Operators working in ProjectWork need a quieter tree: no `.bak` noise; clear treatment of AutoCAD **recover** DWGs vs the saved DWG; ignored folders stay out of the tree; refresh must **not** collapse what the user expanded; and an explicit **Collapse all** action (instead of surprising auto-collapse).

---

## 2. Existing mechanism (reuse)

| Piece | Path / type | Notes |
| --- | --- | --- |
| Tree VM | `ProjectWorkTreeViewModel` | `LoadTreeAsync`, `RescanAsync`, watcher hookup |
| Nodes / `IsExpanded` | `ProjectWorkNodes.cs` | TwoWay expand in `ProjectWorkWindowView.xaml` |
| Scan skip (today) | `FileServerSidecarMetadata.ShouldSkipFromScan` | Only `*.si.json` / companion `.json` + Office `~$` locks — **not** `.bak` / recover |
| Watcher | `FileServerWatcher` | Debounce ~800ms → `RescanAsync` |
| `RescanAsync` | Tree VM | Documented to clear **file** nodes only and keep folder skeleton + expand |
| Extension conflict UI | `HasExtensionConflict` / `ProjectFileExtensionConflict` | Different concern (same base name, different ext) — do not overload blindly |
| Legacy V2 rules (archive) | `ExcludedExtensions` included `.bak`, `.dwl`, …; `_recover` → external bucket | Reference only — re-implement in New System scan/UI deliberately |

**Observed gap (pilot):** after expanding folders, something later collapses the tree. `RescanAsync` claims to preserve expand; suspect full `LoadTreeAsync` / `forceReload` / project-context reload rebuilds roots (`IsExpanded = true` only on roots). DEV must verify which path fires and fix **preserve expanded paths** (and selection if cheap).

---

## 3. Target rules — files

### 3.1 Exclude `.bak`

- Do **not** show `*.bak` in the ProjectWork tree (scan skip, same layer as `ShouldSkipFromScan` or adjacent filter).
- Prefer extending the central skip helper — one place for FileServer list + any other scanners.

### 3.2 Prefer DWG **without** `recover` in the name

When both exist (same logical drawing family):

| File | Role |
| --- | --- |
| Name **without** `recover` (case-insensitive substring in file name) | **Primary** — normal DWG the operator works with |
| Name **with** `recover` | Secondary / recovery candidate — still visible under rules below |

**Priority:** the non-recover file wins as the “main” node / version the UI emphasizes. Do not hide recover entirely.

Exact matching (Needs Review): AutoCAD patterns such as `drawing_recover.dwg`, `Recover.dwg`, autosave names — implement with a small pure helper + tests (`Contains("recover", OrdinalIgnoreCase)` as starting rule unless product refines).

### 3.3 Recover coloring + tooltip

| Condition | UI |
| --- | --- |
| Recover file present | Show in **orange** (recover category) |
| Recover **LastWriteTime ≤** paired primary DWG | Less relevant — keep orange (or muted orange); optional tooltip: older than saved DWG |
| Recover **LastWriteTime >** paired primary DWG | **Green** (or distinct “newer recover” brush) + tooltip: Hebrew text along the lines of «קובץ recover חדש יותר מה-DWG השמור — האם לנסות לשחזר לפיו?» |

**Open action:** opening / activating a newer-recover node should drive the existing AutoCAD open/restore path so AutoCAD recovers from that file (reuse current “open DWG” host — do not invent a second CAD launcher). Exact API hook: Needs Review against existing ProjectWork open commands.

### 3.4 Pairing recover ↔ primary DWG

Needs a deterministic pairing rule (strip `_recover` / ` recover` / suffix patterns). Document the chosen rule in code comments + unit tests. If unpaired recover: still show orange; green rule only when a primary peer exists for comparison.

---

## 4. Target rules — folders

### 4.1 Ignored folder list

- Maintain a list of folder names/paths that are **never shown** in the ProjectWork tree.
- Source of truth: Needs Review — SystemSettings key vs project setting vs hardcoded defaults from V2. Prefer existing settings mechanism if one already exists; do not invent a second store.
- Apply at scan/tree-build time so watcher events under ignored folders do not resurface nodes.

### 4.2 Expand / refresh preserve

- Any automatic refresh (watcher `RescanAsync`, timed reload, project context tick) must **preserve**:
  - which folders were expanded (by stable folder id / relative path),
  - ideally selected node.
- Full tree rebuild (`LoadTreeAsync`) must restore expand state after rebuild, not only in-place rescan.
- Do **not** auto-collapse everything to a minimal tree as a side effect of refresh.

### 4.3 Context menu — Collapse all

- Right-click on tree (folder or root chrome): **«כווץ הכל» / Collapse all** — sets `IsExpanded = false` on all folder nodes (or all except configured roots — product choice).
- This replaces surprising auto-collapse: user opts in when they want a minimal tree.
- Optional later: «הרחב הכל» — out of scope unless cheap.

---

## 5. Implementation slices (ordered for DEV)

Work top-down; tick / remove from [`DEV_BACKLOG.md`](./DEV_BACKLOG.md) as done.

| Step | Work | Done when |
| --- | --- | --- |
| A | `.bak` skip in scan helper + tests | `.bak` never appears in tree |
| B | Recover detection helper + primary vs recover priority in tree grouping | Non-recover preferred; recover still listed |
| C | Orange / green brushes + tooltips by date vs primary | Visual rules match §3.3 |
| D | Open newer-recover → AutoCAD restore path | Manual QA with sample pair |
| E | Ignored folders list wired into tree build | Listed folders absent |
| F | Preserve expand across `RescanAsync` **and** full `LoadTreeAsync` | Expand survives refresh for ≥1–2 minutes under watcher noise |
| G | Context menu Collapse all | Command works; no unwanted auto-collapse |

Version: bump `SiNet.App.Wpf` when shipping; publish from PROD per release process after absorb.

---

## 6. Out of Scope

- Deleting `.bak` / recover files from disk automatically
- Changing DWG naming convention / `ProjectFileNameParser` schema beyond recover display
- File Catalog Admin screens
- Implementing on PROD/`release` without DEV cycle
- Full V2 parity of every archived `ExcludedExtensions` entry in one shot (start with `.bak`; add `.dwl`/`.tmp` only if product confirms)

## 7. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Hide all recover files completely | Dropped | Operator still needs newer-recover signal |
| Auto-collapse tree on every refresh | Cancelled as desired behavior | Replace with explicit Collapse all |
| Port entire V2 ExcludedExtensions list blindly | Postponed | Start with `.bak` + documented recover UX |

## 8. Needs Review

- Exact recover filename patterns used in the office (samples).
- Ignored-folder list location and initial contents (operator to paste list).
- Which open-DWG command AutoCAD should use for recover restore.
- Whether green/orange must follow existing theme resources in `ProjectWorkWindowView.xaml`.
