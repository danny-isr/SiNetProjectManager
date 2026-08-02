# Smoke — SiNet.App.Wpf cutover (clean machine)

> Related: [`DESKTOP_CUTOVER.md`](../DESKTOP_CUTOVER.md), [`WORKFLOW_OPS_DASHBOARD.md`](../WORKFLOW_OPS_DASHBOARD.md)

## Status — `Manual Pending` (operator)

Run after a signed MSIX is published to `\\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf\`.

## Prerequisites

- [ ] Clean Windows machine (or VM) without V2 installed
- [ ] Network access to SQL, AccService, Gmail, ACC
- [ ] Vault secrets provisioned for the test user (or Secret Setup completed once)
- [ ] Tester is an active `SIUser` with Administrator role for ops checks

## Install

1. Double-click `SiNet.App.Wpf.appinstaller` on the share.
2. Confirm Start Menu entry: **שיא חדש — מנהל פרויקטים**.
3. Confirm package identity is `SiNet.App.Wpf` (not `SiNet.ProjectManagerV2`).

## Smoke checklist

| # | Area | Steps | Pass? |
| --- | --- | --- | --- |
| 1 | Startup | App opens shell without Legacy mode picker | |
| 2 | Auth | Current Windows user authorized; shell shows user | |
| 3 | Projects | Open «ריכוז פרויקטים»; select a project | |
| 4 | Email | Open email surface; list loads; open one message | |
| 5 | Tasks | Open Task Workbench; list loads | |
| 6 | Workflow ops | מנהלה → בריאות תהליכים; summary cards + grid | |
| 7 | Workflow detail | Double-click instance → detail; Pause/Resume if safe on a test instance | |
| 8 | Workflow start | «הפעל תהליך» on a test project with allowed definition | |
| 9 | Reports | Open R01 or R03 if MasterPlan key present (else N/A) | |
| 10 | Health | מצב מערכת shows SQL/ACC/Gmail rows | |

## Sign-off

| Date | Tester | Machine | Result | Blockers |
| --- | --- | --- | --- | --- |
| | | | | |
