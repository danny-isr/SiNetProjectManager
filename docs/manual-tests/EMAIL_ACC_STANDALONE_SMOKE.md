# Email ACC — Standalone smoke (N1 / N2 / N3)

> **Status:** **Superseded** (2026-07-29)  
> **Replacement:** [`STANDALONE_PILOT_SMOKE.md`](./STANDALONE_PILOT_SMOKE.md) §3 (Email ACC N1–N3)  
> **Strategy:** [`docs/TEST_STRATEGY.md`](../TEST_STRATEGY.md)  
>
> Kept for history. Operator checklist content was folded into the unified standalone pilot smoke.
>
> Host: `SiNet.App.Wpf` + AccService Remote  
> Spec: [`docs/NATIVE_EMAIL_ACC_INGEST.md`](../NATIVE_EMAIL_ACC_INGEST.md)  
> Legend: `OK` | `FAIL` | `SKIP` | `PENDING`

## Prerequisites

1. Visual Studio: solution profile **New System + AccService** (`SiNet.slnLaunch`) — AccService on `https://localhost:8443`, then `SiNet.App.Wpf`.
2. Vault + Gmail session healthy; user has Email permission.
3. `AccServiceBaseUrl` not empty (standalone `appsettings.json` default: `https://localhost:8443`). Empty → Local mode → inbox bootstrap fails without local executor.
4. Optional: tail AccService / central logs if a step fails.

## Checklist

### N1 — Inbox upload

| # | Action | Expected | Result | Notes |
| --- | --- | --- | --- | --- |
| 1 | NewShell → מיילים → message with Gmail attachment | Opens; no `Backend לא מוגדר` / BackendNotAvailable | PENDING | |
| 2 | Explicit upload to ACC Inbox (or passive ingest) | Files under `_Inbox/THREAD_…/MSG_…/Attachments/` | PENDING | |
| 3 | Refresh ACC status strip | Gmail attachments not stuck MissingInAcc | PENDING | |

### N2 — Move

| # | Action | Expected | Result | Notes |
| --- | --- | --- | --- | --- |
| 4 | Filed message + project tags → Move | Not BackendNotAvailable; files under project filing rules | PENDING | |

### N2 — Jumbo / external download

| # | Action | Expected | Result | Notes |
| --- | --- | --- | --- | --- |
| 5 | Jumbo/WeTransfer link chip in body | In-app WebView2 (not system-browser-only) | PENDING | May need login in dedicated profile |
| 6 | Complete download in that window | Upload to Inbox; strip shows external download | PENDING | ZIP: multi-file or clear error |

### N3 — Recovery

| # | Action | Expected | Result | Notes |
| --- | --- | --- | --- | --- |
| 7 | Message with Gmail attachment MissingInAcc → select / status sync | AccItemId restored after recovery | PENDING | Needs AccService + Gmail |
| 8 | Missing set is external-download-only | No AccId clear / no Gmail re-ingest | PENDING | Optional |

## Live session log

| When | Item # | Result | Observation |
| --- | --- | --- | --- |
| _(empty)_ | | | Fill during smoke |

## How to report

Update Result columns to `OK` / `FAIL` / `SKIP`. For failures: item #, UI text, and relevant log line. Reply in chat for fix triage.
