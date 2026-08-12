# DEV plan — Safe orphan purge on MasterPlan hours reconcile (DEV-019)

> **Title:** Reconcile orphan purge with safety gates (DEV-019)
> **Date:** 12.08.2026
> **Status:** Planning (implement on `development`; not on PROD until shipped)
> **Scope:** After a **full unfiltered** hours reconcile (`FromDate=null`), optionally **delete** replica rows (`MP_ProjectHours`, `MP_ProjectHoursExtended`; optionally `MP_TimeHourReports`) whose IDs were **not** returned by the API — under hard safety gates. Today orphans are only counted and logged (`CountOrphanCandidatesAsync` — never deleted).

Related: [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md), [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) (DEV-018), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md).

**Operator intent (2026-08-12):** During reconcile the API full pull is the source of truth for this data-flow stage. Rows absent from that pull are treated as deleted at source and should be removable from replica — **with** protection so a partial/bad API response cannot wipe the table.

**PROD evidence:** 2026-08-12 forced reconcile inserted 47 Extended rows; reported **38** Extended orphans (sample includes long-standing IDs `20727`, `54818–54824`, …). Engine left them in place.

---

## 1. Purpose

Close the “API ↔ replica must match” rule for hours entities after a successful full reconcile, without repeating the historical failure mode where a **filtered / incomplete** API response looked like mass deletion.

---

## 2. Existing mechanism

| Piece | Behaviour today |
| --- | --- |
| `--daily --reconcile` / weekly auto-reconcile | Full pull of hour entities; upsert by ID; **no DELETE** |
| `CountOrphanCandidatesAsync` | Replica IDs ∉ API ID set → Warning log + `OrphanCandidates` count |
| Docs | [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md) §3.2 item 5; §6 “Deleting replica rows… Not implemented” |

Do **not** delete orphans on the **lookback / watermarked** daily path — only after a pass that used **null FromDate** (true reconcile).

---

## 3. Target behaviour

### 3.1 When purge may run

All of the following must be true:

1. This run is a **reconcile** for that entity (`ForceReconcile` or due interval).
2. The hours request used **no date filter** (`FromDate` null).
3. API call succeeded (HTTP 2xx, deserialize OK, non-null payload).
4. Fetched count is **plausible** (see gates below).
5. Config enables purge (`MasterPlanApi:OrphanPurge:Enabled` default **false** until first ship + PROD verify; then default true on weekly reconcile — Needs Review).

CLI:

- `--purge-orphans` — allow purge this run if gates pass.
- `--purge-orphans-dry-run` — compute + log + write CSV of IDs that **would** delete; no DELETE.
- Without either flag: keep today’s report-only behaviour (even if config Enabled — Needs Review: prefer config OR flag; recommend **both** config on + dry-run default for first week).

### 3.2 Safety gates (locked proposals)

| # | Gate | Default | Why |
| --- | --- | --- | --- |
| G1 | **Full-pull only** | Required | Never purge after lookback sync |
| G2 | **Min API rows** | `Fetched >= max(1000, 0.5 × ReplicaRowCount)` | Guards truncated / empty / near-empty responses |
| G3 | **Max purge fraction** | `PurgeCount / ReplicaRowCount <= 0.10` (10%) | Operator ask: never open more than ~10% of the table |
| G4 | **Max absolute purge** | e.g. `500` per entity per run (configurable) | Caps damage even on small tables |
| G5 | **Age / recency window** | Only purge orphans whose `ReportDate` (or `ReportDateTime`) is within last **N months** (default **24**) **OR** whose `SyncedAt`/`LastUpdated` is within last **N months** — **Needs Review which column**. Very old ETL-stamped rows outside the window stay until monthly replace or explicit `--purge-orphans-include-legacy` | Avoid deleting ancient baseline rows if API historically omits deep history |
| G6 | **Repeat sighting** | Orphan ID must appear in **≥ 2 consecutive** successful full reconciles (store candidate set in `Sync_State` JSON sidecar or small `Sync_OrphanCandidates` table — prefer **no new table**: write `%ProgramData%\SiOffice\MasterPlanSync\orphan-candidates\{Entity}.json`) | One bad pull cannot delete |
| G7 | **Fail closed** | If any gate fails: **zero deletes**, log `[ORPHAN-PURGE] BLOCKED reason=… count=…`, keep report-only | Partial wipe forbidden |
| G8 | **Transaction / batch** | DELETE by ID list in batches (e.g. 200); log every ID deleted; abort remaining batches if SQL errors | Auditable |
| G9 | **Pre-delete artifact** | Always write `orphan-purge-{entity}-{timestamp}.csv` (ID, ReportDate, ProjectID, EmployeeID, LastUpdated) **before** DELETE | Recovery / analysis |
| G10 | **Entity scope** | v1: `ProjectHours` + `ProjectHoursExtended` only; `TimeHourReports` only if its full pull is confirmed complete | THR endpoint historically ignores FromDate but is a different dataset |

**Recommended first ship defaults:** Enabled=false; dry-run on weekly reconcile writes CSV; ops enables with `--purge-orphans` after reviewing two consecutive dry-runs.

### 3.3 What “match” means after purge

- Upsert from API (existing).
- Delete replica-only IDs that passed all gates.
- Still log any orphans that **failed** age/repeat gates as `OrphanDeferred` (not deleted).

---

## 4. Implementation sketch (develop)

1. Extend `HoursSyncOptions` / config section `MasterPlanApi:OrphanPurge` with the knobs above.
2. After upsert + orphan count in reconcile path, call `PurgeOrphanCandidatesAsync` (or dry-run).
3. Persist last orphan ID set for G6.
4. Unit tests: gate math (10%, min fetch, age, repeat, fail-closed); never called when FromDate set.
5. Update [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md) §3.2 item 5 and §6 row “Deleting replica rows…”.
6. No EF migrations to SiData; Replica-only optional file sidecar preferred over new SQL table unless develop chooses a tiny `Sync_OrphanSightings` table (Needs Review).

---

## 5. Complexity / risk

| Topic | Assessment |
| --- | --- |
| Complexity | Medium — gates + persistence of sightings |
| Effort | 1–1.5 days SyncEngine + tests + docs |
| Breaking | Off by default → none; when enabled, R02 row counts can drop for true deletes |
| Residual risk | Vendor API omits deep history → G5/G6 mitigate; G2/G3 stop mass wipe |

---

## 6. Out of Scope

- Deleting orphans outside full reconcile.
- Auto-purge on monthly `--monthly` (that path already DROP/reloads `MP_*`).
- Changing lookback / MERGE LastUpdated skip / Hours unit (separate IDs).
- Soft-delete / recycle bin table for purged rows (CSV pre-delete is enough for v1).
- WPF UI for purge (CLI/config only in v1).

---

## 7. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Unconditional delete of all orphans after one reconcile | Dropped | Historical watermark false-negatives; operator asked for safety caps |
| Delete only “very old” rows | Clarified | Operator wants **not** to auto-delete **very old** without extra care — use G5 deferral + optional legacy flag |
| UI confirmation dialog | Postponed | SyncEngine is Task Scheduler / CLI |

---

## 8. Needs Review

1. Exact defaults after first successful PROD dry-run (enable auto-purge on weekly?).
2. Age column: `ReportDate` vs `LastUpdated` vs `SyncedAt`.
3. Whether `TimeHourReports` is in v1.
4. Sightings store: JSON file vs small Replica table.

---

## 9. Acceptance

- Without `--purge-orphans` / with Enabled=false: behaviour unchanged (report only).
- Dry-run lists the same IDs as today’s orphan samples would, writes CSV, deletes nothing.
- If API returns 0 or tiny set: G2 blocks; zero deletes.
- If orphans > 10% of table: G3 blocks; zero deletes.
- After two consecutive full reconciles with Enabled+flag: orphans inside age window and under caps are deleted; deferred orphans remain logged.
- Build: `MasterPlan.SyncEngine` (+ existing App.Wpf gate if touching shared docs only — SyncEngine tests if present).
- Docs updated. **No SiData EF migration.**

---

## 10. Copy-paste prompt for the `development` agent

```
Implement DEV-019 from docs/DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md (read fully).

Goal: After a FULL unfiltered hours reconcile only, optionally delete replica orphan IDs (in API pull but not returned) under safety gates. Default remains report-only until explicitly enabled.

Gates (must all pass or delete nothing):
- Full-pull reconcile only (never on lookback daily)
- Min fetch vs replica size
- Max purge ≤ 10% of replica rows + absolute max cap
- Do not auto-purge very old rows without --purge-orphans-include-legacy (age window)
- Orphan must be seen on ≥2 consecutive successful full reconciles
- Pre-delete CSV of every ID; fail closed; batch DELETE
- v1: ProjectHours + ProjectHoursExtended

CLI: --purge-orphans and --purge-orphans-dry-run. Config MasterPlanApi:OrphanPurge:*.
Update MASTERPLAN_SYNC_WATERMARKS.md (today says orphans never deleted).
No EF/SiData migrations. Do not implement DEV-018 or hours-unit fixes here.
Tests for each gate. Build SyncEngine.
```
