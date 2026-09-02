# Standalone Pilot — Operator Smoke Checklist

> **Status:** Automated L4W **Pass** (2026-08-24) + **Core Workflow Live Certification 7/7** @ `0803186` (2026-09-02) — **Phase 2: operator interactive smoke Not Run**  
> **Host:** `SiNet.App.Wpf.exe` + AccService Remote (MultiStart)  
> **Strategy:** [`docs/TEST_STRATEGY.md`](../TEST_STRATEGY.md)  
> **Envelope:** [`docs/NEW_SYSTEM_PRODUCTION_READINESS.md`](../NEW_SYSTEM_PRODUCTION_READINESS.md)  
> **Workflow gate:** [`STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./STANDALONE_WORKFLOW_PRODUCTION_GATE.md)  
> **Certification audit:** [`docs/certification/SYSTEM_WORKFLOW_CERTIFICATION_AUDIT.md`](../certification/SYSTEM_WORKFLOW_CERTIFICATION_AUDIT.md)  
> **Pilot controls:** [`docs/PILOT_CONTROLS.md`](../PILOT_CONTROLS.md)  
> **Supersedes:** [`NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md),
> [`EMAIL_ACC_STANDALONE_SMOKE.md`](./EMAIL_ACC_STANDALONE_SMOKE.md)

**Legend:** `Pass` | `Fail` | `Blocked` | `N/A` | **Not Run** (default)

Run offline automation first (local DEV gate via Cursor). Then optional Live (`SINET_LIVE_SMOKE=1`). This checklist is **only** what still needs a human.

**Do not mark this checklist Pass because automated certification is green.** Interactive UI soak is a separate evidence layer.

---

## Session metadata

| Field | Value |
| --- | --- |
| Date | 2026-09-02 (interactive session pending) |
| Operator | Pending — Phase 2 interactive |
| Environment | DEV machine — `danny\SQLEXPRESS` / `SiData`, Gmail `shirly@si-eng.co.il`, ACC Place `SI` |
| Branch / commit | `development` @ `080318610bb06facf92896df41c47d3d70cd2f39` |
| Build config | **Debug** preferred for soak (`[WF-STEP]` + DevTools); Release for menu gating checks |
| Offline tests | **Pass** @ baseline — re-confirm on tip before soak |
| Core Workflow Live Certification | **7/7 Certified** @ `0803186` — PRP Happy (policy boundary), PRP Reject, OPN (policy boundary), PLN, MAT, REV, OUT. **Not** FULL SYSTEM CERTIFIED |
| Interactive smoke | **Not Run** |
| Live tests (L4W P0) | Historical Pass 2026-08-24 — see [`PILOT_CONTROLS.md`](../PILOT_CONTROLS.md) |

### Phase 2 — operator interactive smoke (current)

Automated core workflow certification is **Certified** on `0803186`. Interactive UI/operator soak has **not** started.

**Your turn:** walk §1–§6 below in Debug (or Release for menu gating), then update §6 final decision and [`NEW_SYSTEM_PRODUCTION_READINESS.md`](../NEW_SYSTEM_PRODUCTION_READINESS.md) §9 **Interactive smoke**.

**Prerequisites:** MultiStart AccService + `SiNet.App.Wpf`; seed baseline complete; workflow groups populated; `[SYS-CERT]` / DEV emails only; ACC Place `SI` only.

**After interactive Pass:** continue [`STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./STANDALONE_WORKFLOW_PRODUCTION_GATE.md) Trees as needed; still do **not** claim FULL SYSTEM CERTIFIED while SendQuote/SendOpinion outbound remain policy-blocked.

### P0 Live Smoke (Pilot controls) — 2026-08-17

| Step | Result | Notes |
| --- | --- | --- |
| 1 Environment gate | **Blocked** | [`ENVIRONMENTS.md`](../ENVIRONMENTS.md): dedicated DEV Gmail mailbox **not provisioned**; DEV Gmail/Drive writes treated as production-impacting. Agent stopped before workflow / LiveSmoke / SystemSettings Pilot enablement. |
| 2 Offline Release pre-check | **Pass** | Release build 0 errors; offline suite 3449 Pass |
| 2 LiveSmoke Category | **Not Run** | Would touch vault SQL + AccService + Gmail silent restore against current vault target — not confirmed as isolated replica |
| 3–8 Pilot / PRP / kill-switch | **Not Run** | Requires approved DEV SQL + place=`SI` + isolated Gmail before any `Pilot.Enabled=true` or CreatePriceQuote |
| 9 Gmail/ACC N1–N3 | **Not Run** | Same gate |
| End-state Pilot.Enabled | **N/A** | No replica SystemSettings were modified |
| Decision | **FIX BEFORE P3** (ops/env) | Provision/confirm isolated DEV/replica (SQL + Gmail + ACC place SI), then re-run P0 from step 1 |

**Not changed:** production SystemSettings; no MSIX publish; no P3 user rollout.

### P0 re-attempt via the automated tier — 2026-08-24

The 2026-08-17 Environment Gate block is **resolved**, and steps 3–8 plus the Gmail half of step 9 move to an automated tier instead of a manual walk:

| Blocker (2026-08-17) | Resolution |
| --- | --- |
| No approved DEV SQL target | The DEV machine's database is separate, freely writable and routinely restored from backup (owner confirmation) |
| No isolated Gmail mailbox | Bounded exception: the office shared mailbox is new and not yet in office use — [`ENVIRONMENTS.md`](../ENVIRONMENTS.md) §5.2.1 |
| ACC not isolated | ACC steered to disposable projects behind an in-harness target-name guard — [`TEST_STRATEGY.md`](../TEST_STRATEGY.md) §4W.2 |

Steps now owned by `Category=PilotSmoke` (L4W): 3, 4, 5, 6, 7, 8, 11, and the Gmail portion of 9.
Steps that remain **operator-owned** here: the WPF visual and dismiss steps, ACC recovery, and anything against a production ACC project.

Results for this re-attempt are recorded in [`docs/PILOT_CONTROLS.md`](../PILOT_CONTROLS.md) § "Live evidence (automated tier)" — **latest run 2026-08-24**: SQL/Pilot corridor **Pass** (evidence `p0-pilot-smoke-20260824-154135.md`); Gmail/ACC **Pass** (evidence `p0-pilot-smoke-20260824-154602.md`). Run SQL and Gmail/ACC tiers separately — parallel xUnit can collide on evidence file timestamps. Rollout policy on this page is unchanged.

---

## 0. Automated pre-checks (do not re-test manually)

| Area | Automated pre-check | Operator action |
| --- | --- | --- |
| DI ports / composition | `StandaloneHostCompositionTests` | Skip if green |
| Menu gating (Release features) | `NewShellReleaseMenuGatingTests` | Skip if green |
| Startup order / no legacy dialogs | `StandaloneStartupSequenceTests` | Skip if green |
| Email ACC status text states | `EmailAccSelectionHandlerStatusTests` | Skip if green |
| SQL + schema | `SqlConnectivityLiveTests` (Live) | Skip if Live Pass |
| Vault ACC key hash-only | `VaultLiveTests` (Live) | Skip if Live Pass |
| AccService health/diag | `AccServiceHealthLiveTests` (Live) | Skip if Live Pass |
| Acc mode Remote | `AccModeLiveTests` (Live) | Skip if Live Pass |
| Gmail silent restore | `GmailSilentRestoreLiveTests` (Live) | Skip if Live Pass |

```powershell
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj
# Optional:
$env:SINET_LIVE_SMOKE = "1"
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj --filter "Category=LiveSmoke"
```

---

## 1. Launch (human)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 1.1 | VS MultiStart **New System + AccService** (or AccService then `dotnet run --project src/SiNet.App.Wpf`) | Not Run | |
| 1.2 | Vault / schema / Windows auth gates succeed | Not Run | Live §0 may cover SQL/vault |
| 1.3 | `NewShellWindow` opens; Legacy `MainWindow` does **not** | Not Run | |
| 1.4 | No forced Google login at startup | Not Run | Live Gmail restore covers silent path |

---

## 2. Shell navigation (visual)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 2.1 | Project selector works; Active project display updates | Not Run | |
| 2.2 | **בעבודה 2** opens; browse does not crash | Not Run | Menu gate automated |
| 2.3 | **לוח משימות** shows Quick / Medium / Long | Not Run | |
| 2.4 | **דוחות ביקורת** opens (Release) | Not Run | |
| 2.5 | **צפייה בתהליכים (סגור)** opens read-only | Not Run | |
| 2.6 | Release: no **ביקורת (מעטפת — DEBUG)** / **כלי פיתוח** | Not Run | Source `#if DEBUG` automated |

---

## 3. Email ACC N1–N3 (live ACC + Gmail)

| # | Action | Expected | Status | Notes |
| --- | --- | --- | --- | --- |
| 3.1 | מיילים → message with Gmail attachment | Opens; no “Backend לא מוגדר” | Not Run | Offline: BackendNotAvailable text |
| 3.2 | Upload / passive ingest to ACC Inbox | Files under `_Inbox/THREAD_…/MSG_…/Attachments/` | Not Run | |
| 3.3 | Refresh ACC status strip | Attachments not stuck MissingInAcc | Not Run | |
| 3.4 | Filed message + project tags → Move | Files under project filing rules | Not Run | |
| 3.5 | Jumbo/WeTransfer chip → in-app WebView2 | Not system-browser-only | Not Run | Always manual |
| 3.6 | Complete download in that window | Upload to Inbox; strip shows external download | Not Run | |
| 3.7 | MissingInAcc → select / status sync | AccItemId restored (recovery) | Not Run | |
| 3.8 | **Absent:** Reply / Forward / Send as production actions | Not Run | G-Policy |

---

## 4. ACC status window (operator)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 4.1 | Open **סטטוס ACC** | Not Run | Menu gate automated |
| 4.2 | Mode / health / diagnostics display | Not Run | Live health/diag if run |
| 4.3 | Key diagnostics show hash/prefix only — **no** raw secret | Not Run | Live Vault tests |
| 4.4 | Browse / reconciliation if sample data exists | Not Run | |

---

## 5. MasterPlan Reports (optional for pilot)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 5.1 | Menu **דוחות** R01 / R02 / R03 visible when permitted | Not Run | Menu gate automated |
| 5.2 | R01 generate → Google Sheets opens/writes | Not Run | Always manual |
| 5.3 | R02 / R03 smoke when credentials allow | Not Run | R02: **Data** + **סיכום** (rows: מספר חוזה → שם → תת-חוזה; filters: מספר/שם חוזה + עובד) + **פירוט** (rows: מספר/שם חוזה → תת-חוזה → עובד → תאריך…; filters: מספר/שם חוזה); compare SUM vs Data |
| 5.4 | מנהלה → **מיפוי MasterPlan** opens | Not Run | |

---

## 6. Final decision

| Outcome | When |
| --- | --- |
| **Ready for 1–2 internal ACC-filing pilot users** | Every required section Pass (or N/A) |
| **Needs fix before pilot** | Any required Fail |
| **Blocked** | Missing DB / vault / Gmail / AccService |

```text
Final decision:
  [ ] Ready for 1–2 internal ACC-filing pilot users
  [ ] Needs fix before pilot
  [ ] Blocked by environment/config

Known issues:

Signed: _______________  Date: _______________
```

After **Pass:** update [`NEW_SYSTEM_PRODUCTION_READINESS.md`](../NEW_SYSTEM_PRODUCTION_READINESS.md) §9 Interactive smoke from **Not Run** to **Passed** with operator + commit.
