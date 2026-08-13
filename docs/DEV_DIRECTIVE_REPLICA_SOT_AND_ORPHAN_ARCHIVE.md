# DEV-025 — הנחיית מדיניות: Replica = SoT לשאילתות + מחיקת orphans עם ארכיון JSON

> **Title:** Operator lock — Replica-first queries; API-aligned orphan delete with 30-day JSON archive  
> **Date:** 13.08.2026  
> **Status:** Planning / Directive (documentation only — implement on `development` after push)  
> **Scope:** Two locked product rules for MasterPlan hours / reports. Supersedes the **protective multi-gate** stance of DEV-019 for the *desired end state*. Does not implement code in this round.  
> **Branch:** Write/merge on `development`; ship via normal `release` process later.

Related: [`DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md`](./DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md) (DEV-019 — **superseded intent**), [`DEV_DIAG_R02_GAP_AFTER_RECONCILE.md`](./DEV_DIAG_R02_GAP_AFTER_RECONCILE.md) (DEV-024), [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) (DEV-020 staging folder), [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md).

---

## 1. Purpose

Lock two operator decisions so DEV can refactor without re-debating:

1. **After a successful full reconcile**, `Replica_DB` hours tables must **match the external Master Plan API payload** (upsert + delete orphans). Soft “keep orphans behind gates” is **not** the target product behaviour.
2. **All report / query paths** that today prefer live `Db_Mp_SiEng` (e.g. R02 merge) must **prefer Replica** — across the board. Live MasterPlan DB remains for restore/ETL/admin, not as the default read SoT for product reports.

---

## 2. Existing mechanisms (reuse — do not invent parallel stacks)

| Mechanism | Location / role |
| --- | --- |
| Full reconcile | SyncEngine `--daily --reconcile` / DEV-023 post-monthly force reconcile |
| Orphan detection | `CountOrphanCandidatesAsync` — IDs on replica ∉ API ID set |
| DEV-019 purge plan | Gates G1–G10, CSV pre-delete, dry-run — **too defensive for locked intent**; keep useful pieces (full-pull only, pre-delete artifact, logging) |
| Bak staging folder (DEV-020) | Client: `N:\MasterPlanBakup` · SQL view: `D:\SharedFolder\ProjectsData\MasterPlanBakup` |
| R02 data source | `SqlR02ReportDataSource` — currently **MasterPlan-first** when `EndDate <= mpMax` (root cause of DEV-024) |
| Live MP DB | `Db_Mp_SiEng` — monthly restore target; **not** default report SoT under this directive |

---

## 3. Rule A — Orphan delete = API alignment + 30-day JSON archive

### 3.1 Source of truth

| Layer | Role |
| --- | --- |
| **External Master Plan API** (full unfiltered pull) | **Authoritative** for which hour IDs exist after reconcile |
| **`Replica_DB` `MP_ProjectHours` / `MP_ProjectHoursExtended`** | Must end the run **aligned** with that pull (insert/update + **delete** missing IDs) |
| Live `Db_Mp_SiEng` | Unchanged by orphan purge; still used for monthly bak restore / ETL |
| Disk JSON archive | **Recovery aid only** — not a second SoT for the app |

### 3.2 When delete runs

- Only after a **successful full reconcile** for that entity (`FromDate` null, HTTP OK, deserialize OK).
- **Not** on watermarked / lookback daily sync.
- Applies at least to `ProjectHours` + `ProjectHoursExtended` (same as DEV-019 scope). `TimeHourReports` — Needs Review if full pull is equally complete.

### 3.3 What to drop from DEV-019 (no longer required for target)

| Gate / behaviour | Status under DEV-025 |
| --- | --- |
| Max 10% purge fraction (G3) | **Dropped** as a hard block — API full pull is trusted after Success |
| Repeat sighting ≥2 (G6) | **Dropped** as a hard block — one successful full reconcile is enough to delete |
| Age window deferral (G5) leaving orphans in replica | **Dropped** as default — orphans are deleted; archive preserves them |
| `Enabled=false` forever / dry-run-only product | **Dropped** as end state — purge is part of successful reconcile |
| Min-fetch plausibility (G2) | **Keep as fail-closed** — empty/truncated API must **not** delete (protect against bad pull) |
| Full-pull only (G1) | **Keep** |
| Fail closed on API failure (G7) | **Keep** |
| Pre-delete artifact (G9) | **Replace/extend** with JSON archive below (CSV optional extra) |

### 3.4 JSON archive (required)

| Item | Spec |
| --- | --- |
| Folder | **Same staging root as DEV-020** — client `N:\MasterPlanBakup` (configurable; SQL twin path already documented). Subfolder recommended: `OrphanArchive\` under that root |
| Format | JSON (one file per purge event **or** rolling daily file — implementer’s choice; must be easy to find by date) |
| Retention | **30 days** — delete/archive-rotate files older than 30 days on each successful purge write |
| Content (minimum per deleted row) | `Entity`, `ID`, `DeletedAtUtc` (day of purge), plus useful restore fields: `ReportDate`, `EmployeeID`, `EmployeeName`, `ProjectID`, `ProjectNumber`, `Duration`, `TotalHours`, `LastUpdated`, and other non-secret columns available on the row |
| Order of operations | **1)** Write JSON (flush to disk) **2)** then DELETE from replica **3)** log counts |
| Restore | Out of band / future small tool or documented SQL re-insert from JSON — **not** required in first ship; archive must be sufficient for manual restore |

### 3.5 End-state after reconcile

```text
API full pull Success
  → UPSERT replica from API
  → WRITE orphan rows to JSON archive (30-day retention in MasterPlanBakup)
  → DELETE those IDs from replica
  → Replica ID set ⊆ API ID set (aligned for that entity)
```

No silent “orphan left in table because first sighting / age”.

---

## 4. Rule B — Queries / reports: Replica-first (blanket)

### 4.1 Policy

| Consumer | Read from |
| --- | --- |
| R02 (and R03 hours-related if same pattern) | **`Replica_DB`** (`MP_ProjectHoursExtended` preferred) |
| Any future MasterPlan hours report in App.Wpf | **Replica** |
| Live `Db_Mp_SiEng.HoursReports` | **Not** default for product reports |

Live MasterPlan may still be used for:

- Monthly restore / ETL **writers**
- Explicit admin/diagnostics tools that label the source as live MP
- Connection health probes

### 4.2 Required code change (implementation round — not this doc round)

| File / area | Change |
| --- | --- |
| `SqlR02ReportDataSource.GetMergedHoursAsync` | Stop MasterPlan-first split on `mpMax`. Default: **Replica only** for the requested range |
| Legacy GoogleConnector R02 merger (if still reachable) | Same policy or leave dead path documented |
| Docs / comments claiming “prefer MasterPlan up to max date” | Update to Replica-first |

Optional later (Needs Review): feature flag `Reports:PreferLiveMasterPlan=false` default for emergency only — **not** required if operator wants blanket Replica.

### 4.3 Why (PROD evidence 13.08.2026)

July R02 stayed **271** rows while Replica had **371** and live `HoursReports` had **273** (271 non-zero). R02 followed live MP because of `EndDate <= mpMax` — reconcile could not fix the report. See DEV-024 §12.

---

## 5. Implementation order (for DEV on `development`)

1. Docs already locked here — absorb pointers into DEV-019 / watermarks / R02 diag.  
2. **Replica-first R02** (small, high impact) + tests for source selection.  
3. **Orphan purge**: strip hard G3/G5/G6 blocks; keep G1/G2/G7; add JSON archive under MasterPlanBakup; wire into successful full reconcile (incl. DEV-023 path).  
4. Version bump SyncEngine (+ App.Wpf if only reports change).  
5. PROD verify: after reconcile, orphan count on replica = 0 for entity; JSON file present; R02 July row count tracks Replica (not live 271).

---

## 6. Risk & complexity (for approval before code)

| Item | Notes |
| --- | --- |
| Complexity | Medium — SyncEngine purge + file IO; R02 source change is localized |
| Risk | Bad API full pull could delete many rows — **mitigated by G2 min-fetch fail-closed + JSON archive** |
| Breaking | R02 numbers will change vs today’s live-MP-based sheets (expected / desired) |
| DB schema | None |
| Out of band restore | Manual from JSON until a restore command exists |

---

## 7. Out of Scope

- Implementing code in the documentation round.  
- Deleting from live `Db_Mp_SiEng`.  
- Changing monthly bak retention (still DEV-020 / 10 baks).  
- Making Google Sheets themselves the SoT.  
- Auto UI “restore orphan” in first ship.

## 8. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| DEV-019 hard 10% / 2-sighting / age-defer as product default | **Superseded** by DEV-025 | Operator lock: API alignment + JSON safety net |
| MasterPlan-first R02 merge | **Superseded** | Operator lock: Replica-first blanket |
| Interactive restore tool | Postponed | Archive first; restore UX later |

## 9. Needs Review

- Exact JSON file naming (`orphan-purge-{entity}-{yyyyMMdd}.json` vs append-only).  
- Whether THR orphans purge in the same pass.  
- Emergency flag to read live MP for reports (default off).  
- Min-fetch formula for G2 (keep `max(1000, 0.5 × replica)` unless ops pick another).

---

## 10. Copy-paste brief for DEV agent

```text
Implement DEV-025 on development (docs: docs/DEV_DIAG not needed for impl — use DEV_PLAN_MASTERPLAN_ORPHAN_PURGE + this file).

1) Reports: SqlR02ReportDataSource (and any sibling) — Replica-first; do not prefer live HoursReports via mpMax split.
2) SyncEngine: after successful full hours reconcile, DELETE replica orphans; before DELETE write JSON archive under MasterPlanBakup (same DEV-020 root), retain 30 days; keep fail-closed if API fetch looks truncated; remove hard 10%/2-sighting/age-defer as blockers.
3) Tests + SyncEngine version bump; no EF migrations; no release edit until ship process.
```
