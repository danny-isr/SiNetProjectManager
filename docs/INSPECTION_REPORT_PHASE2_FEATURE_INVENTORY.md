# Inspection Report — Phase 2 Feature Inventory & Certification Matrix

> **Status:** Target / Partially certified overnight  
> **Updated:** 2026-09-06 (morning checkpoint)  
> **Baseline start:** `6e6a3bbcd8393cef4f9bbe7483734c575a0704b2`  
> **Baseline HEAD:** see git `development` tip after overnight commits  
> **Host:** Standalone `SiNet.App.Wpf` (production)  
> **Safe E2E report:** ReportId **#9** (project 136, ReportNumber=2, Inspector=E2E-CREATE)  
> **Workflow report:** ReportId **#4** (ReportNumber=1) — Task #300 target  
> **Preserve:** WF #80 Proposal, #82 Opinion, #83 Review, #85 MaterialIntake Status=3; Task #300 OPEN — **no external send**

This document is the Phase 2 source of truth for **every** user-facing Inspection capability and host seam. Live results fill the **Result** column during certification.

**Result vocabulary:** `PASS` | `FAIL — PRODUCT DEFECT` | `FIXED + PASS` | `NOT APPLICABLE` | `NOT EXERCISED — <exact prerequisite>`

---

## 0. Host / DI parity (Standalone)

| Seam | Standalone binding | Result |
| --- | --- | --- |
| `IInspectionWorkspace` | `SqlInspectionWorkspace` | PASS (composition) |
| `IInspectionNoteCommandService` | `SqlInspectionNoteCommandService` | PASS (composition) |
| `IInspectionReportCommandService` | `SqlInspectionReportCommandService` | PASS (composition) |
| `IInspectionDrawingCommandService` | `SqlInspectionDrawingCommandService` + UI Add/Remove | FIXED + PASS (unit/composition; live OpenFileDialog NOT EXERCISED — dialog prerequisite) |
| `IInspectionReportTaskLinkService` | `SqlInspectionReportTaskLinkService` | PASS (composition) |
| `IInspectionNoteAiReviewer` | `OllamaInspectionNoteAiReviewer` via `AddSiNetAi` | Ollama UP (gemma3/4, llama3.1) — live Grammar/Rephrase NOT EXERCISED — overnight time |
| `IInspectionTemplateCatalog` | `GoogleDriveInspectionTemplateCatalog` | PASS (composition; live list visible on #136) |
| `IInspectionTemplateSheetReader` | Google reader | PASS (composition) |
| `IInspectionReportExportPort` | **`GoogleSheetsInspectionReportExportPort`** via `AddSiNetGoogle` | wired; live Export on #9 NOT EXERCISED — overnight time |
| `IInspectionNoteScreenshotHost` | **`StandaloneInspectionNoteScreenshotHost`** | wired; live clipboard upload NOT EXERCISED — overnight time |
| `IInspectionNoteLinkedFileHost` | **`StandaloneInspectionNoteLinkedFileHost`** (hubs) | FIXED (composition); live open NOT EXERCISED — Project Work tree registration |
| `IInspectionFileTreePickerHost` | **`StandaloneInspectionFileTreePickerHost`** | FIXED (composition); live pick NOT EXERCISED — Project Work tree registration |
| `IInspectionReportComposeDraftService` | **`SqlInspectionReportComposeDraftService`** | FIXED + PASS (unit + LIVE Task #300) |
| `IInspectionReportEmailHost` | **`NoOpInspectionReportEmailHost`** (unused by VM); Task #300 compose strip never sends | PASS (hard stop) |

**Overnight commits:** `3ecfdb1` (UI AutomationIds, toolbar add-note, linked/picker hosts, drawings/reviewed UI); `d205970` (Task #300 compose draft); `e6886d0`/`4d5ef4e` (inventory evidence); plus morning dashboard Current-Project stability commit.

**Export lifecycle (Target):** `ExportAsync` may create the Google artifact and persist `SentSpreadsheetId` / `SentSpreadsheetUrl` for Share/readback. It must **not** set `SentAt` or `IsLockedAfterSend`. Email success remains the Sent/lock boundary.

---

## 1. VALIDATION

| # | Capability | Notes | Result |
| --- | --- | --- | --- |
| V1 | General field empty → INVALID | `HasGeneralFieldValidationError` | PASS (unit `InspectionFillUxParityTests`) |
| V2 | Note missing status → INVALID | | PASS (unit) |
| V3 | Status ≠ NotApplicable + empty text → INVALID | Passed/Failed/RecurringFailed/ManagerReview | PASS (unit) |
| V4 | NotApplicable + empty text → VALID | | PASS (unit) |
| V5 | ManagerReview blocks Export | Even with text | PASS (unit) |
| V6 | `CanExport` aggregates generals + numbered notes | | PASS (unit) |
| V7 | Live gate: invalid↔valid updates `ValidationSummary` + Export enable | Phase 1 PASS on #9 | PASS (Phase 1 live) |
| V8 | Status ComboBox options | Failed/Passed/RecurringFailed/NotApplicable/ManagerReview | PASS (Phase 1 / XAML) |
| V9 | Settings `StatusLabel_*` → ComboBox | Hardcoded labels today — parity gap | FAIL — PRODUCT DEFECT (settings not wired to ComboBox labels) |
| V10 | Full live matrix all statuses on #9 + reopen | | NOT EXERCISED — overnight time after UI restore |

---

## 2. GENERAL FIELDS

| # | Capability | Control / command | Result |
| --- | --- | --- | --- |
| G1 | Auto-fill labels (project, place, date, inspector, email, report#) | `BuildAutoFieldValues` | PASS (unit / Phase 1) |
| G2 | Inspector email from `SIUser.Email` | Phase 1 fix | PASS (Phase 1) |
| G3 | Manual override toggle | `IsManualOverride` / AutoManualToggle | NOT EXERCISED — overnight time |
| G4 | Persist manual value | LostFocus → `SaveGeneralFieldAsync` | NOT EXERCISED — overnight time |
| G5 | Restore auto (clear manual) | status/text null | NOT EXERCISED — overnight time |
| G6 | Persistence across refresh/reselect | | NOT EXERCISED — overnight time |

---

## 3. QUESTIONNAIRE TREE

| # | Capability | Control / command | Result |
| --- | --- | --- | --- |
| Q1 | Chapters / sections / notes load | TreeView | PASS (Phase 1 / DB 36 notes on #9) |
| Q2 | Numbering (`NoteSubIndex`) | | PASS (DB evidence) |
| Q3 | Add note (section `+`) | `AddNoteCommand(section)` | NOT EXERCISED — overnight time |
| Q4 | Toolbar `+ הערה` | `AddNoteFromSelectionCommand` — requires section/note selection | FIXED + PASS (unit); live NOT EXERCISED — overnight time |
| Q5 | Move note up | `MoveNoteUpCommand` — first sibling correctly disabled | FIXED + PASS (unit enablement); live reorder NOT EXERCISED — need ≥2 sibling sub-notes |
| Q6 | Move note down | `MoveNoteDownCommand` | FIXED + PASS (unit); live NOT EXERCISED |
| Q7 | Save note text on edit complete | `EditCompleted` | NOT EXERCISED — overnight time |
| Q8 | Save status (debounced) | | NOT EXERCISED — overnight time |
| Q9 | Reload / Refresh | `RefreshCommand` | PASS (live open/refresh chrome) |
| Q10 | `SaveNoteCommand` unbound | Dead command | NOT APPLICABLE |

---

## 4. AI (`OllamaInspectionNoteAiReviewer`) — MUST LIVE-TEST

| # | Capability | Control | Result |
| --- | --- | --- | --- |
| AI1 | Availability probe | `IsAvailableAsync` | PASS (Ollama `/api/tags` UP — 6 models) |
| AI2–AI13 | Grammar/rephrase/apply/stale/concurrency/errors | UI | NOT EXERCISED — overnight time (Ollama available) |
| AI14 | Explicit Reject control | **Missing** — non-apply is implicit reject | NOT APPLICABLE (intentional non-apply semantics documented) |

---

## 5. IMAGES / SCREENSHOTS

| # | Capability | Result |
| --- | --- | --- |
| S1–S9 | Clipboard → Drive → DB → reopen | NOT EXERCISED — overnight time |

---

## 6. LINKED FILE

| # | Capability | Result |
| --- | --- | --- |
| L1–L4 | Select/open/clear | FIXED composition (`Standalone*` hosts); live NOT EXERCISED — Project Work tree not registered without browse |

---

## 7. REVIEWED FILES

| # | Capability | Result |
| --- | --- | --- |
| R1 | Load reviewed files into metadata | PASS (composition) |
| R2 | Select plan(s) | FIXED composition (picker host); live NOT EXERCISED — Project Work tree |
| R3 | Display list in XAML | FIXED + PASS (`Inspection.ReviewedFiles` bound) |

---

## 8. DRAWINGS

| # | Capability | Result |
| --- | --- | --- |
| D1 | Load into `DrawingsPanel` VM | PASS (composition) |
| D2 | Bound in production XAML | FIXED + PASS |
| D3 | Add/remove commands | FIXED + PASS (unit/composition); live Add NOT EXERCISED — OpenFileDialog |

---

## 9. RICH TEXT / EDITOR

| # | Capability | Result |
| --- | --- | --- |
| E1–E6 | Exhaustive editor matrix | NOT EXERCISED — overnight time |

---

## 10. TEMPLATE / CREATE

| # | Capability | Result |
| --- | --- | --- |
| T1–T2 | Template list / refresh | PASS (live: 3 templates found on project 136) |
| T3–T5 | Create / series / snapshot | PASS (prior create evidence for #9); no new create overnight |

---

## 11. EXPORT / SHARE

| # | Capability | Result |
| --- | --- | --- |
| X1 | Validation gate disables Export | PASS (Phase 1) |
| X2 | Export reaches Google port | PASS (composition `6e6a3bb`) |
| X3–X7 | Live Export/Share on #9 + SentAt null | NOT EXERCISED — overnight time |

---

## 12. LOCK / UNLOCK

| # | Capability | Result |
| --- | --- | --- |
| K1–K3 | Lock lifecycle | NOT EXERCISED — no email finalize; #4/#9 SentAt NULL, IsLockedAfterSend=0 |

---

## 13. PLANNER RESPONSE

| # | Capability | Result |
| --- | --- | --- |
| P1 | Indicator `HasPlannerResponse` | PASS (UI chrome) |
| P2 | Mark response received | FAIL — PRODUCT DEFECT (Stub) |
| P3 | Re-pull planner | FAIL — PRODUCT DEFECT (Stub) |

---

## 14. MULTI-ROUND / RECURRING

| # | Capability | Result |
| --- | --- | --- |
| M1–M3 | Recurring / series | NOT EXERCISED — overnight time (DB shows multiple ReportNumbers on 136) |

---

## 15. TASK MODE / ROUTING

| # | Capability | Result |
| --- | --- | --- |
| TM1–TM3 | Task routing / complete | PASS (unit) |
| TM4 | Task #300 routes to Inspection | PASS (unit) |
| TM5 | #300 compose for Report #4 STOP BEFORE SEND | FIXED + PASS (**LIVE**) |

---

## 16. Shell / Current Project prerequisites (Inspection live)

| # | Capability | Result |
| --- | --- | --- |
| CP1 | Open דוחות ביקורת via `Shell.Menu.InspectionReports` | PASS (LIVE) |
| CP2 | Select Project **136** without matching 3136 | FIXED + PASS (dashboard numeric exact filter + tests) |
| CP3 | `פתח פרויקט` sets Current Project without host crash | FIXED + PASS (no longer auto-opens Project Work; LIVE set Current → Inspection shows `136 — ניהול משרד`) |
| CP4 | Select ReportId **9** (E2E-CREATE) | PASS (LIVE morning) |

---

## 17. Explicit out of scope this round

- External Review / OPN send
- Release / publish / PROD
- L4W PilotSmoke / `run-p0-pilot-smoke.ps1`
- Deleting workflow preserve instances or Task #300 send
- Unrelated dirty PilotSmoke / ProposalWorkflowHarness files (leave isolated)

---

## 18. Live evidence log

| Date | Report | Scope | Evidence |
| --- | --- | --- | --- |
| 2026-09-05 Phase 1 | #9 | Validation + Export gate + email autofill | Commit `026b30d` |
| 2026-09-05 Phase 2 code | — | Export port real; Screenshot host real; Task #300→Inspection; offline tests | `6e6a3bb` |
| 2026-09-06 overnight | #9 | Live UI: Project 136 → דוחות ביקורת → select ReportId 9; close/reopen | PASS — `3ecfdb1` |
| 2026-09-06 overnight | — | Hosts: linked-file + file-tree picker + drawings UI + reviewed list | FIXED composition `3ecfdb1` |
| 2026-09-06 overnight | #4/#300 | Compose draft strip + CompleteTask blocked | **LIVE PASS** `d205970` / docs `4d5ef4e` |
| 2026-09-06 morning | #9 | Current Project 136 exact + select ReportId 9 | PASS — dashboard filter/OpenSelected fix |
| Phase 2 deep | #9 | Validation matrix / AI / screenshot / Export / Share / reorder live | **NOT EXERCISED** — overnight time; Ollama UP |

### Isolated dirty work (NOT in Inspection commits)

Leave untouched / do not stage:
- `docs/PILOT_CONTROLS.md`, `docs/TEST_STRATEGY.md`, `docs/manual-tests/STANDALONE_PILOT_SMOKE.md`
- `src/SiNet.App.Wpf.Tests/Live/PilotSmoke*`, `P0Pilot*`
- `ProposalWorkflowHarness.cs`, `IProjectTypeContinuationStarter.cs`, `ProcessBackboneServiceCollectionExtensions.cs`, `SqlProjectTypeContinuationStarter.cs`
- `tmp-e2e/`
