# Inspection report validation & export gate

> **Status:** Target (2026-09-05)  
> **Hosts:** Standalone `SiNet.App.Wpf` (New System)

## Export gate

`Questionnaire.CanExport` is true only when:

1. Every Chapter-0 **general field** has a non-blank displayed value.
2. Every numbered questionnaire note has a status, and if status ≠ `NotApplicable` then text is non-blank.
3. No numbered note is in `ManagerReview`.

The Export command (`📤`) must be **disabled** while `CanExport` is false (tooltip = `ValidationSummary`). Clicking Export when valid reaches `IInspectionReportExportPort.ExportAsync` (standalone host currently binds `UnavailableInspectionReportExportPort`).

## General auto-fill

Labels in `InspectionQuestionnaireRules.AutoFieldLabels` are filled from project/report context:

| Label | Source |
|---|---|
| שם פרויקט / מספר פרויקט | Current project |
| ישוב / רשות מקומית | Project place |
| תאריך / Today | Report inspection date |
| ממלא דוח / User | Report inspector name |
| מספר דוח | Report number |
| כתובת מייל / Email | **Inspector `SIUser.Email`** via report `InspectorId` (parity with V2). Empty only when no inspector or no email on the user row — not hard-coded blank. |

Manual override remains available for auto fields when the operator must correct a value.
