# DEV-017 — Email list UX follow-ups

> **Title:** Email grouping order/membership, project-switch stuck detail, refresh enablement, ProjectSelector widths  
> **Date:** 06.08.2026  
> **Status:** On `release` tip @ `127dc0e` (ship **1.0.23**) -- **Needs Review:** operator/pilot verify ([`DEV_BACKLOG.md`](./DEV_BACKLOG.md) §2b); may have further local polish
> **Scope:** `SiNet.App.Wpf` Email surface + shared `ProjectSelectorView`; per-user `settings.json`. No SQL schema.  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md`](./DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md) (DEV-016), [`PROJECTS.md`](./PROJECTS.md), [`SETTINGS.md`](./SETTINGS.md)

---

## 1. Grouping (exclusive + order)

Each message appears in **exactly one** group (priority high→low):

1. `OfficeSystem_Personal` → «אישי»
2. `OfficeSystem_Irrelevant` → «לא רלוונטי»
3. Selected project Gmail label → pinned project group
4. Other project label → that project group (leaf title)
5. Else → «לא מתויג» (כולל Pending / FYI / לייבלים שאינם פרויקט)

Display order top→bottom: selected project → other projects (A–Z by leaf) → לא מתויג → לא רלוונטי → אישי.

Pinned project group emails come from the dedicated project-label page; rebuild **merges** matching mailbox rows and **must not** clear gateway-loaded rows.

---

## 2. Project switch stuck

After project context reload, force `EmailDetail.ApplySelectionAsync` on the current row even when Gmail `Id` did not change (so WebView/detail re-activates).

---

## 3. Refresh — conclusion

| Mode | Status |
| --- | --- |
| Periodic auto-refresh while open | **Does not exist** (no timer) |
| Refresh on window open / after auth | **Yes** — `AutoRefreshOnOpenAsync` |
| Manual «רענן» | `IsConnected && !IsBusy`; `CanRefreshEmails` must notify when `IsBusy` changes |

No new polling in this slice.

---

## 4. ProjectSelector widths

Two independent widths (control search box vs popup list), user-resizable; persisted in `settings.json`:

- `EmailProjectSelectorControlWidth` (default 280)
- `EmailProjectSelectorPopupWidth` (default 360)

Main window size unchanged.

**RTL fix:** resize uses absolute screen mouse delta from `DragStarted` (not inverted `HorizontalChange`) so the grip follows the mouse smoothly instead of jumping min↔max.

**Persist fix:** write widths on drag-completed + dispose-if-dirty; load sync in selector ctor; set VM properties from the Thumb handlers. Widths are **shared for all ProjectSelector hosts** (not Email-only).
