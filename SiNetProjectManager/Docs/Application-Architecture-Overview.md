# 📐 SiNet Project Manager — סקירת ארכיטקטורה כללית

> **גרסה:** 1.0 | **תאריך:** יוני 2026  
> **סוג מסמך:** תיעוד ארכיטקטורה ברמה גבוהה  
> **קהל יעד:** מפתחים, אנשי DevOps, מנהלי פרויקט טכניים

---

## 1. תיאור כללי

**SiNet Project Manager** הוא מערכת שולחנית (Desktop) מקיפה לניהול פרויקטי בנייה והנדסה.
המערכת בנויה על **.NET 8 + WPF** ומספקת ניהול פרויקטים מלא הכולל: משימות, החלטות, דוחות ביקורת, ניהול מיילים, אינטגרציה עם Google Workspace ו-Autodesk Construction Cloud (ACC), וסנכרון עם מערכת MasterPlan חיצונית.

**ממשק המשתמש** בעברית (RTL), מבוסס MVVM, עם הרשאות לפי תפקיד (משתמש / מנהל פרויקט / מנהל מערכת).

---

## 2. מבנה ה-Solution (6 פרויקטים)

```
SiNetProjectManager.sln
│
├── SiNetProjectManager/          ← WPF Desktop App (נקודת כניסה, UI, DI Container)
├── SiNetSQL/                     ← Core Library (Models, Services, ViewModels, Data Access)
├── SiOffice.GoogleConnector/     ← אינטגרציה עם Google APIs (Drive, Sheets, Gmail)
├── SiOffice.AutodeskConnector/   ← אינטגרציה עם Autodesk Construction Cloud (ACC/BIM360)
├── MasterPlan.SyncEngine/        ← מנוע סנכרון מול API של MasterPlan
└── SiMasterPlanWeb/              ← פרויקט Web עתידי (ריק כרגע — Class1.cs בלבד)
```

### תרשים תלויות (Dependencies)

```
SiNetProjectManager (WPF UI)
    ├── references → SiNetSQL (Core)
    ├── references → SiOffice.GoogleConnector
    ├── references → SiOffice.AutodeskConnector
    └── references → MasterPlan.SyncEngine

SiOffice.GoogleConnector
    └── references → SiNetSQL (Core) — לגישה ישירה ל-DB עבור דוחות

SiOffice.AutodeskConnector
    └── standalone (HTTP client בלבד)

MasterPlan.SyncEngine
    └── standalone (Dapper + HTTP client — DB ישיר, לא EF)
```

---

## 3. טכנולוגיות ותשתיות

| רכיב | טכנולוגיה |
|---|---|
| **Framework** | .NET 8, C# 12 |
| **UI** | WPF (Windows Presentation Foundation), RTL Hebrew |
| **Architecture** | MVVM (INotifyPropertyChanged + RelayCommand) |
| **DI** | Microsoft.Extensions.DependencyInjection |
| **ORM** | Entity Framework Core + IDbContextFactory (short-lived contexts) |
| **Database** | SQL Server (SI-WIN-2K19\SIDATA, DB: SIData) |
| **Collation** | Hebrew_100_CI_AS |
| **Compat Level** | 120 (UseCompatibilityLevel — מונע שימוש ב-OPENJSON) |
| **Logging** | Serilog (Async file sink, 5MB rolling, 7 ימים) |
| **Auth** | Windows Authentication → טבלת SIUser |
| **Secrets** | Windows Credential Manager (SiNet/ConnectionStrings/SiNetDatabase) |
| **PDF Rendering** | WebView2 (המרת גוף מייל HTML ל-PDF) |
| **Google APIs** | Drive API, Sheets API, Gmail API (OAuth2) |
| **ACC/BIM360** | Autodesk Construction Cloud REST API (2-legged/3-legged OAuth) |
| **Migrations** | EF Migration Bundle (אין auto-migrate — פריסה ידנית) |

---

## 4. ארכיטקטורת ההפעלה (Startup Flow)

סדר ההפעלה ב-`App.xaml.cs`:

```
1. Static Constructor
   └── Serilog Configuration (file sink → Logs/SiNet-*.log)

2. ConfigureServices() — רישום DI
   ├── DbContextFactory<SiNetSQLDbContext> (Singleton)
   ├── DialogService (Singleton)
   ├── EmailIngestionServiceFactory (Singleton)
   ├── WebView2PdfRenderer (Singleton)
   ├── StatusColorService (Singleton)
   ├── SystemSettingsService (Singleton)
   ├── SiUserService, AccUserBootstrapService (Transient)
   └── 12 ViewModels (Transient)

3. OnStartup() — סדר הפעלה
   ├── 3a. Load AppSettings (appsettings.json)
   ├── 3b. Credential Vault (Windows Credential Manager)
   │       └── אם אין → SecretSetupWindow (provisioning)
   ├── 3c. Connection Gate (בדיקת חיבור DB עם retry loop)
   ├── 3d. AppLogger Configuration
   ├── 3e. ManagementSettings (הגדרות ניהול)
   ├── 3f. Build DI Container (ServiceProvider)
   ├── 3g. Legacy Locator Wiring (backward compat)
   ├── 3h. PDF Renderer Initialization (WebView2)
   ├── 3i. ACC User Bootstrap (רקע — Task.Run)
   ├── 3j. DB Validation (Schema + Seed + Default Project)
   ├── 3k. User Authorization (CurrentUserContext.Initialize)
   └── 3l. Show MainWindow
```

---

## 5. דפוסי ארכיטקטורה (Architecture Patterns)

### 5.1 MVVM
- כל ה-**ViewModels** נמצאים ב-`SiNetSQL.MVVM` (ספריית Core)
- ה-**Views** (XAML + code-behind) נמצאים ב-`SiNetProjectManager`
- קישור דרך `DataContext` (חלקם ב-DI, חלקם ב-code-behind)
- `RelayCommand` / `AsyncRelayCommand` לפקודות

### 5.2 Project-Centric Navigation
- **`ActiveProjectContext`** — Singleton שמנהל את הפרויקט הנבחר
- כל החלונות/פאנלים עובדים על הפרויקט הפעיל
- שינוי פרויקט → רענון כל ה-Views

### 5.3 IDbContextFactory Pattern
- **לא** `DbContext` singleton — אלא `IDbContextFactory<SiNetSQLDbContext>`
- כל פעולה יוצרת `using var ctx = _contextFactory.CreateDbContext()`
- מונע בעיות concurrency ו-tracking

### 5.4 Windows Authentication
- **`CurrentUserContext`** — Singleton שממפה את המשתמש הנוכחי
- `Environment.UserName` → חיפוש בטבלת `Siusers` לפי `LoginName`
- `IsDomainGroup = true` → הרשאות מנהל
- `IsActive = false` → חסום מכניסה

### 5.5 Role-Based UI
- שלוש רמות הרשאה:
  - **User** (`IsInUsers`) — צפייה ועבודה בסיסית
  - **Project Manager** (`IsInProjectManagers`) — ניהול פרויקטים
  - **Admin** (`IsInAdmins`) — הגדרות מערכת, משתמשים, כלי ניהול
- תפריטים ב-MainWindow מוסתרים/מוצגים לפי `Visibility="{Binding IsInAdmins}"`

---

## 6. מבנה מסד הנתונים (~55 טבלאות)

### 6.1 תרשים תחומים (Domain Groups)

```
┌──────────────────────────────────────────────────────────────┐
│                    SIData Database                            │
│                                                              │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────────┐   │
│  │   Core       │  │  Tasks       │  │  Decisions        │   │
│  │─────────────│  │──────────────│  │───────────────────│   │
│  │ Projects     │  │ ProjectAs-   │  │ ProjectDecisions  │   │
│  │ Companies    │  │  signments   │  │ DecisionCategories│   │
│  │ Contacts     │  │ TaskTypes    │  │ DecisionHistories │   │
│  │ Places       │  │ TaskLinks    │  │                   │   │
│  │ Siusers      │  │ StatusMaps   │  └───────────────────┘   │
│  │ Properties   │  │ Events       │                          │
│  │ ServiceProv. │  │ Priorities   │  ┌───────────────────┐   │
│  └─────────────┘  └──────────────┘  │  Email Inbox      │   │
│                                      │───────────────────│   │
│  ┌─────────────┐  ┌──────────────┐  │ EmailInboxMessages│   │
│  │ Inspection   │  │  ACC/Cloud   │  │ EmailInbox-       │   │
│  │─────────────│  │──────────────│  │  Attachments      │   │
│  │ Inspection-  │  │ AccHubs      │  │ ThreadStatus-     │   │
│  │  Reports     │  │ AccSystem-   │  │  Mappings         │   │
│  │ Inspection-  │  │  Resources   │  └───────────────────┘   │
│  │  Series      │  │ ProjectAcc-  │                          │
│  │ Chapters     │  │  Mappings    │  ┌───────────────────┐   │
│  │ Sections     │  └──────────────┘  │  Settings/System  │   │
│  │ Notes        │                     │───────────────────│   │
│  │ CommentsBank │  ┌──────────────┐  │ SystemSettings    │   │
│  │ ChapterNames │  │  Financial   │  │ UserSettings      │   │
│  │ SectionNames │  │──────────────│  │ UserStatusPrefs   │   │
│  │ NoteStatuses │  │ Banks        │  │ AppUserRoles      │   │
│  └─────────────┘  │ Bills/Bids   │  │ SyncRunFailures   │   │
│                    │ Contracts    │  └───────────────────┘   │
│  ┌─────────────┐  │ Payments     │                          │
│  │  Files       │  │ Mifrat       │  ┌───────────────────┐   │
│  │─────────────│  └──────────────┘  │  Legacy/Other     │   │
│  │ ProjectFiles │                     │───────────────────│   │
│  │ ProjectFile- │  ┌──────────────┐  │ Announcements     │   │
│  │  Refs        │  │  Planning    │  │ DrawingTypes      │   │
│  │ ProjectFold- │  │──────────────│  │ Layers            │   │
│  │  ers         │  │ Planners     │  │ LayerObjectTypes  │   │
│  └─────────────┘  │ WeekWorks    │  │ MavatBlocks       │   │
│                    │ WorkHours    │  │ TabaData          │   │
│                    │ MavatBlocks  │  │ JobTitles/Types   │   │
│                    └──────────────┘  └───────────────────┘   │
└──────────────────────────────────────────────────────────────┘
```

### 6.2 ישויות ליבה (Core Entities)

| טבלה | תפקיד |
|---|---|
| **Projects** | ישות מרכזית — כל מודול מתקשר אליה (FK). כולל: Title, Number, Company, Place, Status, ProjectPath, Author/Editor audit |
| **Siusers** | משתמשי מערכת — מזוהים דרך Windows Auth. כולל: LoginName, Sid, Email, IsDomainGroup (admin), IsActive |
| **Companies** | חברות/קבלנים — FK מ-Projects |
| **Contacts** | אנשי קשר — FK מ-Projects |
| **Places** | מיקומים/אתרים |
| **Properties** | נכסים |
| **ServiceProviders** | ספקי שירות |

### 6.3 תת-מערכות עיקריות

#### 🔧 מערכת משימות (Tasks)
| טבלה | תפקיד |
|---|---|
| ProjectAssignments | משימות ליבה — FK ל-Project, Handler, Status, TaskType |
| ProjectAssignmentStatuses | סטטוסים (פתוח/בטיפול/סגור...) |
| ProjectAssignmentEvents | מעקב שינויים (audit trail) |
| TaskTypes | סוגי משימה |
| TaskLinks | קישורים בין משימות (parent-child, related) |
| TaskStatusToProjectStatusMappings | מיפוי סטטוס משימה ← סטטוס פרויקט |
| UserStatusPreferences | העדפות סטטוס למשתמש |
| ProjectTypeTaskTypes | חוקי סוג משימה לפי סוג פרויקט |
| ProjectTypeStatuses | סטטוסים מותאמים לפי סוג פרויקט |

#### 📋 מערכת החלטות (Decisions)
| טבלה | תפקיד |
|---|---|
| ProjectDecisions | החלטות ועדה/ניהוליות — FK ל-Project, Category |
| DecisionCategories | קטגוריות (ועדת תכנון, ועדת בנייה...) |
| DecisionHistories | היסטוריית שינויים בהחלטות |

#### 📧 מערכת מיילים (Email Inbox)
| טבלה | תפקיד |
|---|---|
| EmailInboxMessages | הודעות מייל שנקלטו מ-Gmail API |
| EmailInboxAttachments | קבצים מצורפים — עם תמיכה בהעלאה ל-ACC |
| ThreadStatusMappings | מעקב סטטוס Thread (קריאה/טיפול) |

#### 🔍 מערכת ביקורת (Inspection)
| טבלה | תפקיד |
|---|---|
| InspectionSeries | סדרות ביקורת — FK ל-Project (סדרה = מספר דוחות) |
| InspectionReports | דוחות בודדים — FK ל-Series, Project |
| Chapters / ChapterNames | פרקים בדוח (מילון ראשי + instances) |
| Sections / SectionNames | סעיפים בתוך פרקים |
| InspectionNotes | הערות ביקורת בסעיף |
| InspectionNoteStatuses | סטטוסי הערה (פתוח/תוקן/חוזר...) |
| CommentsBank | בנק הערות לשימוש חוזר |

#### ☁️ אינטגרציה ACC (Autodesk Cloud)
| טבלה | תפקיד |
|---|---|
| AccHubs | Hubs ב-Autodesk (ארגונים) |
| AccSystemResources | משאבי מערכת (templates, תיקיות בסיס) |
| ProjectAccMappings | מיפוי Project ← ACC Project |

#### 💰 מערכת פיננסית
| טבלה | תפקיד |
|---|---|
| Banks / BankProjects | בנקים ומיפוי לפרויקטים |
| Bids / ProjectBids | מכרזים |
| Bills / ProjectBills | חשבונות |
| Contracts / ProjectContracts / ProjectDeffContracts | חוזים |
| PaymentsSteps | שלבי תשלום |
| Mifrat | מפרטים |

#### 📂 מערכת קבצים
| טבלה | תפקיד |
|---|---|
| ProjectFiles | קבצי פרויקט (מטאדאטה) |
| ProjectFileRefs | הפניות לקבצים |
| ProjectFolders | מבנה תיקיות וירטואלי |

#### ⚙️ הגדרות ומערכת
| טבלה | תפקיד |
|---|---|
| SystemSettings | הגדרות מערכת (key-value) |
| UserSettings | הגדרות משתמש |
| AppUserRoles | תפקידי משתמש |
| SyncRunFailures | לוג כשלונות סנכרון |

---

## 7. שכבת שירותים (Services Layer) — SiNetSQL

### 7.1 שירותי ליבה

| שירות | תפקיד |
|---|---|
| **TaskService** | CRUD משימות, חיפוש, סינון, מעקב אירועים |
| **TaskPriorityEngine** | חישוב ומיון עדיפויות משימות |
| **WorkPriorityService** | סדרי עדיפויות ברמת "בעבודה" |
| **StatusMappingService** | מיפוי סטטוס משימה ← סטטוס פרויקט |
| **TaskManagementSeedService** | Seed data לסטטוסים וסוגי משימה |
| **ProjectDecisionService** | CRUD החלטות + היסטוריה |
| **ProjectRenameService** | שינוי שם פרויקט (DB + תיקיות) |
| **ProjectFolderCreator** | יצירת מבנה תיקיות לפרויקט חדש |
| **ProjectNameValidator** | ולידציית שם פרויקט |
| **ProjectTypeRuleService** | חוקי סוג פרויקט (מה מותר/חובה) |
| **DefaultProjectService** | וידוא שקיים פרויקט ברירת מחדל |
| **StatusColorService** | צבעי סטטוס (UI) — Singleton |
| **SystemSettingsService** | גישה להגדרות מערכת (key-value) — Singleton |
| **UserSettingService** | הגדרות משתמש מותאמות |

### 7.2 שירותי מיילים

| שירות | תפקיד |
|---|---|
| **EmailIngestionService** (~63K) | קליטת מיילים מ-Gmail API — scan, parse, save |
| **EmailIngestionServiceFactory** | Factory שיוצר instances של EmailIngestionService |
| **AttachmentTaggingService** | תיוג אוטומטי של קבצים מצורפים |
| **MessageKeyGenerator** | יצירת מפתח ייחודי למיילים (dedup) |
| **EmailContextService** | הקשר מייל (פרויקט, שרשור) |

### 7.3 שירותי ביקורת

| שירות | תפקיד |
|---|---|
| **TemplateSyncService** | סנכרון תבנית Google Sheet ← DB |
| **InspectionReportService** | CRUD דוחות ביקורת |
| **TemplateTagValidator** | ולידציית תגיות בתבנית |
| **TemplateHelpValidator** | עזר ולידציה נוספת |
| **RichTextCodec** | קידוד/פענוח טקסט עשיר (RTF ↔ DB) |

### 7.4 שירותי ACC

| שירות | תפקיד |
|---|---|
| **AccUserBootstrapService** | Provisioning משתמשים ל-ACC |
| **AccBootstrapService** | אתחול Hub/Resources ב-ACC |

### 7.5 שירותי תשתית

| שירות | תפקיד |
|---|---|
| **AppLogger** | Logging מרכזי (Serilog wrapper) |
| **CurrentUserContext** | זיהוי המשתמש הנוכחי (Windows Auth → DB) |
| **CurrentUserProvider** | Provider pattern למשתמש |
| **ActiveProjectContext** | Singleton — הפרויקט הפעיל כרגע |
| **CredentialProvider** | גישה ל-Connection String מ-Credential Vault |
| **DatabaseSchemaValidator** | בדיקת תקינות סכמה |
| **MigrationPreflightChecker** | בדיקה לפני הרצת migration |
| **BaselineSnapshotService** | Snapshot בסיסי של ה-DB |

### 7.6 שירותי ייבוא (Task Import)

| שירות | תפקיד |
|---|---|
| **TaskImportService** | ייבוא משימות מ-TSV (Excel/Sheets paste) |
| **TaskImportRow** | שורת ייבוא בודדת (DTO) |

---

## 8. שכבת ViewModels — SiNetSQL.MVVM

| ViewModel | View/Window | תפקיד |
|---|---|---|
| **MainWindowViewModel** | MainWindow | תפריט ראשי, הרשאות, ניווט |
| **TaskPanelViewModel** | TaskPanelView | פאנל משימות (רשימה, סינון, CRUD) |
| **FloatingProjectTasksViewModel** | FloatingProjectTasksView | משימות צפות לפרויקט הפעיל |
| **FloatingInspectionViewModel** (~83K) | FloatingInspectionView | עץ ביקורת אינטראקטיבי |
| **TaskImportViewModel** | TaskImportWindow | ייבוא TSV + מיפוי סטטוסים |
| **EmailManagementViewModel** (~150K) | EmailManagementView | ניהול מיילים מלא |
| **ProjectDecisionsViewModel** | ProjectDecisionsWindow | ניהול החלטות |
| **CreateProjectViewModel** | CreateProjectUserControl | יצירת פרויקט חדש |
| **EditProjectViewModel** | WindowEditProject | עריכת פרויקט |
| **FileManagerViewModel** | FileManagerView | ניהול קבצים |
| **ProjectFolderTreeViewModel** | ProjectFolderTreeView | עץ תיקיות |
| **UserManagementViewModel** | UserManagementWindow | ניהול משתמשים |
| **AddUserViewModel** | AddUserWindow | הוספת משתמש |
| **CompanyViewModel** | WindowCompany | ניהול חברות |
| **ProjectTypeRulesViewModel** | ProjectTypeRulesWindow | חוקי סוג פרויקט |
| **StatusMappingViewModel** | StatusMappingWindow | מיפוי סטטוסים |
| **MasterPlanMappingViewModel** | MasterPlanMappingWindow | מיפוי MasterPlan ↔ SiNet |
| **ProjectWorkViewModel** | ProjectWorkView | מסך "בעבודה" |
| **R01/R02/R03ReportViewModel** | R01/R02/R03ReportDialog | הפקת דוחות Google Sheets |

---

## 9. שכבת UI — SiNetProjectManager

### 9.1 חלון ראשי (MainWindow)
- תפריט עליון מבוסס הרשאות (Admin/ProjectManager/User)
- Content area עם UserControls מתחלפים
- `BaseWindow` — מחלקת בסיס עם תמיכה ב-theme/background

### 9.2 UserControls (פאנלים)

| Control | תפקיד |
|---|---|
| **TaskPanelView** | רשימת משימות + סינון + CRUD |
| **FloatingProjectTasksView** | משימות צפות |
| **FloatingInspectionView** | עץ ביקורת + עריכת הערות |
| **EmailManagementView** | תיבת דואר מלאה |
| **ProjectWorkView** | מסך "בעבודה" |
| **FileManagerView** | ניהול קבצים |
| **ProjectFolderTreeView** | עץ תיקיות |
| **CreateProjectUserControl** | טופס יצירת פרויקט |
| **WindowEditProject** | טופס עריכת פרויקט |
| **RichTextNoteEditor** | עורך טקסט עשיר (הערות ביקורת) |
| **SearchableProjectSelector** | בחירת פרויקט עם חיפוש |

### 9.3 חלונות דו-שיח (Dialogs & Windows)

| Dialog/Window | תפקיד |
|---|---|
| **TaskImportWindow** | ייבוא משימות מ-TSV |
| **AddUserWindow** | הוספת משתמש |
| **UserManagementWindow** | ניהול משתמשים |
| **ProjectDecisionsWindow** | ניהול החלטות |
| **ProjectTypeRulesWindow** | חוקי סוג פרויקט |
| **StatusMappingWindow** | מיפוי סטטוסים |
| **MasterPlanMappingWindow** | מיפוי MasterPlan |
| **SettingsWindow** | הגדרות משתמש |
| **ManagementSettingsWindow** | הגדרות ניהול (Admin) |
| **R01/R02/R03ReportDialog** | הפקת דוחות |
| **InspectionHelpWindow** | עזרה לביקורת |
| **MigrationPocWindow** | PoC חילוץ דוחות |
| **SecretSetupWindow** | הגדרת credentials ראשונית |
| **ProvisioningPasswordDialog** | סיסמת provisioning |
| **ActionProofDialog** | הוכחת ביצוע (משימות) |
| **SyncFailuresWindow** | צפייה בכשלונות סנכרון |
| **ExternalBrowserWindow** | דפדפן חיצוני (WebView2) |
| **BackgroundUploadsDialog** | העלאות ברקע |
| **DownloadAssociationDialog** | שיוך הורדות |
| **RenameProjectWindow** | שינוי שם פרויקט |
| **AlternativeNameWindow** | שם חלופי |
| **WindowCompany / WindowPlace** | ניהול חברות/מיקומים |

### 9.4 Converters

| Converter | תפקיד |
|---|---|
| BoolToOpenClosedConverter | bool ← "פתוח"/"סגור" |
| ColorConverter | המרות צבע |
| HexToColorConverter | Hex string → Color |
| FontSizePercentageConverter | גודל פונט יחסי |
| InverseBooleanConverter | הפיכת bool |
| NotNullToVisibilityConverter | null → Collapsed |
| StatusIdToColorConverter | סטטוס → צבע |
| SelfConverter | החזרת הערך עצמו |
| CompositeChildrenConverter | איחוד אוספי ילדים |

### 9.5 שירותי WPF (SiNetProjectManager\Services)

| שירות | תפקיד |
|---|---|
| **AppConfiguration** | קריאת appsettings.json |
| **CredentialVaultService** | Windows Credential Manager wrapper |
| **SecretProvisioningService** | ייבוא credentials מקובץ מוצפן |
| **ActiveDirectoryService** | שאילתות Active Directory |
| **WebView2PdfRenderer** | WebView2 → PDF |
| **GoogleInspectionTemplateProvider** | מימוש IInspectionTemplateProvider (Google Sheets) |
| **GoogleReportExportService** | מימוש IReportExportService (Google Sheets) |
| **CustomProtocolRegistrar** | רישום protocol handler (sioffice://) |
| **OAuthCallbackPipe / OAuthLoopbackListener** | OAuth2 callback handling |
| **AppLoggerReportAdapter** | מתאם logging לדוחות |

### 9.6 שירותי Migration (חילוץ נתונים)

| שירות | תפקיד |
|---|---|
| **IndexSheetReader** | קריאת גיליון אינדקס (Google Sheet) |
| **ReportContentExtractor** | חילוץ תוכן מדוחות |
| **GeminiExtractionService** | חילוץ באמצעות Gemini AI |
| **ExtractionCacheService** | מטמון לחילוצים |
| **NoteSplitter** | פיצול הערות |
| **MigrationTaskService** | ניהול משימות migration |

---

## 10. אינטגרציות חיצוניות

### 10.1 Google Workspace (SiOffice.GoogleConnector)

```
SiOffice.GoogleConnector/
├── GoogleService.cs              ← Entry point (OAuth2 init, service creation)
├── InlineImageProvider.cs        ← תמונות inline במיילים
├── ProjectModel.cs               ← מודל פרויקט Google
├── Reports/
│   ├── GoogleAuthService.cs      ← OAuth2 authentication
│   ├── GoogleDriveService.cs     ← Drive API (קבצים, תיקיות)
│   ├── GoogleSheetsService.cs    ← Sheets API (קריאה/כתיבה)
│   ├── R01ReportService.cs       ← דוח R01 (סטטוס פרויקט)
│   ├── R02ReportService.cs       ← דוח R02 (מעקב תקציב)
│   ├── R03ReportService.cs       ← דוח R03 (יומי)
│   ├── Data/
│   │   ├── IR01/IR02/IR03Repository.cs        ← interfaces
│   │   ├── ReplicaR01/R02/R03Repository.cs    ← Replica DB queries
│   │   ├── MasterPlanR01/R02Repository.cs     ← MasterPlan data source
│   │   ├── R02DataMerger.cs                   ← מיזוג מקורות נתונים
│   │   └── DataSourceResolver.cs              ← בחירת מקור נתונים
│   └── Models/
│       ├── R01DataRow / R01ReportRequest.cs
│       ├── R02DataRow / R02ReportRequest.cs
│       ├── R03DailyRow / R03ReportRequest.cs
│       ├── ReportGenerationResult.cs
│       └── DataSourceDecision.cs
├── RateLimiting/                 ← מגבלות Gmail API quota
│   ├── GmailRateLimiter.cs
│   ├── GmailThrottleService.cs
│   ├── GmailMetricsCollector.cs
│   ├── GmailQuotaConstants.cs
│   └── ...
└── Logging/
    ├── IReportLogger.cs
    └── R02FileLogger.cs
```

**3 סוגי דוחות:**
- **R01** — דוח סטטוס פרויקט (הפקה ל-Google Sheet)
- **R02** — דוח מעקב תקציב
- **R03** — דוח יומי

### 10.2 Autodesk Construction Cloud (SiOffice.AutodeskConnector)

```
SiOffice.AutodeskConnector/
├── Bim360Service.cs        ← שירות מרכזי: העלאה, תיקיות, משתמשים, markups
├── AuthService.cs          ← OAuth2 (2-legged + 3-legged)
├── TokenProvider.cs        ← ניהול tokens
├── ITokenProvider.cs       ← interface
├── HubInfo.cs              ← מודל Hub (ארגון)
├── UploadResult.cs         ← תוצאת העלאה
├── DocsProbeResult.cs      ← בדיקת זמינות docs
└── ProjectMemberModels.cs  ← מודלי חברות בפרויקט
```

**יכולות:**
- העלאת קבצים ל-ACC (כולל קבצים מצורפים ממיילים)
- יצירת תיקיות
- ניהול משתמשים (provisioning)
- קריאת markups

### 10.3 MasterPlan Sync (MasterPlan.SyncEngine)

```
MasterPlan.SyncEngine/
├── Program.cs                    ← Entry point (Console app)
├── MasterPlanApiClient.cs        ← HTTP client ל-MasterPlan API
├── ApiDailySyncService.cs        ← סנכרון יומי מ-API
├── OfflineDailySyncService.cs    ← סנכרון מקובצי NDJSON (offline)
├── OfflineApiSimulator.cs        ← סימולטור API
├── DatabaseSyncManager.cs        ← ניהול סנכרון ← SQL Server
├── RawCaptureService.cs          ← לכידת נתונים גולמיים
├── MonthlyBackupRestoreService.cs ← גיבוי/שחזור חודשי
├── HoursNormalization.cs         ← נרמול שעות
├── MasterPlanApiException.cs     ← חריגות API
├── Models/
│   ├── ApiEntities.cs            ← מודלים: Projects, Tasks, Bids, Bills, Companies...
│   └── SqlDateTimeHandler.cs
├── Migrations/
│   └── 001_AddHoursEndpointTables.sql
└── Scripts/
    └── CreateReplicaTables.sql   ← טבלאות Replica ב-SQL
```

**יכולות:**
- סנכרון נתונים מ-MasterPlan API (פרויקטים, משימות, מכרזים, חשבונות, חברות, עובדים, שעות)
- שמירה ב-**Replica Tables** (Dapper — לא EF)
- מצב Offline עם קבצי NDJSON
- גיבוי/שחזור חודשי

---

## 11. זרימות עבודה עיקריות (Key Workflows)

### 11.1 עבודה יומית — משימות
```
משתמש → TaskPanelView → בוחר פרויקט → רואה משימות פתוחות
    → יוצר/עורך משימה → TaskService.CreateAsync / UpdateAsync
    → TaskPriorityEngine מחשב עדיפויות
    → ProjectAssignmentEvent נוצר (audit trail)
    → StatusMappingService מעדכן סטטוס פרויקט אם נדרש
```

### 11.2 קליטת מיילים
```
EmailManagementView → לחיצה "סנכרון"
    → EmailIngestionService.ScanInboxAsync (Gmail API)
    → מזהה מיילים חדשים (MessageKeyGenerator → dedup)
    → שומר EmailInboxMessage + EmailInboxAttachments
    → AttachmentTaggingService מתייג קבצים
    → משתמש יכול להעלות קבצים ל-ACC (Bim360Service.Upload)
```

### 11.3 דוחות ביקורת
```
FloatingInspectionView → בוחר סדרה → בוחר דוח
    → TemplateSyncService מסנכרן תבנית מ-Google Sheets
    → משתמש עורך הערות (RichTextNoteEditor)
    → InspectionReportService שומר
    → GoogleReportExportService מייצא ל-Google Sheet
```

### 11.4 הפקת דוחות
```
R01/R02/R03ReportDialog → משתמש בוחר פרמטרים
    → R0xReportService שולף נתונים (ReplicaRepository / MasterPlanRepository)
    → DataSourceResolver בוחר מקור נתונים
    → GoogleSheetsService כותב ל-Google Sheet
```

### 11.5 ייבוא משימות
```
TaskImportWindow → הדבקת TSV
    → Preview (parsing + validation)
    → מיפוי סטטוסים (Task/Decision)
    → Apply Mapping (color-coding)
    → Commit → TaskImportService שומר ל-DB
```

---

## 12. אבטחה

| שכבה | מנגנון |
|---|---|
| **Authentication** | Windows Authentication (אוטומטי — Environment.UserName) |
| **Authorization** | תפקידים ב-DB (IsInUsers, IsInProjectManagers, IsInAdmins) |
| **Secrets** | Windows Credential Manager (Connection String מוצפן) |
| **Provisioning** | קובץ מוצפן + סיסמה ← SecretProvisioningService |
| **DB Access** | IDbContextFactory (short-lived) — אין connection string ב-appsettings |
| **Admin Flag** | `Siuser.IsDomainGroup = true` → הרשאות ניהול מלאות |
| **Soft Delete** | `Siuser.IsActive = false` → חסימת כניסה |

---

## 13. Logging & Monitoring

| רכיב | פירוט |
|---|---|
| **Serilog** | Async File Sink → `Logs/SiNet-{Date}.log` |
| **Rolling** | 5MB per file, 7 ימים שמירה |
| **AppLogger** | Wrapper מרכזי עם context (User, Project, Operation) |
| **SyncRunFailures** | טבלת DB ללוג כשלונות סנכרון |
| **Gmail Metrics** | GmailMetricsCollector — מעקב quota usage |

---

## 14. Configuration

| קובץ | תפקיד |
|---|---|
| `appsettings.json` | הגדרות אפליקציה (Google credentials path, ACC settings, logging) |
| `AppSettings.cs` | מודל strongly-typed ל-appsettings |
| `ManagementSettings.cs` | הגדרות ניהול (paths, defaults) |
| `SettingsManager.cs` | ניהול הגדרות מרכזי |
| **SystemSettings** (DB) | הגדרות מערכת דינמיות (key-value ב-DB) |
| **UserSettings** (DB) | הגדרות משתמש מותאמות |
| **Credential Vault** | Connection String (Windows Credential Manager) |

---

## 15. סיכום ארכיטקטורה

```
┌─────────────────────────────────────────────────────────┐
│                    WPF UI Layer                          │
│  MainWindow → UserControls → Dialogs → Converters       │
│  (SiNetProjectManager project)                          │
├─────────────────────────────────────────────────────────┤
│                   ViewModel Layer                        │
│  MVVM: INotifyPropertyChanged + RelayCommand             │
│  (SiNetSQL.MVVM namespace)                              │
├─────────────────────────────────────────────────────────┤
│                   Service Layer                          │
│  Business Logic: TaskService, EmailIngestion,            │
│  InspectionReport, ProjectDecision, etc.                 │
│  (SiNetSQL.Services namespace)                          │
├─────────────────────────────────────────────────────────┤
│                   Data Access Layer                      │
│  EF Core: SiNetSQLDbContext + IDbContextFactory           │
│  Fluent API Configurations                               │
│  (SiNetSQL.Data namespace)                              │
├─────────────────────────────────────────────────────────┤
│                   External Integrations                   │
│  Google (Drive/Sheets/Gmail) │ ACC (BIM360) │ MasterPlan │
│  (Separate projects)                                     │
├─────────────────────────────────────────────────────────┤
│                   Infrastructure                         │
│  SQL Server │ Windows Auth │ Credential Vault │ Serilog  │
└─────────────────────────────────────────────────────────┘
```

**סך הכל:**
- **~55 טבלאות** ב-DB מחולקות ל-10 תחומים
- **~40 שירותים** בשכבת Services
- **~20 ViewModels** בשכבת MVVM
- **~25 חלונות/דיאלוגים** בשכבת UI
- **~10 UserControls** לפאנלים
- **3 אינטגרציות חיצוניות** (Google, ACC, MasterPlan)
- **6 פרויקטים** ב-Solution
