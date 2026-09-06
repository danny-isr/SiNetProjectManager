# Inspection Report — Phase 2 Feature Inventory & Certification Matrix

> **Status:** Partially certified (Phase 2 deep live closed for core product paths)  
> **Updated:** 2026-09-06 (Phase 2 close-gaps session)  
> **Baseline start:** `6e6a3bbcd8393cef4f9bbe7483734c575a0704b2`  
> **Overnight HEAD:** `71298994401459fdadb7936a65e4778bd4422cb1`  
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
| `IInspectionNoteCommandService` | `SqlInspectionNoteCommandService` | PASS (composition + live) |
| `IInspectionReportCommandService` | `SqlInspectionReportCommandService` | PASS (composition) |
| `IInspectionDrawingCommandService` | `SqlInspectionDrawingCommandService` + UI Add/Remove | FIXED + PASS (unit/composition); live OpenFileDialog **NOT EXERCISED — OpenFileDialog interactive prerequisite** |
| `IInspectionReportTaskLinkService` | `SqlInspectionReportTaskLinkService` | PASS (composition) |
| `IInspectionNoteAiReviewer` | `OllamaInspectionNoteAiReviewer` via `AddSiNetAi` | **LIVE PASS** (grammar + rephrase + non-apply + Apply persist) |
| `IInspectionTemplateCatalog` | `GoogleDriveInspectionTemplateCatalog` | PASS (composition; live list visible on #136) |
| `IInspectionTemplateSheetReader` | Google reader | PASS (composition) |
| `IInspectionReportExportPort` | **`GoogleSheetsInspectionReportExportPort`** via `AddSiNetGoogle` | **LIVE PASS** Export #9; SentAt NULL; IsLockedAfterSend false |
| `IInspectionPlannerResponseService` | **`GoogleInspectionPlannerResponseService`** (Column-A pull + legacy DB fields) | **FIXED + PASS** (VM wired; unit Mark/Repull); live Google pull needs exported sheet with planner cells |
| `IInspectionNoteScreenshotHost` | **`StandaloneInspectionNoteScreenshotHost`** | wired; live clipboard upload **NOT EXERCISED — UI clipboard attach path not automated this session** |
| `IInspectionNoteLinkedFileHost` | **`StandaloneInspectionNoteLinkedFileHost`** (hubs) | FIXED composition |
| `IInspectionFileTreePickerHost` | **`StandaloneInspectionFileTreePickerHost`** via `IProjectFileQueryService` (no Project Work UI) | **FIXED + PASS** (unit); live pick **NOT EXERCISED — project 136 usable file presence / interactive picker** |
| `IInspectionReportComposeDraftService` | **`SqlInspectionReportComposeDraftService`** | FIXED + PASS (unit + LIVE Task #300); To SoT = **ProjectPlanners** |
| `IInspectionReportEmailHost` | **`NoOpInspectionReportEmailHost`** (unused by VM); Task #300 compose strip never sends | PASS (hard stop) |

**Export lifecycle (Target):** `ExportAsync` may create the Google artifact and persist `SentSpreadsheetId` / `SentSpreadsheetUrl` for Share/readback. It must **not** set `SentAt` or `IsLockedAfterSend`. Email success remains the Sent/lock boundary.

**Share safe boundary:** `ShareAsync` grants anyone-with-link. Live Share was **not** invoked. Automated/unit path may prove command enablement + targeting only.

---

## 1. VALIDATION

| # | Capability | Notes | Result |
| --- | --- | --- | --- |
| V1 | General field empty → INVALID | `HasGeneralFieldValidationError` | PASS (unit) |
| V2 | Note missing status → INVALID | | PASS (unit) |
| V3 | Status ≠ NotApplicable + empty text → INVALID | Passed/Failed/RecurringFailed/ManagerReview | PASS (unit) |
| V4 | NotApplicable + empty text → VALID | | PASS (unit) |
| V5 | ManagerReview blocks Export | Even with text | PASS (unit) |
| V6 | `CanExport` aggregates generals + numbered notes | | PASS (unit) |
| V7 | Live gate: invalid↔valid updates `ValidationSummary` + Export enable | Phase 1 PASS on #9 | PASS (Phase 1 live) |
| V8 | Status ComboBox options | Failed/Passed/RecurringFailed/NotApplicable/ManagerReview | PASS |
| V9 | Settings `StatusLabel_*` → ComboBox | Labels from settings; DbKeys stable | **FIXED + PASS** (`InspectionStatusOptionsBuilder` + VM + tests) |
| V10 | Full live matrix all statuses on #9 + restore exportable | Product-path via note commands + workspace reload (NoteId 53) | **PASS (LIVE)** `InspectionReport9LiveValidationMatrixTests` |

---

## 2. GENERAL FIELDS

| # | Capability | Control / command | Result |
| --- | --- | --- | --- |
| G1 | Auto-fill labels | `BuildAutoFieldValues` | PASS |
| G2 | Inspector email from `SIUser.Email` | Phase 1 fix | PASS |
| G3–G6 | Manual override → save → restore auto | Harmless non-identity field on #9 | **PASS (LIVE)** `InspectionReport9LiveGeneralOverrideTests` |

---

## 3. QUESTIONNAIRE TREE

| # | Capability | Control / command | Result |
| --- | --- | --- | --- |
| Q1 | Chapters / sections / notes load | TreeView | PASS |
| Q2 | Numbering (`NoteSubIndex`) | | PASS |
| Q3–Q4 | Add note (section / toolbar) | Command path + unique SubIndex | **PASS (LIVE)** add siblings via `AddNoteAsync` (toolbar UI chrome PASS overnight unit; live toolbar click **NOT EXERCISED — UI automation** ) |
| Q5–Q6 | Move Up/Down | `RenumberNotesAsync` swap | **PASS (LIVE)** |
| Q7–Q8 | Save text/status | | **PASS (LIVE)** via matrix / CRUD |
| Q9 | Reload / Refresh | | PASS |
| Q10 | `SaveNoteCommand` unbound | Dead command | NOT APPLICABLE |

---

## 4. AI (`OllamaInspectionNoteAiReviewer`) — MUST LIVE-TEST

| # | Capability | Control | Result |
| --- | --- | --- | --- |
| AI1 | Availability probe | `IsAvailableAsync` | PASS |
| AI2–AI5 | Grammar/rephrase suggestions; non-apply leaves original; Apply persists | Live Ollama on #9 NoteId 53 | **PASS (LIVE)** `InspectionReport9LiveAiTests` |
| AI6–AI13 | Stale suggestion overwrite / busy note switch / forced network | UI concurrency / unit network | **NOT EXERCISED — UI concurrency automation**; forced network remains unit-only by design |
| AI14 | Explicit Reject control | Missing — non-apply is implicit reject | NOT APPLICABLE |

---

## 5. IMAGES / SCREENSHOTS

| # | Capability | Result |
| --- | --- | --- |
| S1–S9 | Clipboard → Drive → DB → reopen → Open Last → duplicate | **NOT EXERCISED — UI clipboard attach + Drive upload not automated this session** |

---

## 6. LINKED FILE

| # | Capability | Result |
| --- | --- | --- |
| L1 | Picker opens without Project Work UI registration | **FIXED + PASS** (unit `StandaloneInspectionFileTreePickerHostTests`) |
| L2–L4 | Select/link/reopen/open/replace/clear live | **NOT EXERCISED — interactive picker + project 136 usable ACC/file row** |

---

## 7. REVIEWED FILES

| # | Capability | Result |
| --- | --- | --- |
| R1 | Load reviewed files into metadata | PASS |
| R2 | Select plan(s) live | **NOT EXERCISED — interactive picker** |
| R3 | Display list in XAML | FIXED + PASS |

---

## 8. DRAWINGS

| # | Capability | Result |
| --- | --- | --- |
| D1 | Load into `DrawingsPanel` VM | PASS |
| D2 | Bound in production XAML | FIXED + PASS |
| D3 | Add/remove commands | FIXED + PASS (unit); live Add **NOT EXERCISED — OpenFileDialog** |

---

## 9. RICH TEXT / EDITOR

| # | Capability | Result |
| --- | --- | --- |
| E1–E6 | Exhaustive editor matrix (RTL, colors, bold, paste, reopen) | **NOT EXERCISED — UI rich-editor automation not run this session** |

---

## 10. TEMPLATE / CREATE

| # | Capability | Result |
| --- | --- | --- |
| T1–T2 | Template list / refresh | PASS |
| T3–T5 | Create / series / snapshot | PASS (prior #9 create) |

---

## 11. EXPORT / SHARE

| # | Capability | Result |
| --- | --- | --- |
| X1 | Validation gate disables Export | PASS |
| X2 | Export reaches Google port | PASS |
| X3–X6 | Live Export #9 → SpreadsheetId/URL; SentAt NULL; unlocked | **PASS (LIVE)** `InspectionReport9LiveExportTests` |
| X7 | Share anyone-with-link | **NOT EXERCISED — STOP before permission mutation** (safe boundary); command conditions only |

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
| P2 | Mark response received | **FIXED + PASS** (wired to `IInspectionPlannerResponseService`; unit) |
| P3 | Re-pull planner | **FIXED + PASS** (unit isRepull); live Google Column-A pull **NOT EXERCISED — requires planner-filled cells on exported sheet** (fake/integration path covered by unit Fake) |

---

## 14. MULTI-ROUND / RECURRING

| # | Capability | Result |
| --- | --- | --- |
| M1–M3 | Recurring / series | NOT EXERCISED — multi-round workflow not in this gate |

---

## 15. TASK MODE / ROUTING

| # | Capability | Result |
| --- | --- | --- |
| TM1–TM4 | Task routing / #300 → Inspection | PASS |
| TM5 | #300 compose for Report #4 STOP BEFORE SEND | FIXED + PASS (**LIVE**) |
| TM6 | Canonical recipient = **ProjectPlanners** only (no invented fallback) | **PASS** — empty To + warning `חסר נמען מתכנן (ProjectPlanners)`; Send remains blocked |
| TM7 | Artifact URL on compose after Export | **PASS** after #9 export path; #4 artifact display **NOT EXERCISED — no Export on workflow #4 this session (preserve send boundary)** |

---

## 16. Shell / Current Project prerequisites (Inspection live)

| # | Capability | Result |
| --- | --- | --- |
| CP1–CP4 | Menu / Project 136 / Report #9 | PASS / FIXED + PASS |

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
| 2026-09-05 Phase 2 code | — | Export port real; Screenshot host real; Task #300→Inspection | `6e6a3bb` |
| 2026-09-06 overnight | #9/#300 | UI chrome + compose STOP BEFORE SEND | `3ecfdb1` / `d205970` |
| 2026-09-06 morning | — | StatusLabel_* + file picker without Project Work UI | `c09cdbb` |
| 2026-09-06 Phase 2 close | #9 | Validation matrix, general override, note CRUD/reorder, AI, Export (SentAt null), planner VM wire | Live tests under `InspectionReport9Live*` + planner unit tests |

### Isolated dirty work (NOT in Inspection commits)

Leave untouched / do not stage:
- `docs/PILOT_CONTROLS.md`, `docs/TEST_STRATEGY.md`, `docs/manual-tests/STANDALONE_PILOT_SMOKE.md`
- `src/SiNet.App.Wpf.Tests/Live/PilotSmoke*`, `P0Pilot*`
- `ProposalWorkflowHarness.cs`, `IProjectTypeContinuationStarter.cs`, `ProcessBackboneServiceCollectionExtensions.cs`, `SqlProjectTypeContinuationStarter.cs`
- `tmp-e2e/`

---

## 19. Certification verdict

**INSPECTION REPORT MODULE = PARTIALLY CERTIFIED**

Closed this session into real LIVE PASS (product path): validation matrix, general override, note CRUD/reorder, AI grammar/rephrase Apply, Export artifact with SentAt/lock invariant, StatusLabel_* + planner Mark/Repull wiring, ProjectPlanners recipient SoT.

Remaining user-facing gaps (honest NOT EXERCISED): screenshot clipboard E2E, rich editor matrix, linked-file/reviewed/drawings interactive picks, Share permission mutation (intentionally stopped), planner live Column-A on filled sheet, UI toolbar click / concurrency AI.

Full **CERTIFIED** requires closing those interactive UI rows (or documenting permanent N/A with operator sign-off).
