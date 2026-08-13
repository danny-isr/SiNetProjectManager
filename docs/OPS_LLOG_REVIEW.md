# Agent Llog review — incremental UNC sweep

> **Title:** Cursor/agent incremental review of the central Serilog share  
> **Date:** 13.08.2026  
> **Updated:** 13.08.2026  
> **Status:** Active  
> **Scope:** How Cursor agents review `\\si-win-2k19\AutoCAD Data\log` without missing old unread files and without re-scanning bytes already processed. **Not** a feature in `SiNet.App.Wpf`. No application code in this round.

Related: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md), [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md) (what must be Warning+ on the share), [`DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md`](./DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md), [`.cursor/skills/llog-review/SKILL.md`](../.cursor/skills/llog-review/SKILL.md).

---

## 1. Existing mechanism

Live tails and folder layout: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) §2–3. Central Client files exist only at **Warning+**, so a healthy session may create **no** file.

There is **no** in-app log viewer. This process is for the **agent on the ops workstation**, not for end users.

If a user-visible ACC upload / MoveToProject / Gmail File fails and **no** Warning+ line appears in the delta, that is an application logging gap — [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md) — not a miss by this sweep.

---

## 2. Target (what we built)

| Piece | Role |
| --- | --- |
| Skill | `.cursor/skills/llog-review/SKILL.md` — run on “check Llog / סרוק לוגים” |
| Script | `.cursor/skills/llog-review/scripts/Invoke-LlogDelta.ps1` — inventory + extract **new bytes only** |
| Byte cursor | `artifacts/llog-review/state.json` (**gitignored**) — per-file `bytesConsumed` |
| Findings ledger | `artifacts/llog-review/ledger.json` (**gitignored**) — fingerprints so the same 401 is not re-diagnosed |
| Pending inbox | `artifacts/llog-review/pending.json` (**gitignored**) — new fingerprints kept across empty incremental runs until they are appended to the ledger |
| Seed | `.cursor/skills/llog-review/ledger.seed.json` — incidents already diagnosed 13.08.2026 (Lilach 401, Sarita Gmail/R02, Danny token lock, SyncEngine 429) |
| Last delta | `artifacts/llog-review/last-run.md` — what the agent reads after each run |
| Rule | `.cursor/rules/llog-review.mdc` — do not ad-hoc recurse the share |

**Unread = not in `state.json`.** That is how Lilach’s June file would have been picked up even when the question was “users working today”.

**Already processed = `bytesConsumed`.** Daily roll files are new paths (`Client-yyyyMMdd.log`); growth within a day is a delta.

---

## 3. How to run

```powershell
pwsh -File .cursor/skills/llog-review/scripts/Invoke-LlogDelta.ps1
```

Then read `artifacts/llog-review/last-run.md`. Diagnose **New fingerprints** only. Append each diagnosis to `ledger.json` (`status`, `summaryHe`, `samplePath`). The next run drops those fingerprints from `pending.json`.

A later run with **New=0 Grown=0** still lists pending items — do not treat an empty byte-delta as “inbox cleared”.

Periodic: ask the agent to “סרוק Llog” (or `/loop 1d` with that prompt) — same skill, incremental.

---

## 4. Out of Scope

- Changing Serilog levels, sinks, or `SiNet.App.Wpf`
- In-app log viewer
- Indexing `CrashReports` in the default sweep
- Committing `artifacts/llog-review/` (local ops state)

---

## 5. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Re-read every historical Client log on each chat | **Dropped** | Does not scale; state.json is the cursor |
| Today-only file filter | **Dropped** as the default | That filter missed Lilach |
| Seq / HTTP telemetry | **Out of scope** | Same as PRODUCTION_MONITORING §8 |

---

## 6. Needs Review

1. Whether `development` should absorb this skill/docs from `release` in the usual merge.
2. Optional `/loop 1d` on the PROD Cursor session — operator must start it; not armed by this round.
