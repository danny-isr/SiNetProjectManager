# DEV-016 — Email triage two-stage + group leaf title + «לידיעה בלבד»

> **Title:** Email surface — two-stage handling, mark-read on completion, FYI action, project group leaf header  
> **Date:** 06.08.2026  
> **Status:** Implemented on `development`  
> **Scope:** `SiNet.App.Wpf` email list/detail, Gmail triage/modify ports. No SQL schema. No city-level grouping.  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md`](./DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md) (DEV-004 superseded for trigger), [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md), `SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md`

---

## 1. Product model (locked)

### Stage 1 — classify

Operator decides for each mail:

| Choice | Effect |
| --- | --- |
| Associate to project | Gmail project label; **stays UNREAD** |
| Personal | `OfficeSystem_Personal` + **mark as read** + leave active triage list |
| Irrelevant | `OfficeSystem_Irrelevant` + **mark as read** + leave active triage list |

### Stage 2 — filed project mail

Operator walks filed + still-unread mail **by project**:

| Choice | Effect |
| --- | --- |
| «לידיעה בלבד» | `OfficeSystem_Fyi` + **mark as read**; row stays in list with `IsUnread=false` |
| Successful real Workflow | **mark as read** (not `FileOnly`) |
| FileOnly / file / move only | **no** mark as read |

### Never mark as read

- Selecting a row or loading the body (overrides DEV-004 auto mark-on-open)
- Filing / unfiling / move to project alone
- Pending triage
- `FileOnly` suggested action

### Group header

Labels remain `Office / City / Project`. Grouping stays **by project label id**. Header text shows **leaf segment only** (project display name after last `/`).

---

## 2. Mark-read matrix

| Action | Mark as read? |
| --- | --- |
| Selection / body load | No |
| File / move / FileOnly | No |
| Personal / Irrelevant | Yes |
| FYI (requires already filed) | Yes |
| Workflow success except FileOnly | Yes |

Session toggle «סמן כנקרא» (DEV-004): default **off** in all builds; **not** wired to body-load pipeline.

---

## 3. Implementation map

| Area | Change |
| --- | --- |
| `EmailListGroupBuilder` | Project label group title → leaf via `EmailProjectLabelParser` |
| `EmailDetailViewModel` | Remove mark-read from selection pipeline; mark after workflow success |
| `EmailActionBarViewModel` | `MarkAsReadEnabled` default always `false` |
| `EmailTriageStatus` + Gmail | Add `Fyi` / `OfficeSystem_Fyi`; Personal/Irrelevant/Fyi remove `UNREAD` |
| Context menu / Action Bar | «לידיעה בלבד» when `IsFiledToProject` |

---

## 4. Out of scope

- Group by city
- Mark as unread / bulk mark-read
- SQL schema / migrations
- Version bump / PROD publish (separate)
