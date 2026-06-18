# Configuration — System Settings Catalog

**Date:** 18.06.2026  
**Status:** Active  
**Scope:** Defines the catalog of configuration parameters managed by `SystemSettingsService`.  

---

## 1. Catalog Table

All settings listed below are stored in the database `SystemSettings` table unless otherwise noted. They are office-wide configurations. 

Read access is generally available to all authenticated users. Write access is restricted to Administrators.

| Key | Display Name | Description | Storage Location | Scope / Level | Read Access | Write Access | Required / Optional | Default Value | Value Type | External Dependency | External Permission Required | Required External Role / Permission | Validation Method | Health Check Category | Health Check Severity | User Message When Missing | User Message When Permission Missing | Admin Fix Instructions | Where Used | Owner / Responsible Admin | Sensitive / Secret | Status | Notes / Known Issues |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `DefaultProjectTitle` | Default Project Title | The default title to use for new projects | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | Settings | Low | - | - | - | Core | System Admin | No | Active | - |
| `HourPriceDefault` | Default Hour Price | Default hourly rate | DB | Office | All | Admin | Optional | - | Numeric | None | No | - | TryParse | Settings | Low | - | - | - | Core | System Admin | No | Active | - |
| `InspectionTemplatesFolderId` | Inspection Templates Folder ID | Google Drive folder ID for templates | DB | Office | All | Admin | Required | - | String | Google Drive | Yes | Google Drive Reader | ID Format | Google Drive | High | "לא הוגדרה תיקיית תבניות משרדית. פנה למנהל המערכת." | "אין לך הרשאה לתיקיית התבניות ב-Google Drive. יש לבקש מהמנהל לשתף את התיקייה איתך." | "שתף את תיקיית התבניות בדרייב עם קבוצת העובדים או עם המשתמש הספציפי." | GoogleAuthService | System Admin | No | Active | Shared setting, but folder access is per Google user. |
| `InspectionReportsFolderId` | Inspection Reports Folder ID | Google Drive folder ID for reports | DB | Office | All | Admin | Required | - | String | Google Drive | Yes | Google Drive Editor | ID Format | Google Drive | High | "לא הוגדרה תיקיית דוחות משרדית. פנה למנהל המערכת." | "אין לך הרשאה לתיקיית הדוחות ב-Google Drive. יש לבקש מהמנהל לשתף את התיקייה איתך." | "שתף את תיקיית הדוחות בדרייב עם קבוצת העובדים או עם המשתמש הספציפי." | GoogleAuthService | System Admin | No | Active | Shared setting, per-user access required. |
| `ReportsOutputRoot` | Reports Output Root | Base UNC/Local path for reports | DB | Office | All | Admin | Optional | - | Path | UNC | Yes | Network Access | Path exists | Path | High | "נתיב דוחות לא מוגדר" | "אין גישה לנתיב" | "ודא נתיב רשת תקין" | Export | System Admin | No | Active | Risk if local path. |
| `InboxProjectName` | Inbox Project Name | Name of the ACC inbox project | DB | Office | All | Admin | Required | - | String | ACC | Yes | Project access | Project exists | ACC | High | "פרויקט Inbox לא מוגדר" | "אין גישה לפרויקט" | "ודא גישה לפרויקט ב-ACC" | ACC Connector | System Admin | No | Active | - |
| `InboxFolderName` | Inbox Folder Name | Name of the ACC inbox folder | DB | Office | All | Admin | Required | - | String | ACC | Yes | Folder access | Folder exists | ACC | High | "תיקיית Inbox לא מוגדרת" | "אין גישה לתיקייה" | "ודא גישה לתיקייה ב-ACC" | ACC Connector | System Admin | No | Active | - |
| `AccService.BaseUrl` | ACC Service Base URL | ACC API Base URL | DB | Office | All | Admin | Required | - | URL | ACC API | No | - | Valid URL | ACC | High | "URL לא מוגדר" | - | "הגדר URL בפורמט תקין" | ACC Connector | System Admin | No | Active | - |
| `AccProjectTemplateName` | ACC Project Template Name | Template for new ACC projects | DB | Office | All | Admin | Optional | - | String | ACC | Yes | Template access | Name exists | ACC | Medium | - | - | "ודא תבנית קיימת ב-ACC" | ACC Connector | System Admin | No | Active | - |
| `AccBootstrapAdminEmail` | ACC Bootstrap Admin Email | Email of ACC bootstrap admin | DB | Office | All | Admin | Required | - | Email | ACC | Yes | ACC Account Admin | Email format | ACC | High | "Admin Email לא מוגדר" | - | "הגדר Email תקין של מנהל ACC" | ACC Connector | System Admin | Sensitive | Active | - |
| `StatusLabel_Passed` | Status Label: Passed | Label text | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | UI | Low | - | - | - | UI | System Admin | No | Active | - |
| `StatusLabel_Failed` | Status Label: Failed | Label text | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | UI | Low | - | - | - | UI | System Admin | No | Active | - |
| `StatusLabel_RecurringFailed` | Status Label: Recurring Failed | Label text | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | UI | Low | - | - | - | UI | System Admin | No | Active | - |
| `StatusLabel_NotApplicable` | Status Label: Not Applicable | Label text | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | UI | Low | - | - | - | UI | System Admin | No | Active | - |
| `OllamaBaseUrl` | Ollama Base URL | Local Ollama API URL | DB | Office | All | Admin | Optional | - | URL | Ollama | No | - | Valid URL | AI | Medium | - | - | "הגדר URL של שרת Ollama" | AI Services | System Admin | No | Active | - |
| `OllamaModel` | Ollama Model | Default model for Ollama | DB | Office | All | Admin | Optional | - | String | Ollama | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiModel.Simple` | AI Model (Simple) | Model for simple tasks | DB | Office | All | Admin | Optional | - | String | AI Provider | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiModel.QualityCheck` | AI Model (Quality Check) | Model for QA | DB | Office | All | Admin | Optional | - | String | AI Provider | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiModel.Writing` | AI Model (Writing) | Model for text gen | DB | Office | All | Admin | Optional | - | String | AI Provider | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiModel.DeepAnalysis` | AI Model (Deep Analysis) | Model for analysis | DB | Office | All | Admin | Optional | - | String | AI Provider | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiProvider.Simple` | AI Provider (Simple) | Provider (e.g. OpenAI) | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiProvider.QualityCheck` | AI Provider (QA) | Provider | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiProvider.Writing` | AI Provider (Writing) | Provider | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiProvider.DeepAnalysis` | AI Provider (Analysis) | Provider | DB | Office | All | Admin | Optional | - | String | None | No | - | Not empty | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `AiConfiguredCloudModels` | AI Configured Cloud Models | Cloud models | DB | Office | All | Admin | Optional | - | JSON | None | No | - | Valid JSON | AI | Medium | - | - | - | AI Services | System Admin | No | Active | - |
| `StampTemplatePath` | Stamp Template Path | Path to stamp file | DB | Office | All | Admin | Optional | - | Path | UNC | Yes | Network Access | Path exists | Path | High | "קובץ חותמת לא הוגדר" | "אין גישה לקובץ חותמת" | "ודא נתיב רשת תקין" | UI | System Admin | No | Active | Risk if local path. |
| `OfficeManagementProjectId` | Office Management Project ID | Internal project ID | DB | Office | All | Admin | Required | - | String | Database | No | - | ID Format | Settings | High | "פרויקט ניהול משרד לא מוגדר" | - | - | Core | System Admin | No | Active | - |
| `AccViewerMaxTabs` | ACC Viewer Max Tabs | Tab limit | DB | Office | All | Admin | Optional | - | Numeric | None | No | - | TryParse | UI | Low | - | - | - | UI | System Admin | No | Active | - |
| `AccManualUploadAllowedExtensions` | ACC Manual Upload Extensions | Allowed file types | DB | Office | All | Admin | Optional | - | CSV | None | No | - | Valid CSV | Validation | Low | - | - | - | UI | System Admin | No | Active | - |
| `Logging.CentralLogPath` | Central Log Path | Path for central logs | DB | Office | All | Admin | Optional | - | Path | UNC | Yes | Network Access | Path exists | Path | Medium | - | - | "ודא נתיב רשת תקין" | Logging | System Admin | No | Active | Risk if local path. |
| `Logging.LocalRetentionDays` | Local Log Retention | Days to keep local logs | DB | Office | All | Admin | Optional | - | Numeric | None | No | - | TryParse | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |
| `Logging.CentralRetentionDays`| Central Log Retention | Days to keep central logs | DB | Office | All | Admin | Optional | - | Numeric | None | No | - | TryParse | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |
| `Logging.Client.FileLevel` | Client File Log Level | Log level | DB | Office | All | Admin | Optional | - | String | None | No | - | Valid Enum | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |
| `Logging.Client.CentralLevel` | Client Central Log Level | Log level | DB | Office | All | Admin | Optional | - | String | None | No | - | Valid Enum | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |
| `Logging.AccService.FileLevel`| ACC File Log Level | Log level | DB | Office | All | Admin | Optional | - | String | None | No | - | Valid Enum | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |
| `Logging.AccService.CentralLevel`| ACC Central Log Level | Log level | DB | Office | All | Admin | Optional | - | String | None | No | - | Valid Enum | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |
| `Logging.SyncEngine.FileLevel`| Sync File Log Level | Log level | DB | Office | All | Admin | Optional | - | String | None | No | - | Valid Enum | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |
| `Logging.SyncEngine.CentralLevel`| Sync Central Log Level | Log level | DB | Office | All | Admin | Optional | - | String | None | No | - | Valid Enum | Logging | Low | - | - | - | Logging | System Admin | No | Active | - |

## 2. Special Case: InspectionTemplatesFolderId

**Issue Context:** `InspectionTemplatesFolderId` is an office-wide setting stored in `SystemSettings`, but access to the actual Google Drive folder depends on the currently authenticated Google user.
Because `GoogleAuthService` stores tokens per Windows user in AppData, Administrator and regular users can be authenticated to different Google accounts.

**Documentation Rule for this Key:**
- The setting itself is shared across the office.
- The folder access is **per Google user**.
- The user must have Google Drive access to the folder.
- The health check should show a Google Drive problem if access is missing.
- The user should **not** be asked to configure the folder ID themselves.
- Admin should share the folder in Google Drive with the user or an organization group.

## 3. Recommended Health Checks

| Check Name | Trigger / When to run | What to test | Possible Statuses | User-facing message | Technical log details | Responsible Owner |
|---|---|---|---|---|---|---|
| Database Connection | App startup, System check | Can reach SiNetSQLDbContext | OK, Fail | "אין תקשורת למסד נתונים" | DB connection exception | System Admin |
| SystemSettings Availability | App startup | `SystemSettingsService.GetAllAsync` | OK, Missing | "חלק מהגדרות המערכת חסרות" | Missing mandatory keys | System Admin |
| Google OAuth Configured | App startup, System check | Check AppData token | OK, Not Configured | "יש להתחבר לחשבון Google" | OAuth token status | User |
| Google Account Connected | System check | Validate token | OK, Invalid Token | "חיבור Google פג תוקף" | Token validation result | User |
| Google Drive Template Folder | Google UI / System check | Check if folder ID is readable | OK, Missing, No Access | "אין לך גישה לתיקיית התבניות" | 404 or 403 response | Admin / User |
| Google Drive Reports Folder | Google UI / System check | Check if folder ID is writable | OK, Missing, No Access | "אין לך גישה לתיקיית הדוחות" | 404 or 403 response | Admin / User |
| ACC Service Reachable | App startup | Ping `AccService.BaseUrl` | OK, Unreachable | "שירות ACC אינו זמין" | HTTP status | System Admin |
| ACC Project Template | Project Creation | Validate `AccProjectTemplateName` | OK, Not Found | "תבנית ACC חסרה" | Template API response | System Admin |
| ACC Bootstrap Admin | ACC Provisioning | Validate `AccBootstrapAdminEmail` | OK, Invalid Email | "מנהל ACC חסר" | Validation failure | System Admin |
| File/UNC Path Settings | Feature Use / System check | `Directory.Exists` | OK, Not Found, No Access | "נתיב רשת אינו זמין" | IO Exception | System Admin |
| Central Logging Path | App startup | Write test to `CentralLogPath` | OK, Write Failed | "שגיאה ברישום מרכזי" | IO Exception | System Admin |
| AI Provider Reachable | AI Use / System check | Ping AI Base URL | OK, Unreachable | "שירות בינה מלאכותית אינו זמין"| HTTP status | System Admin |

## 4. Documentation Gaps / Implementation Gaps

- **Write Authorization:** `SystemSettingsService.SetAsync` should require Administrator role inside the service, but currently write authorization may be enforced only by UI.
- **External Health Checks:** Some settings require external permissions that are not yet natively health-checked by the app at startup.
- **Google Drive Error Precision:** Google Drive folder access failure (`403 Forbidden`) is not clearly separated from “no templates found” (`404 Not Found` or empty list).
- **Local Path Risks:** Office-wide local path settings (like `C:\Reports` instead of `\\SERVER\Reports`) can fail on user machines if they point to a local admin path not present on other machines.
- **System Status UI:** Full system status window integration is not yet implemented to show the results of the Health Checks in one place.
- **No DB schema change:** is proposed in this round for settings validation or metadata.
- **Needs Review:** Unused or deprecated parameters are included here for completeness but marked as "Candidate for future cleanup".

## Dropped / cancelled / postponed
- לא מוסיפים כרגע טבלת DB חדשה ל־metadata של פרמטרים.
- לא מוסיפים fallback לתיקיות חלופיות.
- לא מוסיפים הגדרות פר־משתמש לתיקיות משרדיות.
- לא משנים Google/ACC authorization model בסבב הזה.
- לא משנים SystemSettingsService.SetAsync בסבב התיעוד הזה.
- לא מוחקים פרמטרים גם אם הם נראים לא בשימוש.
