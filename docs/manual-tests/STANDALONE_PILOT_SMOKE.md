# Standalone Pilot — Operator Smoke Checklist

> **Status:** Not Run  
> **Host:** `SiNet.App.Wpf.exe` + AccService Remote (MultiStart)  
> **Strategy:** [`docs/TEST_STRATEGY.md`](../TEST_STRATEGY.md)  
> **Envelope:** [`docs/NEW_SYSTEM_PRODUCTION_READINESS.md`](../NEW_SYSTEM_PRODUCTION_READINESS.md)  
> **Workflow gate:** [`STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./STANDALONE_WORKFLOW_PRODUCTION_GATE.md)  
> **Supersedes:** [`NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md),
> [`EMAIL_ACC_STANDALONE_SMOKE.md`](./EMAIL_ACC_STANDALONE_SMOKE.md)

**Legend:** `Pass` | `Fail` | `Blocked` | `N/A` | **Not Run** (default)

Run offline automation first (CI / local). Then optional Live (`SINET_LIVE_SMOKE=1`). This checklist is **only** what still needs a human.

---

## Session metadata

| Field | Value |
| --- | --- |
| Date | |
| Operator | |
| Environment | |
| Branch / commit | |
| Build config | Release recommended |
| Offline tests | Pass / Fail (attach count) |
| Live tests | Pass / Fail / Skipped |

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
| 5.3 | R02 / R03 smoke when credentials allow | Not Run | R02: expect sheets **Data** + **סיכום פרויקט-תת-חוזה** + **פירוט דיווחים**; compare SUM hours for one project vs filtered Data; pivot filter employee on summary |
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
