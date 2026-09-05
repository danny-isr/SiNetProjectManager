# Inspection Report — Phase 2 Feature Inventory & Certification Matrix

> **Status:** Target / In progress  
> **Updated:** 2026-09-05  
> **Baseline:** `026b30db6e2037b5828daffc883b8cb50b192194` on `development`  
> **Host:** Standalone `SiNet.App.Wpf` (production)  
> **Safe E2E report:** #9 (project 136) — Export / lock / AI / screenshot experiments  
> **Workflow report:** #4 — untouched until Task #300 compose boundary  
> **Preserve:** WF #80 PRP.SentFollowUp, #82 OPN.SendOpinion, #83 REV.SendReportToPlanner, #85 MAT.Complete; Task #300 OPEN — **no external send**

This document is the Phase 2 source of truth for **every** user-facing Inspection capability and host seam. Live results fill the **Result** column during certification.

**Result vocabulary:** `PASS` | `FAIL` | `NOT APPLICABLE` | `NOT EXERCISED` (only with exact unavoidable external prerequisite)

---

## 0. Host / DI parity (Standalone)

| Seam | Standalone binding | Result |
| --- | --- | --- |
| `IInspectionWorkspace` | `SqlInspectionWorkspace` | |
| `IInspectionNoteCommandService` | `SqlInspectionNoteCommandService` | |
| `IInspectionReportCommandService` | `SqlInspectionReportCommandService` | |
| `IInspectionDrawingCommandService` | `SqlInspectionDrawingCommandService` (no UI) | |
| `IInspectionReportTaskLinkService` | `SqlInspectionReportTaskLinkService` | |
| `IInspectionNoteAiReviewer` | `OllamaInspectionNoteAiReviewer` via `AddSiNetAi` | |
| `IInspectionTemplateCatalog` | `GoogleDriveInspectionTemplateCatalog` | |
| `IInspectionTemplateSheetReader` | Google reader | |
| `IInspectionReportExportPort` | **`GoogleSheetsInspectionReportExportPort`** via `AddSiNetGoogle` (SQL Unavailable is fallback only) | wired (composition PASS) |
| `IInspectionNoteScreenshotHost` | **`StandaloneInspectionNoteScreenshotHost`** via Standalone composition Replace | wired (composition PASS) |
| `IInspectionNoteLinkedFileHost` | **`NoOpInspectionNoteLinkedFileHost`** — PRODUCT GAP (open) | |
| `IInspectionFileTreePickerHost` | **`NoOpInspectionFileTreePickerHost`** — PRODUCT GAP | |
| `IInspectionReportEmailHost` | **`NoOpInspectionReportEmailHost`** (not injected into VM); Task #300 remaps to Inspection pre-send | routing PASS (unit) |

**Phase 2 code slice:** real Export (moved `GoogleReportExportService`; Export ≠ SentAt/lock), real Screenshot host, Task #300 `EmailComposeToPlanner`+`InspectionReport` → Inspection (**STOP BEFORE SEND**). Remaining product gaps: linked open, file-tree picker, planner stubs, drawings UI.

**Export lifecycle (Target):** `ExportAsync` may create the Google artifact and persist `SentSpreadsheetId` / `SentSpreadsheetUrl` for Share/readback. It must **not** set `SentAt` or `IsLockedAfterSend`. Email success remains the Sent/lock boundary.

---

## 1. VALIDATION

Source: [`InspectionQuestionnaireRules.cs`](../src/SiNet.Application/Inspection/InspectionQuestionnaireRules.cs)

| # | Capability | Notes | Result |
| --- | --- | --- | --- |
| V1 | General field empty → INVALID | `HasGeneralFieldValidationError` | |
| V2 | Note missing status → INVALID | | |
| V3 | Status ≠ NotApplicable + empty text → INVALID | Applies to Passed, Failed, RecurringFailed, ManagerReview | |
| V4 | NotApplicable + empty text → VALID | | |
| V5 | ManagerReview blocks Export | Even with text | |
| V6 | `CanExport` aggregates generals + numbered notes | | |
| V7 | Live gate: invalid↔valid updates `ValidationSummary` + Export enable | Phase 1 PASS on #9 | PASS (Phase 1) |
| V8 | Status ComboBox options | Failed/Passed/RecurringFailed/NotApplicable/ManagerReview | |
| V9 | Settings `StatusLabel_*` → ComboBox | Hardcoded labels today — parity gap | |

---

## 2. GENERAL FIELDS

| # | Capability | Control / command | Result |
| --- | --- | --- | --- |
| G1 | Auto-fill labels (project, place, date, inspector, email, report#) | `BuildAutoFieldValues` | |
| G2 | Inspector email from `SIUser.Email` | Phase 1 fix | PASS (Phase 1) |
| G3 | Manual override toggle | `IsManualOverride` / AutoManualToggle | |
| G4 | Persist manual value | LostFocus → `SaveGeneralFieldAsync` | |
| G5 | Restore auto (clear manual) | status/text null | |
| G6 | Persistence across refresh/reselect | | |

---

## 3. QUESTIONNAIRE TREE

| # | Capability | Control / command | Result |
| --- | --- | --- | --- |
| Q1 | Chapters / sections / notes load | TreeView | |
| Q2 | Numbering (`NoteSubIndex`) | | |
| Q3 | Add note (section `+`) | `AddNoteCommand(section)` | |
| Q4 | Toolbar `+ הערה` | `AddNoteCommand` without section — **dead / GAP** | |
| Q5 | Move note up | `MoveNoteUpCommand` | |
| Q6 | Move note down | `MoveNoteDownCommand` | |
| Q7 | Save note text on edit complete | `EditCompleted` | |
| Q8 | Save status (debounced) | | |
| Q9 | Reload / Refresh | `RefreshCommand` | |
| Q10 | `SaveNoteCommand` unbound | Dead command | NOT APPLICABLE |

---

## 4. AI (`OllamaInspectionNoteAiReviewer`) — MUST LIVE-TEST

| # | Capability | Control | Result |
| --- | --- | --- | --- |
| AI1 | Availability probe | `IsAvailableAsync` | |
| AI2 | Grammar suggestion (`GrammarCorrected`) | `ReviewNoteAiCommand` / auto after edit | |
| AI3 | Professional rephrase (`Rephrased`) | same | |
| AI4 | Show suggestions in context menu | `InspectionNoteRichEditor` | |
| AI5 | Apply grammar explicitly | menu → `ApplyAiSuggestionAsync("grammar")` | |
| AI6 | Apply rephrase explicitly | menu → `ApplyAiSuggestionAsync("rephrase")` | |
| AI7 | Not applying leaves original unchanged | | |
| AI8 | Accepted text persists after reopen | | |
| AI9 | Validation updates after apply | | |
| AI10 | Stale suggestion blocked for other/edited note | menu disabled when `AiOriginalText ≠ current` | |
| AI11 | Empty input | | |
| AI12 | Busy / concurrency | | |
| AI13 | AI unavailable / error path | Prefer automated tests if breaking Ollama is destructive | |
| AI14 | Explicit Reject control | **Missing** — non-apply is implicit reject | |

---

## 5. IMAGES / SCREENSHOTS — MUST LIVE-TEST (no “no clipboard” excuse)

| # | Capability | Control / command | Host | Result |
| --- | --- | --- | --- | --- |
| S1 | Attach from clipboard | `AttachScreenshotCommand` / 📷 | Standalone WPF clipboard host | |
| S2 | Upload succeeds → Drive URL | | | |
| S3 | DB `InspectionNoteAttachments` row | | | |
| S4 | Attachment count updates | | | |
| S5 | Correct `NoteId` | | | |
| S6 | Persist after close/reopen | | | |
| S7 | Open last | `OpenLastAttachmentCommand` | Standalone opens persisted Drive URL | |
| S8 | Duplicate detection | | | |
| S9 | Upload failure messaging | | | |

---

## 6. LINKED FILE

| # | Capability | Command | Host | Result |
| --- | --- | --- | --- | --- |
| L1 | Select / replace | `SetNoteLinkedFileCommand` | picker NoOp | |
| L2 | Persist link | SQL real | | |
| L3 | Open | `OpenNoteLinkedFileCommand` | linked NoOp | |
| L4 | Clear | `ClearNoteLinkedFileCommand` | SQL real | |

---

## 7. REVIEWED FILES

| # | Capability | Command | Result |
| --- | --- | --- | --- |
| R1 | Load reviewed files into metadata | | |
| R2 | Select plan(s) | `SelectReviewedPlanCommand` — picker NoOp | |
| R3 | Display list in XAML | **Missing UI** | |

---

## 8. DRAWINGS

| # | Capability | Result |
| --- | --- | --- |
| D1 | Load into `DrawingsPanel` VM | |
| D2 | Bound in production XAML | **Missing** |
| D3 | Add/remove commands in Inspection VM | **Missing** |

---

## 9. RICH TEXT / EDITOR

| # | Capability | Control | Result |
| --- | --- | --- | --- |
| E1 | Enter/exit edit | click / blur | |
| E2 | Color context menu (red/blue/green/gray ± bold) | `ColorText_Click` | |
| E3 | Bold / default | | |
| E4 | RecurringFailed bold-red preview | | |
| E5 | Read-only when locked | | |
| E6 | RichTextCodec round-trip | | |

---

## 10. TEMPLATE / CREATE

| # | Capability | Command | Result |
| --- | --- | --- | --- |
| T1 | Template list | `IInspectionTemplateCatalog` | |
| T2 | Refresh templates | `RefreshTemplatesCommand` | |
| T3 | Create report | `CreateReportCommand` | |
| T4 | Correct series / next ReportNumber | | |
| T5 | Template population / notes snapshot | | |

---

## 11. EXPORT / SHARE

| # | Capability | Command | Result |
| --- | --- | --- | --- |
| X1 | Validation gate disables Export | Phase 1 | PASS (Phase 1) |
| X2 | Export reaches port | `AddSiNetGoogle` replaces the SQL-only unavailable default with the native Google port | |
| X3 | Real Google Sheets artifact on #9 | | |
| X4 | Artifact URL readback | | |
| X5 | Export does **not** set SentAt / lock | | |
| X6 | Share | `ShareReportCommand` | |
| X7 | Share requires prior artifact | | |

---

## 12. LOCK / UNLOCK

| # | Capability | Command | Result |
| --- | --- | --- | --- |
| K1 | Locked UI read-only | `IsLockedAfterSend` | |
| K2 | Unlock | `UnlockReportCommand` | |
| K3 | Supported finalize→lock path (email success) | Not Export | |

---

## 13. PLANNER RESPONSE

| # | Capability | Command | Result |
| --- | --- | --- | --- |
| P1 | Indicator `HasPlannerResponse` | visual | |
| P2 | Mark response received | Stub | |
| P3 | Re-pull planner | Stub | |

---

## 14. MULTI-ROUND / RECURRING

| # | Capability | Result |
| --- | --- | --- |
| M1 | RecurringFailed status + styling | |
| M2 | Additional report in series | |
| M3 | Report cards list | |

---

## 15. TASK MODE / ROUTING

| # | Capability | Result |
| --- | --- | --- |
| TM1 | `Component.InspectionReport` → Inspection + exact report | |
| TM2 | `Component.ManagerReviewApproval` → Inspection + result picker | |
| TM3 | Complete task | `CompleteTaskCommand` | |
| TM4 | Task #300 `SendReportToPlanner` | Target: `EmailComposeToPlanner` + InspectionReport target routes to Inspection, never Email inbox | |
| TM5 | #300 opens report-compose for Report #4 | Safe certification boundary: exported artifact visible in Inspection; **STOP BEFORE SEND** — do not call `IInspectionReportEmailHost.SendReportEmailAsync` and do not complete `CommentsSentToPlanner` | |

---

## 16. Master control checklist (each once)

| UI | Command | Seam | Standalone | Result |
| --- | --- | --- | --- | --- |
| Pin | `IsPinned` | — | cosmetic | |
| Dock | `IsDocked` | — | cosmetic | |
| Refresh | `RefreshCommand` | workspace | real | |
| Collapse | `ToggleCollapseCommand` | — | real | |
| Close | code-behind | — | real | |
| Refresh templates | `RefreshTemplatesCommand` | catalog | real | |
| + new report | `CreateReportCommand` | report cmds | real | |
| Mark planner | `MarkResponseReceivedCommand` | Stub | GAP | |
| Re-pull planner | `RepullPlannerResponsesCommand` | Stub | GAP | |
| Open source | `OpenSourceReportCommand` | URN | real | |
| Unlock | `UnlockReportCommand` | report cmds | real | |
| Share | `ShareReportCommand` | export port | Native Google target | |
| Export | `ExportReportCommand` | export port | Native Google target | |
| Complete task | `CompleteTaskCommand` | task completion | real | |
| Select plan | `SelectReviewedPlanCommand` | picker | NoOp | |
| AI review | `ReviewNoteAiCommand` | Ollama | real | |
| + note toolbar | `AddNoteCommand` | — | dead | |
| + note section | `AddNoteCommand(section)` | note cmds | real | |
| Move ▲▼ | Move up/down | note cmds | real | |
| Linked open/set/clear | linked cmds | mixed | GAP/real | |
| Screenshot 📷/menus | screenshot cmds | Standalone Google/WPF target | |
| General LostFocus/toggle | code-behind | note cmds | real | |
| Rich edit + AI apply | editor | AI | real | |

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
| 2026-09-05 Phase 2 code | — | Export port real; Screenshot host real; Task #300→Inspection; offline 3721 PASS | this branch (pre-push) |
| Phase 2 live UI | #9 | AI grammar/rephrase + clipboard screenshot + Export artifact | **IN PROGRESS** — host wired; clipboard image prepared; full UI pass still required on Report #9 |
| Phase 2 Task #300 | #4 | Open Inspection via SendReportToPlanner; STOP BEFORE SEND | routing unit PASS; live compose boundary pending |

### Isolated dirty work (NOT in Inspection commits)

Leave untouched / do not stage:
- `docs/PILOT_CONTROLS.md`, `docs/TEST_STRATEGY.md`, `docs/manual-tests/STANDALONE_PILOT_SMOKE.md`
- `src/SiNet.App.Wpf.Tests/Live/PilotSmoke*`, `P0Pilot*`
- `ProposalWorkflowHarness.cs`, `IProjectTypeContinuationStarter.cs`, `ProcessBackboneServiceCollectionExtensions.cs`, `SqlProjectTypeContinuationStarter.cs`
- `tmp-e2e/`
