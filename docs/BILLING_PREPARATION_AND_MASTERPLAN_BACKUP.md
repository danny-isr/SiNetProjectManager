# Billing Preparation + MasterPlan Backup Intake

> **Title:** Billing Preparation workflow and MasterPlan backup intake  
> **Date:** 09.09.2026  
> **Updated:** 10.09.2026 (A1 recovery: company-or-person customer name; Continue Prepare Bill when decision exists without a request; operation-error banner)  
> **Status:** Active. A0 accepted. Application + SQL + UI + SyncEngine inbox mode implemented on `development`. EF migrations are operator-owned (not applied in this slice). A4 hourly scope is manager-selected in «חשבונות להכנה»; never labelled as unbilled truth.  
> **Scope:** New System WPF (`SiNet.App.Wpf`) + `MasterPlan.SyncEngine --process-backup-inbox`. No PROD publish. No `release` merge.  
> **Related:** [`BILLING_CONTROL_CENTER_V1_IMPLEMENTATION_PLAN.md`](./BILLING_CONTROL_CENTER_V1_IMPLEMENTATION_PLAN.md), [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md), [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md)

---

## 0. Git baseline (this workstation, 09.09.2026)

After `git fetch origin development release --prune` and `git merge --ff-only origin/development`:

| Ref | SHA |
| --- | --- |
| `development` (HEAD) | `bf3244a8c9f936655195893787a2988c0173c2c8` |
| `origin/development` | `bf3244a8c9f936655195893787a2988c0173c2c8` |
| `origin/release` | `bf3244a8c9f936655195893787a2988c0173c2c8` |

Untracked local folder `tmp-e2e/` was left untouched (not discarded).

**PROD CHANGES = NONE. RELEASE CHANGES = NONE.**

---

## 1. Approved working design (locked)

These rules remain binding. A0 may **narrow** automation; it must not weaken them.

1. **Prepare Bill** creates/reuses a `BillingPreparationRequest`. It does **not** create a task.
2. A canonical `PrepareBill` / «הכנת חשבון» task is created **only after** the manager selects exact billing instructions and approves.
3. One billing instruction may contain **milestone components, hourly components, or both**. Do not model `BillingType` as a single project-level enum.
4. Milestone billing supports **partial percentages of a stage**.
5. Do **not** guess already-billed hours.
6. Do **not** use SiNet `PaymentsStep` as MasterPlan source of truth.
7. Missing/ambiguous MasterPlan semantics stay **PARTIAL / BLOCKED** (manual override / waiting states). Do not invent heuristics.
8. Desktop does **not** perform SQL `RESTORE`.
9. Email native `.bak`, JumboMail/external download, and manual file picker feed **one** Backup Intake service.
10. Normal JumboMail → ACC Inbox behavior stays unchanged (`Purpose = ProjectAttachment`).

Business flow:

```text
Billing candidate
  → manager decision
       ├── Not Now          (existing BillingReviewDecision semantics)
       └── Prepare Bill     → BillingPreparationRequest (idempotent)
              → manager reviews components
              → manager selects exact instructions
              → manager approves
              → one open PrepareBill task
              → employee works in MasterPlan
              → task completed
              → AwaitingMasterPlanConfirmation
              → Completed only if proven evidence exists
```

Suggested request lifecycle (names may follow project conventions; semantics must match):

`WaitingForSnapshot` → `WaitingForSelection` → `ReadyForApproval` → `TaskOpen` → `AwaitingMasterPlanConfirmation` → `Completed` / `Cancelled`

Reuse existing `TaskTypeCodes.PrepareBill`. Reuse `ITaskWorkbenchService.CreateTaskAsync`. Reuse monthly restore `BackupFinishDate` gate. Do not add `Database.Migrate()` at startup. EF migrations remain operator-owned.

---

## 2. A0 investigation identity (READ ONLY)

| Item | Value |
| --- | --- |
| Workstation | `DANNY` (DEV) |
| Live SQL | `danny\SQLEXPRESS` (`SERVERPROPERTY('MachineName')` = `danny`) |
| Monthly snapshot | `Db_Mp_SiEng` |
| Replica | `Replica_DB` |
| Queries | `SELECT` only — no writes |

This DEV copy is a **stale** monthly snapshot / replica (Replica `Sync_State` last sync **2026-04-19**; no `MonthlyRestore` row). Schema and relationships are still usable as evidence. **Do not treat row values as current Release facts.** Project **6982** is absent from this snapshot.

SiNet `PaymentsStep` (SiData) is a **different** table. MasterPlan `Db_Mp_SiEng` has **no** `PaymentsSteps` / `PaymentSteps` table.

### 2.1 Final A0 check — `Bills.StatusID` and `StepProgress` scale

Read-only on `danny\SQLEXPRESS` / `Db_Mp_SiEng`, 09.09.2026, immediately before intended A1 code.

#### `Bills.StatusID` — PROVEN

Lookup: `dbo.BillStatuses` (`ID`, `Name`). `Bills.StatusID` counts in this snapshot:

| StatusID | `BillStatuses.Name` | `Bills` rows |
| ---: | --- | ---: |
| 1 | ביצירה | 19 |
| 2 | הוגש | 505 |
| 3 | אושר | **1** |
| 4 | סגור | 2387 |

This matches V1 candidate docs. **Observed cumulative progress** uses `StatusID IN (2, 3, 4)` (הוגש / אושר / סגור). **Do not** treat `StatusID = 1` (ביצירה) as submitted. Status 3 is rare here (1 bill, 4 step lines); closed (4) is the common “accepted” terminal state.

#### `BillLines.StepProgress` scale — PROVEN as 0–1 cumulative, with dirty outliers to **flag**

Among 9204 lines with `StepID`:

| Check | Count |
| --- | ---: |
| In `[0, 1]` (tolerance 1e-7) | **9151** |
| Exactly `0.50` | 152 |
| Exactly `1.00` | 7923 |
| `NULL` | 0 |
| `< 0` | **1** (value ≈ `-1.66e-6`, float noise) |
| `> 1` | **52** (max ≈ `1.068` on StepID 7814 / BillID 2676) |

**Meaning (does not contradict A0):** `0.50` = 50% cumulative stage progress; `1.00` = 100% cumulative stage progress. This is **not** 0–100 integers and **not** an increment-per-bill.

**Does not stop stage calculation globally.** Rows with `StepProgress` outside `[0, 1]` (or otherwise inconsistent) must be **flagged**: no silent remaining/target math; manager/manual route allowed.

Do **not** SUM historical `StepProgress`.

---

## 2.2 Locked implementation decisions (accepted 09.09.2026)

**Decision 1 — target cumulative progress (not “add X%” as the primary field).**

Manager-facing fields:

| Concept | Meaning |
| --- | --- |
| `StageWeightWithinSubContract` | `SubContractSteps.Percentage` (0–1 of **this SubContract**, not the whole project/contract unless later proven) |
| `ObservedCumulativeProgress` | conceptually `MAX(StepProgress)` over statuses 2/3/4 |
| `TargetCumulativeProgress` | manager input: cumulative progress **after this bill** |
| `RequestedDelta` | `Target - Observed` (derived, not stored as SoT) |

Validation: `0 ≤ observed ≤ 1`; `observed ≤ target ≤ 1`; `delta ≥ 0`. Label weight as משקל השלב **בהסכם המשנה**.

UI:

```text
תכנון מפורט
משקל השלב בהסכם המשנה: 20%
מצב שנצפה בחשבונות MasterPlan: 40%
אחרי החשבון הנוכחי:              [70] %
תוספת בחשבון הזה:                30%
```

**Decision 2 — no automatic stage amount in V1.** Stage amount remains **BLOCKED**. Task instructions are stage + cumulative %. Do not compute `ContractValue * Percentage`.

**Decision 3 — hourly scope is manager-defined.** Never show «שעות לא מחויבות» as a MasterPlan fact. Show **available/reportable** hours for an explicit SubContract + date range; resolve to concrete `HoursReportId`s on approve. SiNet may warn «already in preparation request #X»; never «already billed in MasterPlan».

**A4 WPF picker (10.09.2026):** Tab «חשבונות להכנה», section **«רכיבי שעות»**. Manager include/remove, `FeeType=4` SubContract from the loaded snapshot, inclusive FromDate/ToDate. Preview uses `BillingHourlyScopeResolver` (report count, normalized hours). Overlap warning vs other SiNet preparation requests only. `SavePreparationAsync` persists **both** `StageEdits` and composed hourly snapshots — never reuse `Source.Hours` unchanged. Caption: manager-selected scope; never «שעות לא מחויבות» / unbilled. Hourly still never auto-confirms; after the task, «אשר שבוצע ב-MasterPlan» remains required.

**A1 snapshot header / hour-report names (10.09.2026, proven on DEV `Db_Mp_SiEng`):** `dbo.Contacts` and `dbo.Employees` have `FirstName` / `LastName`, **not** `Name`. Customer name is company **or** person:

```sql
COALESCE(
    NULLIF(LTRIM(RTRIM(comp.Name)), ''),
    NULLIF(LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))), '')
)
```

Join remains `Projects.CustomerID` → `Contacts` → `Companies`. Do not use `Contacts.Name`. Hour-report employee display name is `CONCAT(FirstName, ' ', LastName)`. `SqlBillingPreparationComponentSource.LoadAsync` must not reference `Contacts.Name` or `Employees.Name`.

**A1 UI / composition / recovery:** Production Billing Center (`AddSiNetNewSystemWpf`) requires `IBillingPreparationService` (`GetRequiredService`). The Healthy visual fixture may still pass `preparation: null`. Dashboard Refresh **reads** active preparation requests to detect a gap; it must **not** call `EnsureFromPrepareBillAsync`.

Valid failure state: active `BillingReviewDecision = PrepareBill` and no `BillingPreparationRequest`. The operator must **not** clear the decision. When `HasActivePrepareBill` and no active request for that `MasterPlanProjectId`, show **«המשך להכנת חשבון»**. That action does not write another decision; it calls existing `EnsureFromPrepareBillAsync` (idempotent). Command failures use a dedicated operation-error banner (`לא ניתן היה לפתוח בקשת הכנת חשבון: …`) without switching `UiState` to Fatal/Recoverable (candidate grid stays). Retry is allowed; once a request exists the recovery action is hidden.

**A7 confirmation:**

- Task complete → always `AwaitingMasterPlanConfirmation`.
- Stage auto-complete **only** if a **newer** `Db_Mp_SiEng` backup shows `BillLines.StepID` match **and** `StepProgress >= TargetCumulativeProgress` **and** evidence is newer than approval baseline. Old historical rows with the same % must not close the request.
- Hourly auto-complete remains **BLOCKED**. Manager action «אשר שבוצע ב-MasterPlan» (Manual) is required for hourly (and for mixed, **all** components must be confirmed).

---

## 3. Source map

| Store | Role for this feature |
| --- | --- |
| `Replica_DB.MP_Bills` / `MP_ProjectHoursExtended` / `MP_Intakes` | Current billing **candidates** (V1). Header-level bills and hours. **No** stage lines, **no** hour↔bill junction. |
| `Db_Mp_SiEng` | Contract/stage/bill-detail **snapshot** after bak restore. Required for preparation components. |
| SiNet `BillingReviewDecision` | Manager first decision (PrepareBill / NotNow). Not the preparation lifecycle. |
| SiNet `PaymentsStep` | **Forbidden** as MasterPlan SoT (V1 already unused). |
| Monthly ETL (`MonthlyBackupRestoreService`) | Copies header `Bills` → `MP_Bills` and `HoursReports` → `MP_ProjectHoursExtended` (step **name** only). Does **not** ETL `BillLines`, `BillSubContracts`, `BillWorkingHours`, `SubContractSteps`. |

Confidence legend used below: **PROVEN** / **PARTIAL** / **BLOCKED**.

---

## 4. Stage (milestone) billing — evidence

### 4.1 Stable stage ID — PROVEN

| | |
| --- | --- |
| Table.column | `Db_Mp_SiEng.dbo.SubContractSteps.ID` |
| Relationship | `SubContractSteps.SubContractID` → `SubContracts.ID` → `SubContracts.ContractID` → `Contracts.ID` → `Contracts.ProjectID` → `Projects.ID` |
| Display title | `SubContractSteps.Name` |
| Order | `SubContractSteps.OrderNum` |

`BillLines.StepID` joins **1:1** to `SubContractSteps.ID`: **9204 / 9204** lines with `StepID` match; **0** orphans.

Representative (project 3425, subcontract 1948 «תכנון תנועה…», FeeTypeID=3):

| StepID | Name | Percentage (0–1) |
| --- | --- | --- |
| 2617 | תכנון מוקדם | 0.35 |
| 2618 | תכנון סופי | 0.25 |
| 2619 | תכנון מפורט | 0.30 |
| 2620 | פיקוח עליון על הביצוע | 0.10 |

Sum of `Percentage` across a subcontract is **~1.0** (sampled many subcontracts). Values are **fractions**, not 0–100 integers.

### 4.2 SubContract / “JobType” — PARTIAL

MasterPlan does **not** put SiNet `JobType` on a stage.

| Field | Meaning in this snapshot |
| --- | --- |
| `SubContracts.TypeID` | → `SubContractTypes` (discipline / work package, e.g. תנועה, הנדסת דרכים). Often NULL. |
| `SubContracts.SubContractStepTypeID` | → `SubContractStepTypes`: `1` = «% משתנה לכל שלב», `2` = «% תואם לכל שלב» |
| `SubContracts.FeeTypeID` | → `FeeTypes` (see §5). Billing **component** kind. |

UI context should show **SubContract name + TypeID display + FeeType**, not SiNet JobType.

### 4.3 Stage % of contract — PROVEN as % of **that SubContract**, not of the whole project

| | |
| --- | --- |
| Column | `SubContractSteps.Percentage` (float 0–1) |
| Also on bills | `BillLines.StepPercentage` (snapshot at bill time) |

**Restriction:** this is the stage’s weight **inside its SubContract** (steps sum to 1.0). A project may have several SubContracts, each with its own 100%. It is **not** automatically “% of the whole project contract”.

`BillLines.StepPercentage` matches live `SubContractSteps.Percentage` on **8974 / 9204** lines; **230** mismatch (live stage table drifted after older bills). Preparation UI must show **current** `SubContractSteps.Percentage`, and persist **both** current and billed snapshot values on approval.

### 4.4 Stage monetary value — BLOCKED for general use

| Candidate | Observation |
| --- | --- |
| `SubContractSteps.VariablePrice` | **0** non-zero rows in this snapshot |
| `PercentagePrices` | Only **82** rows, almost all `FeeTypeID=1` (אחוז מעלות), `PercentageStatusID=1` (אומדן). This is a **cost-base × fee%**, not a stage value. |
| `BillLines.PercentagePricePrice` | Present on some lines; not a general stage catalog |

**Do not** invent stage amounts from Replica `MP_Bills.Sum` or `ProjectsExtraData.ProgressPercentage`. Amount on a preparation line is allowed only when a **reliable** snapshot field exists for that component; otherwise omit amount.

### 4.5 Partial-stage submission — PROVEN (cumulative bills)

There is **no** increment column “% submitted in this bill”.

| Column | Role |
| --- | --- |
| `BillLines.StepProgress` | **Cumulative** fraction (0–1) of **this stage** recorded on that bill line |
| `BillLines.Progress` | Almost always NULL — ignore |
| `SubContractSteps.Progress` | Sparse: **189 / 7109** non-null. **Do not** use as SoT (V1 already forbade relying on manually maintained progress) |

All bills in this snapshot are `Bills.TypeID = 2` («מצטבר»). Cumulative bills **repeat** already-finished stages at `StepProgress = 1.0` on later accounts.

Example stage **7379** (same StepID, many bills):

| BillID | TypeID | StatusID | SubmitDate | StepProgress |
| --- | --- | --- | --- | --- |
| 2570 | 2 | 4 סגור | 2021-10-21 | **0.50** |
| 2657 | 2 | 4 סגור | 2022-01-20 | **1.00** |
| later bills | 2 | 4 or 2 | … | **1.00** (repeated) |

Example stage **7378** «תכנון כללי»: first closed bills at **0.90**, then **1.00**, then repeated at 1.00 for years.

**Must not SUM `StepProgress` across bills.** That would explode past 100% because cumulative bills restate the stage.

**Previously submitted % of stage (operational rule, PARTIAL):**  
`MAX(BillLines.StepProgress)` over bills with `StatusID IN (2,3,4)` for that `StepID`.

**Previously approved % of stage (operational rule, PARTIAL):**  
`MAX(BillLines.StepProgress)` over bills with `StatusID IN (3,4)`.

Check: **37 / 4633** steps have submitted-max ≠ approved-max, so the two are not identical. **68** steps also appear on in-creation (`StatusID=1`) lines — in-creation must **not** be treated as submitted for remaining-%.

**% of stage requested now:** not stored in MasterPlan. This is the manager input. Validate:

`0 ≤ requestedNow ≤ (1 − previouslySubmitted)`  
using the 0–1 fraction (UI may display ×100).

### 4.6 Multiple partial bills against the same stage — PROVEN

Same `StepID` appears on many `BillID`s (up to 19 in this snapshot). Progress can increase (0.50 → 1.00) and then be restated. Partial then full is real. Restatement after 100% is cumulative-bill behavior, not a second independent increment.

### 4.7 Exact bill ↔ stage linkage — PROVEN in monthly DB, BLOCKED on Replica

```text
Bills.ID
  → BillSubContracts.BillID + SubContractID + FeeTypeID + BillStepMethodID
      → BillLines.BillSubContractID + StepID (= SubContractSteps.ID)
```

`BillStepsMethods`: `1` עם שלבים, `2` ללא שלבים, `3` משולב.

Replica `MP_Bills` has only header fields (`ID, BillNum, ProjectID, Sum, StatusID, …`). **A7 “Completed” cannot be proven from Replica alone.** Detecting that a **specific stage %** was entered in MasterPlan requires a **newer `Db_Mp_SiEng` snapshot** (or a future ETL of `BillLines`, which this slice must not invent). Until then, task completion → `AwaitingMasterPlanConfirmation` remains **BLOCKED** from auto-`Completed` for stage evidence.

`ProjectsExtraData.ProgressPercentage` is billed/financial progress (existing B2). Never engineering completion.

---

## 5. Hourly billing — evidence

### 5.1 FeeTypeID — PROVEN

`FeeTypes` in this snapshot (matches `MasterPlanSnapshotFeeTypeIds`):

| ID | Name |
| --- | --- |
| 1 | אחוז מעלות |
| 2 | יחידות |
| 3 | מחיר קבוע |
| 4 | שעות עבודה |

Live MasterPlan stores `FeeTypeID` on **`SubContracts`**, not on `Contracts` (already in V1 + `MonthlyBillingEnrichmentDataSource`).

Hourly + milestone **coexistence** — **PROVEN**: **51** projects have at least one `FeeTypeID=4` subcontract **and** another subcontract with `FeeTypeID IN (1,2,3)`. Examples: project 3884 (fees 2+3+4), 4097 (2+3+4), 4189 (1+3+4).

Do **not** collapse the project to a single `BillingType` enum.

### 5.2 Eligible hour records — PARTIAL

`HoursReports` (20,317 rows here):

| Column | Role |
| --- | --- |
| `ID` | Stable hour report id (also `MP_ProjectHoursExtended.ID`) |
| `ProjectID` | Project |
| `SubContractID` | Present on 17,657 rows |
| `SubContractStepID` | Present on 13,409 rows |
| `Hours` | Milliseconds in monthly ETL (SyncEngine DEV-021) |
| `IsHistory` | Almost unused (1 / 20,317) — **not** a billed flag |

Hours exist on **both** fee types: **3,004** rows on `FeeTypeID=4`, **13,433** on `FeeTypeID=3`. Hours on a fixed-price subcontract are **work tracking**, not automatically an hourly bill component.

Eligible hourly **component** hours: reports whose `SubContractID` has `FeeTypeID=4`. Reuse `MasterPlanHoursNormalizer` / R02 source resolution. Do not build a second hours reader.

Date range / employees are available on `HoursReports` (`DateTime`, `EmployeeID`) for **display and explicit manager scope**, not as an implicit billed filter.

### 5.3 Can we determine “already billed” reliably?

**NO.** Manual selection of hourly scope remains required.

| Candidate link | Result |
| --- | --- |
| `HoursReports` has `BillID` / billed bit | **No such columns** |
| `BillWorkingHours.HoursReportID` | Schema **exists** (`BillLineID`, `HoursReportID`, `IsCharged`, `Hours`) — the **only** table with `HoursReportID` besides hours itself — but this DEV snapshot has **0 rows** |
| Hourly `BillLines` | **919** lines with `WorkingHoursID` + `WorkingHourCount` + `WorkingHourPrice`, `FeeTypeID=4` |
| `BillLines.WorkingHoursID` | FK to catalog `WorkingHours` (`SubContractID` + `WorkingHourRoleEditionID` + role name such as «חשבון 5»). **Not** an `HoursReports.ID`. `WorkingHours.Progress` is **all NULL**. |
| `WorkingHourFromDate` / `TillDate` | **0** lines populated |

So live practice in this bak is: bill a **count of hours at a role/rate**, not a set of report IDs. There is **no** populated junction proving which `HoursReports` rows were included.

**Forbidden:** “everything since last bill”, `HoursSinceLastBill` from V1 candidates, or `ProjectsExtraData.LastBillHours` as an already-billed marker.

UI must present the limitation and require the manager to choose an explicit hourly scope (subcontract, optional date range / employees / count) and persist that snapshot.

### 5.4 bill ↔ HoursReports — BLOCKED (data), PARTIAL (schema)

Schema intent: `BillWorkingHours.HoursReportID` → `HoursReports.ID`.  
This snapshot: unused. A7 must not auto-complete hourly instructions from Replica hours.

---

## 6. The three percentages (must stay distinct)

Primary manager field is **target cumulative**, not “add X%”.

| Concept | Source | Unit | Automation |
| --- | --- | --- | --- |
| `StageWeightWithinSubContract` | `SubContractSteps.Percentage` | 0–1 of **this SubContract** | PROVEN — do not label as whole-project/contract % |
| `ObservedCumulativeProgress` | `MAX(BillLines.StepProgress)` for `StatusID IN (2,3,4)` | 0–1 of stage | PARTIAL — never SUM; ignore StatusID=1; flag values outside `[0,1]` |
| `TargetCumulativeProgress` | Manager input: cumulative after this bill | 0–1 of stage | Not in MasterPlan |
| `RequestedDelta` | `Target - Observed` | 0–1 of stage | Derived |

UI example mapping:

```text
תכנון מפורט
משקל השלב בהסכם המשנה: 20%          ← StageWeightWithinSubContract × 100
מצב שנצפה בחשבונות MasterPlan: 40%  ← ObservedCumulativeProgress × 100
אחרי החשבון הנוכחי:              [70] %  ← TargetCumulativeProgress
תוספת בחשבון הזה:                30%     ← RequestedDelta
```

Validation: `0 ≤ observed ≤ 1`; `observed ≤ target ≤ 1`; `delta ≥ 0`. Inconsistent rows: flag, no silent math.

---

## 7. Representative projects in this snapshot

| ProjectId | ProjectNum | Notes |
| --- | --- | --- |
| 5905 | 2608 | Four FeeType **3** subcontracts with full statutory/licensing stage lists. Some stages already on closed bills at StepProgress=1.0 (מקדמה, שלב 1א). |
| 5893 | 2576 | FeeType **3** only here. Stages present (e.g. תכנון מוקדם 0.40). |
| 3611 | 1182 | FeeType **3**. Stage «היתר בניה» has `SubContractSteps.Progress=0.90` (sparse; still not SoT). |
| 6982 | — | **Missing** from this DEV `Projects` table. |

Mixed fee examples (not the September four): 3884, 4097, 4189, 3458.

---

## 8. A0 verdict matrix

| Semantic | Verdict | Restriction |
| --- | --- | --- |
| Milestone mapping (stage ID, name, SubContract) | **PROVEN** | Read from `Db_Mp_SiEng`, not Replica |
| Stage % of SubContract | **PROVEN** | Not automatically % of whole project |
| Stage monetary value | **BLOCKED** | Omit unless a later proven field appears |
| Partial-stage semantics (`StepProgress` cumulative) | **PROVEN** | Never SUM across cumulative bills |
| Previously submitted % | **PARTIAL** | MAX over submitted/approved/closed lines |
| Previously approved % | **PARTIAL** | MAX over approved/closed; can differ |
| Multiple partial bills | **PROVEN** | Restatement after 100% is cumulative |
| bill ↔ stage | **PROVEN** in monthly DB | **BLOCKED** on Replica / A7 auto-complete |
| Hourly identification (`FeeTypeID=4`) | **PROVEN** | Per SubContract, mix allowed |
| Eligible hours | **PARTIAL** | Prefer hours on FeeType 4 subcontracts |
| Already-billed hours | **BLOCKED** | **NO** reliable link; manual scope required |
| bill ↔ HoursReports | **BLOCKED** | Junction empty in this snapshot |
| Hourly + milestone coexistence | **PROVEN** | 51 mixed projects |
| SiNet `PaymentsStep` as MP SoT | **BLOCKED** | Wrong database; unused by Billing |

---

## 9. Implementation consequences (do not skip)

**Safe to implement after this A0:**

- `BillingPreparationRequest` + Prepare Bill idempotency (no task).
- Tab «חשבונות להכנה».
- Load stages from monthly `SubContractSteps` (+ current bill MAX progress).
- Partial % input and validation against remaining.
- Hourly component as first-class row with **explicit** manager scope (no auto unbilled list presented as truth).
- `WaitingForSnapshot` when `SubContracts` / `SubContractSteps` missing for the project in the latest restore.
- Audited manual override.
- Approve → one `PrepareBill` task with frozen instruction snapshot (stages use **target cumulative** wording).
- Task complete → `AwaitingMasterPlanConfirmation`.
- Stage auto-`Completed` only from a **newer** monthly snapshot proving `StepProgress >= target` for the approved `StepID`.
- Hourly `Completed` only via explicit manager «אשר שבוצע ב-MasterPlan» (Manual).

**Must stay blocked / manual until new evidence:**

- Auto list of “unbilled hours” / “already billed in MasterPlan”.
- Auto `Completed` from Replica `MP_Bills` header rows or from **old** snapshot lines that already had the same %.
- Stage currency amounts (`ContractValue * Percentage`).

When a newer bak is restored, **re-run the same SELECT pack** before turning on any BLOCKED automation. Do not assume Release `BillWorkingHours` is populated.

---

## 10. Backup intake (approved design, not built in A0)

Shared pipeline; Desktop copies only.

```text
Email action «גיבוי MasterPlan»
  ├── native .bak attachment → Backup Intake
  └── JumboMail/WeTransfer (existing browser)
         └── Purpose?
                ├── ProjectAttachment → existing ACC Inbox (unchanged)
                └── MasterPlanBackup → Backup Intake
Manual «בחר קובץ גיבוי MasterPlan...» → same Backup Intake

Intake: copy to D:\SharedFolder\ProjectsData\MasterPlanBakup\Incoming
        via *.partial then atomic rename to .bak
        keep user original file
Server: MasterPlan.SyncEngine --process-backup-inbox
        HEADERONLY BackupFinishDate > last MonthlyRestore
        else skip / no DB change
        never allowOlderOrEqualBackup on this path
        never WPF RESTORE, never N:\, never watcher-only
```

Statuses: התקבל לתור / ממתין לעיבוד / שוחזר בהצלחה / דולג — הגיבוי אינו חדש יותר / נכשל. Provenance in SiData; **do not** store the `.bak` in SiData.

Existing admin monthly restore UI remains operator tooling (may still expose `--allow-older-backup`). Normal intake must not.

---

## 11. Assumptions that are NOT allowed

- SiNet `PaymentsStep.Percent` / `ApprovalPercent` / `BillSubmission` as MasterPlan stage state.
- `SubContractSteps.Progress` as submitted % SoT.
- `ProjectsExtraData.ProgressPercentage` as engineering completion or remaining stage %.
- SUM of `BillLines.StepProgress` across bills.
- `HoursSinceLastBill` or “hours after last bill date” as already-billed.
- Treating `WorkingHoursID` as `HoursReports.ID`.
- Treating vault `SI-WIN-2K19\SIDATA` on this DEV PC as the office SQL instance.
- Desktop SQL RESTORE; mapped `N:\` as a server path.
- Automatic Gmail classify-as-backup; marking mail read before intake accept.
- Changing PROD scheduled tasks from DEV.

---

## 12. Operator follow-up (DEV, after this code slice)

EF migrations are **not** created or applied by the agent. Scaffold **one** migration only from the repo root. Do **not** edit the generated `.cs`, `.Designer.cs`, or `SiNetSQLDbContextModelSnapshot.cs`. Do **not** `database update` until the generated files and SQL have been inspected.

```
dotnet ef migrations add AddBillingPreparationAndMasterPlanBackupIntake --context SiNetSQLDbContext --project src\SiNet.Infrastructure.Sql\SiNet.Infrastructure.Sql.csproj --startup-project SiNetProjectManagerV2\SiNetProjectManagerV2.csproj
```

Then apply only on DEV SQL when ready. Do **not** `Update-Database` against production.

DEV inbox processor (do **not** change PROD Scheduled Tasks from this workstation):

```
MasterPlan.SyncEngine.exe --process-backup-inbox
```

Incoming root: `D:\SharedFolder\ProjectsData\MasterPlanBakup\Incoming`. Files named `*.partial` are ignored. `--allow-older-backup` is never passed on this path.

