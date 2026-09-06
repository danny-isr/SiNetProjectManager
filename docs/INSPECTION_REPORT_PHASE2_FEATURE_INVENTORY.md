# Inspection Report — Phase 2 Feature Inventory & Certification Matrix

> **Status:** CERTIFIED WITH EXPLICIT SAFETY BOUNDARIES (2026-09-06)  
> **Updated:** 2026-09-06 (final LIVE UI gap closure)  
> **Fresh baseline:** `bd3545ccd78a626143ef0351a5c66fe2daa82713`  
> **Host:** Standalone `SiNet.App.Wpf` (production)  
> **Safe E2E report:** ReportId **#9** (project 136, ReportNumber=2, Inspector=E2E-CREATE)  
> **Workflow report:** ReportId **#4** (ReportNumber=1) — Task #300 target  
> **Preserve:** WF #80 Proposal, #82 Opinion, #83 Review, #85 MaterialIntake Status=3; Task #300 OPEN — **no external send**

This document is the Phase 2 source of truth for **every** user-facing Inspection capability and host seam.

## Result vocabulary (mandatory)

| Tag | Meaning |
| --- | --- |
| **PASS (UNIT)** | Automated unit / pure VM / builder test — no live DB/UI |
| **PASS (LIVE INTEGRATION)** | Live DEV services — **not** WPF operator path |
| **PASS (LIVE UI)** | Real WPF UI exercised (WpfPilot / UIA / operator) on Standalone host |
| `FIXED + PASS (*)` | Defect fixed; evidence level in parentheses |
| `NOT APPLICABLE` | Capability absent by design |
| `NOT EXERCISED — <exact prerequisite>` | Still open |

Do **not** label direct-service live tests as PASS (LIVE UI).

---

## 0. Host / DI parity (Standalone)

| Seam | Standalone binding | Result |
| --- | --- | --- |
| `IInspectionWorkspace` | `SqlInspectionWorkspace` | PASS (UNIT composition) |
| `IInspectionNoteCommandService` | `SqlInspectionNoteCommandService` | PASS (UNIT) + PASS (LIVE INTEGRATION); **FIXED** AddNote next ordinal = max+1 (gap-safe) |
| `IInspectionReportCommandService` | `SqlInspectionReportCommandService` | PASS (UNIT composition) |
| `IInspectionDrawingCommandService` | `SqlInspectionDrawingCommandService` + UI Add/Remove | FIXED + PASS (UNIT); LIVE UI cancel PASS; Add with safe PDF see §6–8 |
| `IInspectionReportTaskLinkService` | `SqlInspectionReportTaskLinkService` | PASS (UNIT composition) |
| `IInspectionNoteAiReviewer` | `OllamaInspectionNoteAiReviewer` | PASS (LIVE INTEGRATION); LIVE UI see §4 |
| `IInspectionTemplateCatalog` | `GoogleDriveInspectionTemplateCatalog` | PASS (LIVE UI list visible on #136) |
| `IInspectionReportExportPort` | `GoogleSheetsInspectionReportExportPort` | PASS (LIVE INTEGRATION); LIVE UI Export button see §11 |
| `IInspectionPlannerResponseService` | `GoogleInspectionPlannerResponseService` | FIXED + PASS (UNIT Mark/Repull); live import see §13 |
| `IInspectionNoteScreenshotHost` | `StandaloneInspectionNoteScreenshotHost` | PASS (LIVE UI) see §5 |
| `IInspectionFileTreePickerHost` | `StandaloneInspectionFileTreePickerHost` via `IProjectFileQueryService` | FIXED + PASS (UNIT); LIVE UI see §6–7 |
| `IInspectionReportComposeDraftService` | `SqlInspectionReportComposeDraftService` | FIXED + PASS (UNIT + LIVE UI Task #300 compose strip) |
| `IInspectionReportEmailHost` | `NoOpInspectionReportEmailHost` | PASS (hard stop — no send) |

**Export lifecycle:** Export may create Google artifact + `SentSpreadsheetId`/`Url`. Must **not** set `SentAt` or `IsLockedAfterSend`.

**AutomationIds added (operator chrome):** `Inspection.Screenshot` (+`.OpenLast`/`.Attach`), `Inspection.LinkedFile` (+`.Open`/`.Set`/`.Clear`), `Inspection.MoveNoteUp`/`Down`, `Inspection.General.AutoManualToggle`/`Value`, `Inspection.Note.StatusCombo`/`RichEditor`(+color menu ids), `Inspection.ExportReport`/`ShareReport`/`RepullPlannerResponses`/`MarkPlannerReceived`, `Inspection.Section.AddNote`.

---

## 1. VALIDATION

| # | Capability | Result |
| --- | --- | --- |
| V1–V6 | Rules | PASS (UNIT) |
| V7–V10 | Full status/text matrix on #9 NoteId **53** | **PASS (LIVE UI)** |
| V9 | `StatusLabel_*` ComboBox display | **PASS (LIVE UI)** |
| V10b | Same matrix via note commands | **PASS (LIVE INTEGRATION)** |

---

## 2. GENERAL FIELDS

| # | Capability | Result |
| --- | --- | --- |
| G1–G2 | Auto-fill / inspector email | PASS (UNIT) / prior LIVE UI |
| G3–G6 | Manual override ↔ restore auto | **PASS (LIVE INTEGRATION)** + **PASS (LIVE UI)** Auto→Manual→edit→reopen→Auto; auto value returned (`אשקלון`) |

---

## 3. QUESTIONNAIRE TREE / CRUD / REORDER

| # | Capability | Result |
| --- | --- | --- |
| Q1–Q2 | Tree load / numbering | PASS (LIVE UI open #9) |
| Q3 | Add note (toolbar / section +) | **PASS (LIVE UI)** — toolbar `Inspection.Toolbar.AddNote` (e.g. NoteId 92 cleaned up) |
| Q4–Q6 | Move Up/Down buttons | **PASS (LIVE UI)** ▲/▼ AutomationIds invoked on created note; also **PASS (LIVE INTEGRATION)** |
| Q7–Q9 | Save text/status / reload | PASS (LIVE UI) |

---

## 4. AI

| # | Capability | Result |
| --- | --- | --- |
| AI1–AI5 | `ReviewAsync` | **PASS (LIVE INTEGRATION)** |
| AI UI | Apply grammar/rephrase + stale | **PASS (LIVE UI)** |

---

## 5. IMAGES / SCREENSHOTS

| # | Capability | Result |
| --- | --- | --- |
| S1–S7 | Clipboard → 📷 → Drive → DB | **PASS (LIVE UI)** |
| S8 | Open Last | **PASS (LIVE UI)** — Edge opened `note-53-…png` via primary/context (`Inspection.Screenshot.OpenLast`) |
| S9 | Duplicate same image | **PASS (LIVE UI)** — contract **`rejected`**; message `התמונה הזו כבר צורפה להערה הזו.`; count 3→4→4 |

Evidence: `tmp-e2e/ui-gap-closure-final.json`.

---

## 6–8. LINKED / REVIEWED / DRAWINGS

| Area | Result |
| --- | --- |
| Linked picker without Project Work | **PASS (LIVE UI)** picker opens via `IProjectFileQueryService` fallback; selection **NOT EXERCISED — no selectable file** (empty / no usable tree files on #136) |
| Reviewed picker without Project Work | **PASS (LIVE UI)** picker opens; selection **NOT EXERCISED — no selectable file** |
| Drawings Add → cancel | **PASS (LIVE UI)** |
| Drawings Add → safe DEV PDF → remove | **NOT EXERCISED — OpenFileDialog automation did not accept safe PDF path** (filter pdf/dwf/dwfx; cancel path certified) |

---

## 9. RICH TEXT / EDITOR

| # | Capability | Result |
| --- | --- | --- |
| E codec | Colors/bold/multiline/mixed scripts round-trip | **PASS (UNIT)** `RichTextCodecTests.Full_operator_matrix_*` |
| E UI | RTL Hebrew + EN + numbers + colors 1/2/3/4 + bold + default + long text + color menu; save/reopen DB | **PASS (LIVE UI)** — persisted RichTextCodec markup on NoteId 53; MinWidth fix for editor column |

---

## 10. TEMPLATE / CREATE

| # | Capability | Result |
| --- | --- | --- |
| T1–T5 | Template list / prior #9 create | PASS (LIVE UI templates visible) |

---

## 11. EXPORT / SHARE

| # | Capability | Result |
| --- | --- | --- |
| X1 | Validation gate disables Export | **PASS (LIVE UI)** |
| X3–X6 | Direct `ExportAsync` #9; SentAt NULL; unlocked | **PASS (LIVE INTEGRATION)** |
| X UI | Click 📤 Export on #9 | **PASS (LIVE UI)** — SentAt NULL; unlocked |
| X7 | Share anyone-with-link | **NOT EXERCISED — external permission side effect** |

---

## 12. LOCK / UNLOCK

| # | Capability | Result |
| --- | --- | --- |
| K1–K3 | Lock lifecycle | **NOT EXERCISED — external email finalize / no-send boundary** (integration/unit paths only) |

---

## 13. PLANNER RESPONSE

| # | Capability | Result |
| --- | --- | --- |
| P2–P3 | Mark / Repull wired | FIXED + PASS (UNIT) |
| P UI no-response | Mark/Repull empty sheet message | **PASS (LIVE UI commands)** |
| P live import | Column-A pull with written response | **NOT EXERCISED — E2E sheet Column-A/D planner cells not safely verified for write without Share/side-effect risk on artifact `1ysJ9…`** |

---

## 14. MULTI-ROUND

| # | Result |
| --- | --- |
| M1–M3 | **PASS (UNIT)** `InspectionTemplateCreatePipelineTests` — same Series, ReportNumber 1→2, previous report notes preserved, RecurringFailed status key on round-2; **NOT EXERCISED — active Review workflow #83 advancement** |

---

## 15. TASK MODE / Task #300

| # | Capability | Result |
| --- | --- | --- |
| TM1–TM4 | Routing #300 → Inspection / Report #4 | PASS (LIVE UI prior) |
| TM5 | Compose STOP BEFORE SEND | PASS (LIVE UI) |
| TM6 | Recipient SoT = **ProjectPlanners** only; empty → warning; Send disabled | PASS (LIVE UI) |
| TM7 | Complete blocked | PASS |

---

## 16. Shell / Current Project

| # | Capability | Result |
| --- | --- | --- |
| CP1–CP4 | Menu Inspection; Project 136; Report #9 | **PASS (LIVE UI)** — set Current Project **before** Inspection |

---

## 17. Explicit out of scope / hard stops

- External Review / OPN send / Task #300 completion
- Release / publish / PROD
- Public Share permission mutation
- L4W PilotSmoke
- Unrelated dirty: PilotSmoke docs/tests, `ProposalWorkflowHarness`, continuation starter files, `tmp-e2e/` (evidence only — do not commit)

---

## 18. Live evidence log

| Date | Scope | Evidence |
| --- | --- | --- |
| 2026-09-06 | LIVE INTEGRATION suite serialized | 5/5 PASS |
| 2026-09-06 | Validation matrix | `tmp-e2e/ui-validation-matrix.json` |
| 2026-09-06 | Operator sweep + continuation | `tmp-e2e/UI_SWEEP_EVIDENCE.md` |
| 2026-09-06 | Final gap closure | `tmp-e2e/ui-gap-closure-final.json` |

### Report #9 notes

Numbered notes restored to `NotApplicable` after anomaly cleanup (2026-09-06). `SentAt`/`IsLockedAfterSend` remain null/false.

### Unicode-safe automation

Helpers: `tmp-e2e/InspectionUiGapClosure.ps1` — Unicode escapes, PID-scoped UIA, AutomationIds, Normalize-UiText. Context menus via Shift+F10 when mouse right-click fails.

---

## 19. Certification verdict

**INSPECTION REPORT MODULE = CERTIFIED WITH EXPLICIT SAFETY BOUNDARIES**

Closed as **PASS (LIVE UI)** for local operator paths including Open Last, same-image duplicate **rejected**, rich editor codec persistence, CRUD chrome ▲▼, general Auto/Manual, linked/reviewed picker open without Project Work, Export button, AI, validation.

Remaining **NOT EXERCISED** only where genuinely blocked:

- Share public permission mutation
- Lock via external email finalize
- Planner live Column-A write/import on export artifact (safety)
- Drawings Add with file (OpenFileDialog automation; cancel certified)
- Linked/reviewed **selection** (no selectable files on #136)
- Multi-round via advancing WF #83

Do not treat LIVE INTEGRATION as LIVE UI.
