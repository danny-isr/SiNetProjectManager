# DEV-006 / DEV-007 — Configurable ProjectWork scan exclusions, and open-by-extension

> **Title:** ProjectWork file noise filter (editable) + open-with by extension  
> **Date:** 04.08.2026  
> **Status:** DEV-006 Implemented · DEV-007 Planning (approved direction)  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md`](./DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md)

---

## 1. Product intent

1. **DEV-006.** Operators must hide lock/noise files from the ProjectWork tree **and** be able to add/remove rules without a code change (System Settings).
2. **DEV-007.** Operators choose, **by file extension**, how to open a file (ACC viewer / Drive download+open / Windows Shell local app). Local opens use Windows Shell (`UseShellExecute`) for now — no custom exe picker in this phase.

---

## 2. Existing mechanisms (reuse)

| Piece | Role |
| --- | --- |
| `ProjectWorkScanExclusions` + `IProjectWorkScanExclusionPolicy` | Parse/match + settings-backed cache (DEV-006) |
| `FileServerSidecarMetadata.IsOfficeOwnerLockFile` / `~$` | Office locks — already skipped, not editable |
| `FileServerSidecarMetadata.IsMetadataCompanion` | `*.si.json` / companion json — **always** skipped (not user-editable) |
| `SystemSettings` + Settings UI | Pattern: `AccManualUploadAllowedExtensions` CSV field |
| `IFileOpenService.SetOpenPreference*` | Stub Phase 2 — wire for DEV-007 |

---

## 3. DEV-006 — target behavior

### 3.1 Rule format (single CSV setting)

Key: `ProjectWork.ScanExclusionRules`  
Default:

```text
.bak,.dwt,.dwl,.dwl2,.ini,.$ds,.err,.tmp,.log,.exe,~$
```

Parsing:

| Token | Meaning |
| --- | --- |
| Starts with `.` (e.g. `.bak`, `.$ds`) | Exclude by **extension** |
| Anything else (e.g. `~$`) | Exclude by **file-name prefix** |

Sidecar companions (`*.si.json` / `{data}.json` beside sibling) remain **hard-coded** skips — not in this list.

### 3.2 Runtime

- `IProjectWorkScanExclusionPolicy` loads/caches parsed rules from SystemSettings (fallback = default CSV).
- FileServer + Drive (and stale-recover sweep) call the policy after the sidecar check.
- Saving System Settings refreshes the policy cache so the next scan/rescan sees the new list (no app restart required if refresh is hooked on save; otherwise document restart).

### 3.3 Settings UI

New admin tab or field: «סיומות / קידומות להסתרה בעץ עבודה» — one text box, same style as ACC allowed extensions. Tooltip explains `.ext` vs `~$`.

### 3.4 Acceptance

- [x] Default list matches today’s behavior (V2 extensions + `~$` + sidecars).
- [x] Admin can add `.xyz` and remove `.log`; rescan hides/shows accordingly (Settings field + policy cache refresh on save).
- [x] Removing `~$` makes Office lock files visible again (intentional) — no longer hard-coded in scan skip.
- [x] Sidecar `.si.json` still never appears even if removed from the CSV.
- [x] Automated parse/match tests + settings default alignment.

---

## 4. DEV-007 — target behavior (approved direction, implement after 006 ships)

1. Preference table/map: **extension → open mode** (`Acc` | `Drive` | `LocalShell`).
2. Default = today’s behavior by **storage destination** when no preference is set.
3. LocalShell = `Process.Start(..., UseShellExecute = true)` (Windows file association).
4. UI: later — settings grid or context «פתח עם…»; storage of preferences via extending the existing `SetOpenPreference*` seam / SystemSettings CSV — exact storage chosen at implementation time (prefer SystemSettings for global-by-extension to avoid schema change).

Out of scope for 007 v1: per-file overrides, picking a specific `.exe` path, macOS.

---

## 5. Out of Scope / Postponed

| Item | Status |
| --- | --- |
| Making `*.si.json` user-removable from exclude | Rejected — breaks sidecar model |
| Ignored **folders** (DEV-003 F) | Still postponed |
| Custom application path picker | Postponed |

## 6. Needs Review (007 only)

- Exact Settings UI for extension→mode map.
- Whether ACC/Drive modes apply only when the file’s storage matches, or attempt cross-open.
