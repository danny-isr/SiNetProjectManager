# New System — Stage 2 P0 smoke checklist (2026-07-27)

> **Status:** Not Run  
> **Scope:** Interactive smoke for V2 **New System** release menu surfaces and startup prerequisites.  
> **Supersedes:** Inline §9.3.1 in [`NEW_SYSTEM_PRODUCTION_READINESS.md`](../NEW_SYSTEM_PRODUCTION_READINESS.md) for Stage 2 P0.

Related: [`NEW_SYSTEM_BOUNDARY.md`](../NEW_SYSTEM_BOUNDARY.md),
[`DATABASE_RECOVERY_BASELINE.md`](../DATABASE_RECOVERY_BASELINE.md).

**Legend:** `Pass` | `Fail` | `Blocked` | `N/A` | **Not Run** (default)

---

## Session metadata

| Field | Value |
| --- | --- |
| Date | |
| Operator | |
| Environment | |
| Branch / commit | |
| Build config | Release recommended for menu parity |

---

## 1. Database connect

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 1.1 | Launch V2; select **New System** at startup mode | Not Run | |
| 1.2 | `RunNewSystemStartup` completes without silent Legacy fallback | Not Run | |
| 1.3 | DB connection succeeds (or clear error — no silent continue) | Not Run | |
| 1.4 | **NewShellWindow** opens; **Legacy MainWindow** does not | Not Run | |

---

## 2. Credential Vault

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 2.1 | Vault configured or setup completes successfully | Not Run | May show legacy SecretSetup during startup until HostMode (Stage 4) |
| 2.2 | Connection string readable from vault / config bridge | Not Run | |
| 2.3 | Native **מפתחות וסודות** opens from shell menu (admin) | Not Run | Native `SecretSetupWindow`, not legacy |

---

## 3. Gmail restore (G-Startup)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 3.1 | `StartNewSystemConnectorAuthRestore` runs at startup — **no** interactive Google login prompt | Not Run | |
| 3.2 | With valid stored token, session restores silently | Not Run | |
| 3.3 | Without token, **Connect Google** available only on user action in Email surface | Not Run | |

---

## 4. ACC health / diagnostics (authenticated)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 4.1 | Open **סטטוס ACC** from shell menu (admin gate) | Not Run | |
| 4.2 | Mode / health / diagnostics display correctly | Not Run | |
| 4.3 | Key diagnostics show hash/prefix only — **no** raw secret | Not Run | |
| 4.4 | Read-only browse / reconciliation works if sample data exists | Not Run | |
| 4.5 | **Absent:** upload, provisioning, metadata write as normal UI | Not Run | |

---

## 5. Email surface (**מיילים**)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 5.1 | Menu **מיילים** visible per `Shell.OpenEmailSurface` | Not Run | |
| 5.2 | Email surface opens in main content host | Not Run | |
| 5.3 | Project selector works; summaries load for project label | Not Run | |
| 5.4 | Refresh / Search / ClearSearch behave correctly | Not Run | |
| 5.5 | Body + attachment **metadata** load; write actions hidden/disabled | Not Run | |

---

## 6. ProjectWork (**בעבודה 2**)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 6.1 | Menu **בעבודה 2** visible per `Shell.OpenProjectWorkSurface` | Not Run | |
| 6.2 | Surface opens; project context respected | Not Run | |
| 6.3 | Browse / navigation basic smoke — no unhandled crash | Not Run | |

---

## 7. Task Workbench (**לוח משימות**)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 7.1 | Menu **לוח משימות** visible per `Shell.OpenTaskPanelReadOnly` | Not Run | |
| 7.2 | Quick / Medium / Long queues display | Not Run | |
| 7.3 | Read-only — no unintended workflow mutation from panel | Not Run | |

---

## 8. Inspection reports (**דוחות ביקורת**)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 8.1 | Menu **דוחות ביקורת** visible per `Shell.OpenInspectionSurface` | Not Run | Release build |
| 8.2 | Native inspection window opens | Not Run | |
| 8.3 | **ביקורת (מעטפת — DEBUG)** harness **absent** in Release | Not Run | DEBUG-only |

---

## 9. Workflow closed viewer (**צפייה בתהליכים (סגור)**)

| # | Check | Status | Notes |
| --- | --- | --- | --- |
| 9.1 | Menu item visible per `Shell.OpenWorkflowClosedViewer` | Not Run | |
| 9.2 | Read-only canvas opens (legend + templates) | Not Run | |
| 9.3 | No save / stage mutation from viewer | Not Run | |

---

## 10. Final decision

| Outcome | When |
| --- | --- |
| **Ready for limited internal pilot** | Every required section **Pass** (or **N/A** where noted) |
| **Needs fix before pilot** | Any section **Fail** |
| **Blocked** | Missing DB / vault / Gmail / ACC credentials or config |

```text
Final decision:  [ ] Ready   [ ] Needs fix   [ ] Blocked
Known issues:

Signed: _______________  Date: _______________
```

After **Pass:** update [`NEW_SYSTEM_PRODUCTION_READINESS.md`](../NEW_SYSTEM_PRODUCTION_READINESS.md) §9.2–§9.3 status from **Not Run** to **Passed** with operator + commit reference.
