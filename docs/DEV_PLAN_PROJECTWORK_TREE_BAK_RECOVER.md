# DEV-003 — ProjectWork file tree: .bak exclude, recover UX, folder ignore, expand preserve, collapse-all

> **Title:** ProjectWork («עבודה» / עץ קבצים) — wishlist for development  
> **Date:** 03.08.2026  
> **Updated:** 03.08.2026  
> **Status:** On `release` tip (cited ship **1.0.6**; tip App.Wpf **1.0.23**) -- **Needs Review:** operator verification; ignored-folders (slice F) postponed (see [`DEV_BACKLOG.md`](./DEV_BACKLOG.md) §2b)
> **Scope:** `SiNet.App.Wpf` ProjectWork tree + file scan (`FileServerFileStore` / `FileServerSidecarMetadata`). Operator wishlist from PROD pilot + field scan of `U:\יבנה\(1844)יבנה_מזרח\תכנון`. **Documentation only until DEV implements.**  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md),  
> `SiNetProjectManagerV2/Docs/Domains/ProjectWork/ProjectWorkWindow2-2026-06-19.md`,  
> Archive (legacy rules): `SiNetProjectManagerV2/Docs/Archive/ProjectWork-Documentation.md` §6.4–6.5

---

## 1. Why / product intent

Quiet ProjectWork tree: no `.bak`; recover files only when they still matter (newer than the saved DWG); bulk delete of **stale** recovers with a safety gate; ignored folders out of the tree; refresh must preserve expand; explicit **Collapse all**.

**Guiding key for recover:** compare `LastWriteTime` of the recover file to the primary DWG it is meant to replace. Date wins over recover sequence numbers (`_recover001` vs `_recover`).

---

## 2. Existing mechanism (reuse)

| Piece | Path / type | Notes |
| --- | --- | --- |
| Tree VM | `ProjectWorkTreeViewModel` | `LoadTreeAsync`, `RescanAsync`, watcher hookup |
| Nodes / `IsExpanded` | `ProjectWorkNodes.cs` | TwoWay expand in `ProjectWorkWindowView.xaml` |
| Scan skip (today) | `FileServerSidecarMetadata.ShouldSkipFromScan` | Only sidecars + `~$` — **not** `.bak` / recover |
| Watcher | `FileServerWatcher` | Debounce ~800ms → reconcile Expanded folders only (see DEV-013) |
| Delete file path | Existing ProjectWork delete / file-write commands | Reuse for recover delete; confirm + audit log |
| Legacy V2 | Archive `ExcludedExtensions` / `_recover` bucket | Reference only |

**Expand bug (pilot):** tree sometimes collapses after refresh — preserve expand across `RescanAsync` **and** full `LoadTreeAsync`.

---

## 3. Field scan — how recovers look in the office (1844 / תכנון)

Sample root: `U:\יבנה\(1844)יבנה_מזרח\תכנון` (~5,700 `.dwg`/`.bak`; **103** recover-named files).

### 3.1 Naming (100% of samples)

```text
{PrimaryFileName}_recover.dwg
{PrimaryFileName}_recover000.dwg
{PrimaryFileName}_recover001.dwg
…
```

- Regex (case-insensitive): `_recover(\d{0,3})(?=\.[^.]+$)`
- Strip that suffix → `PrimaryFileName` (same extension).
- Pairing: primary must live in the **same folder**.
- Multiple recovers per primary: common (up to 5). **Representative** for date compare = variant with **max `LastWriteTime`** (not max digit suffix).
- Also saw `_recover*.bak` (few) — disappear with `.bak` exclude.
- Zero-byte recovers exist → treat as irrelevant (hide; eligible for stale-delete only if paired).

### 3.2 Date outcomes in that folder (illustrative)

| Class | Approx. count (families) |
| --- | --- |
| Paired, newest recover **newer** than primary → actionable | ~9 |
| Paired, newest recover **older** than primary → irrelevant | ~50 |
| No primary in folder (orphan) | ~23 |

---

## 4. Target rules — recover (approved product rules)

### 4.1 Detect + pair

1. Detect via regex above (pure helper + unit tests).
2. `PrimaryName =` strip `_recover` / `_recoverNNN` before extension.
3. Resolve primary = file with `PrimaryName` in the **same directory**.
4. Family = all recover variants sharing that primary key in that directory.
5. `BestRecover` = max `LastWriteTime` in the family (ignore 0-byte when choosing “best” for green if a non-empty newer exists; 0-byte never green).

### 4.2 Visibility — hide what is not relevant

| Condition | Tree visibility |
| --- | --- |
| `.bak` (any, including `*_recover*.bak`) | **Never show** (scan exclude) |
| Recover with `Length == 0` | **Never show** |
| Recover paired and `BestRecover.LastWriteTime ≤ Primary.LastWriteTime` | **Never show** (irrelevant — primary already newer/equal) |
| Older recover variants in a family when `BestRecover` is shown | **Never show** (only show `BestRecover` if it is actionable) |
| Recover paired and `BestRecover.LastWriteTime > Primary.LastWriteTime` | **Show** — green + tooltip «recover חדש יותר מה-DWG השמור — לפתוח לשחזור?» |
| Recover **orphan** (no primary in folder) | **Show** — distinct style (e.g. orange) + tooltip «אין קובץ מקור מתאים»; **not** deletable via stale-clean |

Primary DWG (no `recover` in name) is always the main node when present.

### 4.3 Open actionable recover

Opening the green recover uses the existing AutoCAD open path so the drawing is restored from that file (Needs Review: exact host command).

### 4.4 Delete stale recovers (operator action)

- UI: command / button **«מחק recover ישנים»** (toolbar and/or context menu on ProjectWork).
- **Configurable age gap** (setting): recover is eligible for delete only if  
  `Primary.LastWriteTime - Recover.LastWriteTime >= Threshold`  
  (default Needs Review — e.g. 0 = any recover older/equal than primary; or 1 day / 7 days).  
  Store via existing SystemSettings (do not invent a second config store).
- Eligible set = recover files that:
  1. Have a resolved **primary in the same folder**, and  
  2. Meet the threshold vs that primary (typically all hidden stale + optionally older variants), and  
  3. Are not open / locked (standard delete failure handling).
- **Hard rule:** if there is **no matching primary** → **must not** include that recover in delete. Orphans stay on disk and remain visible until a primary appears or ops handle manually.
- Confirm dialog: list count (+ optional sample names); require explicit confirm.
- After delete: `RescanAsync` (preserve expand).
- No silent auto-delete on scan.

---

## 5. Target rules — folders / tree chrome

### 5.1 Ignored folder list

**Postponed (03.08.2026).** Requirement is underspecified: no real folder examples, no decision on name-vs-path / global-vs-per-project / code-vs-settings. Do **not** implement until PROD pastes 2–3 real paths and answers those questions — otherwise we risk hiding important folders.

### 5.2 Preserve expand on refresh

Watcher / reload must restore expanded folder ids/paths; no surprise auto-collapse.

### 5.3 Context menu — Collapse all

«כווץ הכל» — user-initiated only.

---

## 6. Implementation slices (ordered for DEV)

| Step | Work | Done when | Status |
| --- | --- | --- | --- |
| A | Full V2 excluded extensions in scan helper + tests | Listed extensions never in tree | Done |
| B | Recover detect/pair helper + tests (regex from §3.1) | Pure logic covers `_recover` / `_recover000`… | Done |
| C | Hide irrelevant recovers (stale, 0-byte, non-best variants); show only actionable green (+ orphans orange) | Tree matches §4.2 | Done |
| D | Open green recover → AutoCAD path | Manual QA | Done (reuse existing open) |
| E | «מחק recover ישנים» + **threshold default 0** + **block orphans** + confirm | Deletes only paired stale; orphans untouched | Done |
| F | Ignored folders | Listed folders absent | **Postponed** — see §5.1 |
| G | Preserve expand on rescan + full reload | Expand survives watcher noise | Done |
| H | Collapse all context menu | Command works | Done |

**Ship decisions (03.08.2026):** ignore-folders out of scope for this pass; delete threshold default = **0** (any paired recover with `LastWriteTime ≤ primary` is eligible) — constant until product asks for a SystemSettings key.

Version bump `SiNet.App.Wpf` when shipping; publish from PROD after absorb.

---

## 7. Out of Scope

- Auto-delete on every scan without user click
- Deleting orphan recovers from this UI
- Cross-folder pairing
- Changing `ProjectFileNameParser` project naming schema
- Implementing on PROD without DEV cycle

## 8. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Show stale recovers in muted orange | **Superseded** | Product: **hide** irrelevant completely |
| Hide all recovers including newer | Dropped | Newer recover must stay visible (green) |
| Allow delete of orphan recovers | Dropped | No primary = no safe delete |
| Auto-collapse on refresh | Cancelled | Explicit Collapse all only |
| Blind full V2 ExcludedExtensions list | **Done 03.08.2026** | Ported to `ProjectWorkScanExclusions`: `.bak`, `.dwt`, `.dwl`, `.dwl2`, `.ini`, `.$ds`, `.err`, `.tmp`, `.log`, `.exe` |
| Ignored folder list (slice F) | **Postponed 03.08.2026** | No concrete examples / match rules from PROD; defer until clarified |

## 9. Needs Review

- ~~Default **threshold** for “ישן”~~ — shipped default **0** (constant); revisit SystemSettings only if ops ask.
- Ignored-folder list contents (blocks slice F).
- AutoCAD open/restore command for green recover (likely shell-open is enough).
- Theme brushes for green recover + orange orphan.
