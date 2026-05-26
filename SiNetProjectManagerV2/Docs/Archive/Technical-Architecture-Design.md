# 🏗️ SiNet Project Manager — Technical Architecture Design

> **גרסה:** 1.1 | **תאריך:** יוני 2026  
> **סוג מסמך:** אפיון טכני ארכיטקטוני  
> **קהל יעד:** מפתחים, ארכיטקטים, מנהלי פרויקט טכניים  
> **סטטוס:** מתואם לארכיטקטורה הקנונית הנוכחית

---

## 🔄 עדכון אלינמנט (Workflow Canonical Model)

מסמך זה מתואם למצב הנוכחי של ה-codebase לאחר ניקוי הזרם הישן והמעבר ל-PlanningWorkflow:

- **WorkflowDefinition קנוני יחיד:** `PlanningWorkflow` עם שלבי `PLN.*`. הזרמים הישנים
  (Design / Review / Opinion / Intake / ScopeExpansion) הוסרו.
- **TFM:** הפרויקט עבר ל-.NET 10. כל הפניה ל-.NET 8 במסמך זה תקפה כהקשר היסטורי בלבד.
- **ProjectStatus היחיד שעוקבים אחריו:** Lead/QuotePreparation/.../Active/.../Closed.
  אין שימוש ב-`Completed`. שינויי סטטוס נעשים אך ורק דרך פעולות מעבר במנוע
  (`SetProjectStatus`, `SetBillingPending`, `CloseProject`).
- **TaskResult ולא TaskStatus:** משימות נסגרות עם `RecordTaskResult` (CompletedSuccess /
  AuthorityApproved / וכו'). אין מיפוי גלובלי של "סטטוס משימה" כסטטוס פרויקט.
- **פעולות מעבר נתמכות (WorkflowTransitionActionType):**
  `SetProjectStatus`, `RecordTaskResult`, `SetBillingPending`, `CloseProject`, וכו'.
- **Per-ProjectType policy:** מופעל דרך seed (`SeedProjectTypeWorkflowStagesAsync`,
  `SeedProjectTypeDisciplinesAsync`) **וגם** דרך UI ב-`WorkflowManagementWindow → Policy`
  עם 3 לשוניות: תהליכים מותרים / שלבים פעילים / תחומים פעילים.
- **WorkflowManagementWindow** הוא ה-hub היחיד לניהול תהליכים (Builder / Designer /
  Policy / Dashboard / Behaviors / Help). חלונות `WorkflowBuilderWindow` ו-
  `WorkflowPolicyWindow` הוסרו כיתירים.

---

## תוכן עניינים

1. [Current State — מצב קיים](#1-current-state--מצב-קיים)
2. [Target State — מצב יעד](#2-target-state--מצב-יעד)
3. [Architecture — ארכיטקטורה](#3-architecture--ארכיטקטורה)
4. [Domain Model — מודל תחום](#4-domain-model--מודל-תחום)
5. [Application Layer — שכבת אפליקציה](#5-application-layer--שכבת-אפליקציה)
6. [Infrastructure — תשתית](#6-infrastructure--תשתית)
7. [UI — ממשק משתמש](#7-ui--ממשק-משתמש)
8. [Development Breakdown — פירוט פיתוח](#8-development-breakdown--פירוט-פיתוח)
9. [Technical Roadmap — מפת דרכים](#9-technical-roadmap--מפת-דרכים)

---

## 1. Current State — מצב קיים

### 1.1 סקירה כללית

**SiNet Project Manager** היא מערכת שולחנית (.NET 8 / WPF) לניהול פרויקטי בנייה והנדסה.
המערכת כוללת כיום מודולים עובדים עם יכולות מגוונות, אך **חסרים בה שלושה מרכיבים מפתח**:
מנוע Workflow, מנוע ניתוח הקשר מייל (Email Context Engine), ומנוע הצעות פעולה (Suggested Actions).

### 1.2 מודולים קיימים ופעילים

| מודול | סטטוס | יכולות עיקריות |
|---|---|---|
| **Project Core** | ✅ פעיל | CRUD פרויקטים, סטטוסים, סוגי פרויקט (M:N), שיוך חברות/אנשי קשר/מקומות |
| **Task Management** | ✅ פעיל | CRUD משימות, עדיפויות (TaskPriorityEngine), אירועים (audit trail), מיפוי סטטוסים, ייבוא TSV |
| **Email Inbox** | ✅ פעיל | קליטת Gmail (lease-based), dedup (MessageUniqueId/SHA256), שיוך לפרויקט, העלאה ל-ACC, תיוג קבצים |
| **ProjectWork Workspace** | ✅ פעיל | עץ קבצים אחיד (DB+filesystem), Naming Convention, אלטרנטיבות/גרסאות, Drag&Drop, FileSystemWatcher |
| **Inspection / Review** | ✅ פעיל | סדרות ביקורת, דוחות, פרקים/סעיפים/הערות, סנכרון תבנית Google Sheets, ייצוא דוח |
| **Decisions** | ✅ פעיל | CRUD החלטות, קטגוריות, היסטוריית שינויים |
| **Reports** | ✅ פעיל | R01 (סטטוס), R02 (תקציב), R03 (יומי) — הפקה ל-Google Sheets |
| **User Management** | ✅ פעיל | Windows Auth, תפקידים (User/PM/Admin), Active Directory, Soft delete |
| **ACC Integration** | ✅ פעיל | העלאת קבצים, תיקיות, provisioning משתמשים, WebView2 viewer |
| **Google Integration** | ✅ פעיל | Drive, Sheets, Gmail — OAuth2, rate limiting |
| **MasterPlan Sync** | ✅ פעיל | סנכרון API/Offline, Replica tables, Dapper |

### 1.3 מבנה ה-Solution (6 פרויקטים)

```
SiNetProjectManager.sln
├── SiNetProjectManager/          ← WPF UI (Views, Dialogs, Services, App.xaml.cs DI)
├── SiNetSQL/                     ← Core Library (Models, Services, ViewModels, EF Core)
├── SiOffice.GoogleConnector/     ← Google APIs (Drive, Sheets, Gmail, Reports)
├── SiOffice.AutodeskConnector/   ← Autodesk ACC/BIM360 REST API
├── MasterPlan.SyncEngine/        ← MasterPlan sync (Dapper + HTTP, standalone)
└── SiMasterPlanWeb/              ← Web project (ריק — שמור לעתיד)
```

### 1.4 טכנולוגיות נוכחיות

| רכיב | טכנולוגיה |
|---|---|
| Runtime | .NET 8, C# 12 |
| UI | WPF, RTL Hebrew, MVVM |
| DI | Microsoft.Extensions.DependencyInjection |
| ORM | Entity Framework Core + IDbContextFactory |
| DB | SQL Server (SI-WIN-2K19\SIDATA), Hebrew_100_CI_AS, compat level 120 |
| Logging | Serilog (Async file sink, 5MB rolling, 7 days) |
| Auth | Windows Authentication → Siuser table |
| Secrets | Windows Credential Manager |
| Integrations | Google APIs (OAuth2), ACC REST (2/3-legged OAuth), MasterPlan API |

### 1.5 מסד נתונים — מצב נוכחי

**~55 טבלאות** מחולקות ל-10 תחומים:

```
Core:        Projects, Siusers, Companies, Contacts, Places, Properties, ServiceProviders
Tasks:       ProjectAssignments, TaskTypes, TaskLinks, ProjectAssignmentStatuses, Events, StatusMaps
Decisions:   ProjectDecisions, DecisionCategories, DecisionHistories
Email:       EmailInboxMessages, EmailInboxAttachments, ThreadStatusMappings
Inspection:  InspectionSeries, InspectionReports, Chapters, Sections, Notes, CommentsBank, ChapterNames, SectionNames, NoteStatuses
Files:       ProjectFiles, ProjectFileRefs, ProjectFolders
ACC/Cloud:   AccHubs, AccSystemResources, ProjectAccMappings
Financial:   Banks, Bids, Bills, Contracts, Payments, Mifrat
Planning:    Planners, WeekWorks, WorkHours
Settings:    SystemSettings, UserSettings, AppUserRoles, SyncRunFailures
```

### 1.6 תפוקות נוכחיות (מספרים)

- **~40 שירותים** (Services)
- **~20 ViewModels**
- **~25 חלונות/דיאלוגים**
- **~10 UserControls**
- **3 אינטגרציות חיצוניות**

### 1.7 מה קיים ועובד — Capabilities Map

```
✅ בחירת פרויקט → ActiveProjectContext (Singleton, global)
✅ ניהול משימות → TaskService, TaskPriorityEngine, WorkPriorityService
✅ קליטת מיילים → EmailIngestionService (lease-based, Gmail API)
✅ שיוך מייל לפרויקט → ברמת Thread/Message + User Override
✅ תיוג קבצים מצורפים → AttachmentTaggingService (ProjectFileId FK)
✅ העלאת קבצים ל-ACC → Bim360Service.Upload
✅ עבודה עם קבצי פרויקט → ProjectWorkViewModel (Unified Tree)
✅ Naming Convention → BaseFileVersion (parser/builder)
✅ דוחות ביקורת → InspectionReportService + TemplateSyncService
✅ החלטות → ProjectDecisionService
✅ דוחות Google → R01/R02/R03 ReportService
✅ ייבוא משימות → TaskImportService (TSV)
✅ קישורים בין ישויות → TaskLink (polymorphic: Task, Report, Note, Email, Decision)
```

### 1.8 מה **חסר** — פערים מזוהים

| פער | תיאור | השפעה |
|---|---|---|
| **🔴 Workflow Engine** | לא קיים כלל. אין WorkflowInstance, WorkflowStage, או מנוע מעברים. | אין ניהול תהליכים עסקיים מובנה. כל פעולה היא ad-hoc. |
| **🔴 Email Context Engine** | `EmailContextService` קיים ברמה בסיסית (ThreadStatusMapping). אין ניתוח הקשר אוטומטי מלא. | המשתמש צריך לשייך ולהבין הקשר ידנית. |
| **🔴 Suggested Actions** | לא קיים. אין מנגנון שמציע פעולות בהתבסס על הקשר המייל. | המשתמש צריך לדעת בעצמו מה לעשות עם כל מייל. |
| **🟡 Workflow ↔ Task Separation** | משימות (ProjectAssignment) קיימות, אבל אינן חלק ממנוע תהליך. | אין שליטה על רצף פעולות, אין מעברי שלבים. |
| **🟡 ACC WebView2 Mapping** | Placeholder — `AccViewerUrl = null`. WebView2 ACC viewer מוגדר אך לא ממופה. | לא ניתן לצפות בקבצי ACC מתוך ProjectWork. |
| **🟡 File Import from Email** | תיוג קיים (ProjectFileId FK), אבל אין זרימה מלאה של ייבוא קובץ ממייל ← ProjectWork tree. | קבצים מצורפים לא "נוחתים" באופן אוטומטי בעץ הקבצים. |

---

## 2. Target State — מצב יעד

### 2.1 חזון — זרימה מקצה לקצה

```
Email → Context → Process → Work → Files → Review → Delivery
  │        │         │        │       │        │         │
  │        │         │        │       │        │         └─ דוח/משלוח ללקוח
  │        │         │        │       │        └─ בדיקת ביקורת + הערות
  │        │         │        │       └─ ניהול קבצים (ProjectWork)
  │        │         │        └─ עבודה בפועל (ProjectWork Workspace)
  │        │         └─ תהליך עסקי (Workflow Engine)
  │        └─ זיהוי הקשר אוטומטי (Email Context Engine)
  └─ קליטת מייל (Gmail API — כבר עובד)
```

**עיקרון מפתח:** המייל הוא **טריגר בלבד** — הוא נקודת הכניסה לתהליך.
העבודה בפועל מתרחשת בתוך **ProjectWork Workspace**.

### 2.2 הגדרות יסוד

| מונח | הגדרה | דוגמה |
|---|---|---|
| **Email** | נקודת כניסה — טריגר | מייל מלקוח עם בקשת שינוי |
| **Context** | הבנה אוטומטית של מה קורה | "מייל מלקוח X, בפרויקט Y, סוג: Design, שלב: ביצוע" |
| **Process (Workflow)** | תהליך עסקי מתמשך | "תהליך תכנון" — כולל שלבים: קבלה → בדיקה → תכנון → בקרה → אישור |
| **Work** | ביצוע בפועל | עבודה על קבצים ב-ProjectWork |
| **Task** | יחידת ביצוע בודדת | "בדוק תוכנית קומה 3" — פעולה אחת בתוך תהליך |
| **Review** | סבב ביקורת | בדיקת מסמכים, הערות, אישור |
| **Delivery** | משלוח ללקוח | דוח מוגמר, קובץ מאושר |

### 2.3 Workflow ≠ Task

> **זו הבחנה קריטית.**

| Workflow | Task |
|---|---|
| תהליך עסקי מתמשך | יחידת עבודה בודדת |
| כולל שלבים (Stages) | אין שלבים — רק סטטוס |
| מחזור חיים מלא | נוצרת ונסגרת |
| מקשר כמה משימות | שייכת לתהליך אחד |
| לא נוצר "באלפים" | ייתכנו כמה בפרויקט |
| דוגמה: "תהליך בדיקת תוכניות" | דוגמה: "בדוק תוכנית קומה 3" |

### 2.4 מצב יעד — מודולים

```
┌────────────────────────────────────────────────────────────────────────┐
│                        SiNet Target Architecture                       │
│                                                                        │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────────────────┐    │
│  │ Email Module  │  │ Context      │  │ Suggested Actions Engine  │    │
│  │ (קיים ✅)    │→│ Engine       │→│ (חדש 🔴)                 │    │
│  │              │  │ (חדש 🔴)    │  │                           │    │
│  └──────────────┘  └──────────────┘  └─────────────┬─────────────┘    │
│                                                     │                  │
│                                                     ▼                  │
│  ┌──────────────────────────────────────────────────────────────┐      │
│  │                    Workflow Engine (חדש 🔴)                  │      │
│  │  WorkflowDefinition → WorkflowInstance → WorkflowStage      │      │
│  │  History → Stage transitions → Task spawning                 │      │
│  └──────────────────────────────────┬───────────────────────────┘      │
│                                     │                                  │
│         ┌───────────────────────────┼─────────────────┐                │
│         ▼                           ▼                  ▼                │
│  ┌──────────────┐  ┌──────────────────────┐  ┌───────────────────┐    │
│  │ Task Module   │  │ ProjectWork Workspace │  │ Inspection Module │    │
│  │ (קיים ✅)    │  │ (קיים ✅)            │  │ (קיים ✅)        │    │
│  └──────────────┘  └──────────────────────┘  └───────────────────┘    │
│         │                   │                          │                │
│         └───────────────────┼──────────────────────────┘                │
│                             ▼                                          │
│                    ┌────────────────┐                                   │
│                    │ Decision Module │                                  │
│                    │ (קיים ✅)      │                                  │
│                    └────────────────┘                                   │
│                                                                        │
│  Cross-cutting: TaskLink (polymorphic) connects all modules            │
└────────────────────────────────────────────────────────────────────────┘
```

### 2.5 Email Context Engine — מצב יעד

**תפקיד:** ניתוח מייל נכנס ← הבנת ההקשר ← הכנת מידע לקבלת החלטות.

```
Input:  EmailInboxMessage (קיים)
         │
         ├── מאיזה פרויקט? (ProjectId — כבר ממופה)
         ├── איזה Thread? (ThreadId + ThreadStatusMapping)
         ├── מי שלח? (Sender → Company → Project contacts)
         ├── מה סוג הפרויקט? (TypeOfProjectInProject)
         ├── האם יש קבצים מצורפים? (Attachments — type, size, naming)
         ├── האם יש Workflow פעיל? (WorkflowInstance.Status == Active)
         └── מה ההיסטוריה? (קשרי TaskLink קודמים ב-Thread)
         
Output: EmailContext
         ├── Project (ref)
         ├── ProjectTypes[] (ref)
         ├── ActiveWorkflows[] (ref)
         ├── RelatedTasks[] (ref)
         ├── RelatedDecisions[] (ref)
         ├── AttachmentAnalysis (types, naming match, potential folder)
         └── ContextConfidence (high/medium/low)
```

### 2.6 Suggested Actions Engine — מצב יעד

**תפקיד:** בהתבסס על ה-Context, מציע למשתמש **רשימת פעולות אפשריות**.

```
Input:  EmailContext
         │
Output: SuggestedAction[]
         ├── "התחל תהליך תכנון" (StartWorkflow: Design)
         ├── "קשר לתהליך קיים" (AttachToWorkflow: existing instance)
         ├── "ייבא קבצים לפרויקט" (ImportAttachments: → ProjectWork folder)
         ├── "צור משימה" (CreateTask: from email content)
         ├── "רשום החלטה" (CreateDecision: from email content)
         ├── "התחל סבב ביקורת" (StartReview: from attachments)
         └── "העבר ל-ACC" (UploadToAcc: specific files → mapped folder)
```

**כל Action מכיל:**
- `ActionType` (enum)
- `DisplayText` (טקסט למשתמש)
- `Confidence` (high/medium/low)
- `PrefilledData` (נתונים מוכנים מראש — פרויקט, תיקייה, שם קובץ)
- `RequiresUserInput` (bool — האם צריך input נוסף)

### 2.7 Workflow Engine — מצב יעד

**תפקיד:** ניהול תהליכים עסקיים מתמשכים. לא Task manager — אלא Process manager.

```
WorkflowDefinition (תבנית תהליך)
    ├── Name: "תהליך תכנון"
    ├── StageDefinitions[]:
    │   ├── Stage 1: "קבלת חומרים"
    │   ├── Stage 2: "בדיקת התכנות"
    │   ├── Stage 3: "תכנון"
    │   ├── Stage 4: "בקרה פנימית"
    │   └── Stage 5: "אישור ושליחה"
    └── AllowedTransitions[]

WorkflowInstance (מופע תהליך — per project)
    ├── WorkflowDefinitionId
    ├── ProjectId
    ├── CurrentStageId
    ├── Status: Active / Paused / Completed / Cancelled
    ├── StartedAt, CompletedAt
    ├── TriggeredByEmailId? (אם התחיל ממייל)
    └── History[] (WorkflowStageTransition)

WorkflowStageTransition (היסטוריה)
    ├── FromStageId → ToStageId
    ├── TransitionedAt
    ├── TransitionedByUserId
    ├── Notes
    └── TaskLinks[] (משימות שנוצרו בשלב)
```

**סוגי Workflow (ראשוניים):**

| Workflow | שלבים | טריגר |
|---|---|---|
| Design (תכנון) | קבלה → בדיקה → תכנון → בקרה → אישור | מייל / ידני |
| Review (בדיקה) | קבלת חומר → ביקורת → תיקונים → אישור | מייל / ידני |
| Opinion (חוות דעת) | בקשה → ניתוח → כתיבה → אישור | מייל / ידני |
| Scope Change (שינוי היקף) | בקשה → הערכה → אישור / דחייה | מייל |
| Email Intake (קליטת מייל) | זיהוי → סיווג → הפניה → טיפול | אוטומטי |

---

## 3. Architecture — ארכיטקטורה

### 3.1 שכבות מערכת

```
┌─────────────────────────────────────────────────────────────┐
│  Layer 1: UI (Presentation)                                  │
│  WPF Views, Dialogs, UserControls, Converters               │
│  📁 SiNetProjectManager/                                    │
├─────────────────────────────────────────────────────────────┤
│  Layer 2: ViewModel (Presentation Logic)                     │
│  MVVM ViewModels, Commands, Presentation state              │
│  📁 SiNetSQL/MVVM/                                         │
├─────────────────────────────────────────────────────────────┤
│  Layer 3: Application (Use Cases)                            │
│  Orchestrators, Coordinators, Use Case handlers             │
│  📁 SiNetSQL/Services/ (new: UseCases/)                    │
├─────────────────────────────────────────────────────────────┤
│  Layer 4: Domain (Business Logic)                            │
│  Entities, Value Objects, Domain Services, Business Rules   │
│  📁 SiNetSQL/Models/ + SiNetSQL/Services/ (domain parts)   │
├─────────────────────────────────────────────────────────────┤
│  Layer 5: Infrastructure (External Concerns)                 │
│  EF Core, Repositories, File System, External APIs          │
│  📁 SiNetSQL/Data/ + External connector projects            │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 גבולות מודולים (Module Boundaries)

```
┌──────────────────────────────────────────────────────────────────┐
│                        Module Map                                 │
│                                                                   │
│  ┌─────────┐    ┌───────────┐    ┌──────────────┐               │
│  │ Email    │───→│ Context   │───→│ Suggested    │               │
│  │ Module   │    │ Engine    │    │ Actions      │               │
│  └─────────┘    └───────────┘    └──────┬───────┘               │
│       │                                  │                        │
│       │              ┌───────────────────┘                        │
│       │              ▼                                            │
│  ┌────┴────────────────────────────────────────────────────┐     │
│  │              Workflow Engine                              │     │
│  │  ┌─────────────┐  ┌────────────────┐  ┌──────────────┐ │     │
│  │  │ Definition   │  │ Instance       │  │ Transition   │ │     │
│  │  │ Management   │  │ Lifecycle      │  │ Engine       │ │     │
│  │  └─────────────┘  └────────────────┘  └──────────────┘ │     │
│  └──────────────────────┬──────────────────────────────────┘     │
│                          │                                        │
│    ┌─────────────────────┼───────────────────────┐                │
│    ▼                     ▼                        ▼                │
│  ┌──────────┐  ┌────────────────┐  ┌──────────────────────┐     │
│  │ Task     │  │ ProjectWork    │  │ Inspection / Review  │     │
│  │ Module   │  │ Workspace      │  │ Module               │     │
│  └──────────┘  └────────────────┘  └──────────────────────┘     │
│    ▲                     ▲                        ▲                │
│    └─────────────────────┼────────────────────────┘                │
│                          │                                        │
│              ┌───────────┴────────────┐                           │
│              │ Decision Module         │                           │
│              └────────────────────────┘                           │
│                                                                   │
│  ═══════════════ Cross-cutting ═══════════════                   │
│  TaskLink (polymorphic) │ ActiveProjectContext │ AppLogger        │
└──────────────────────────────────────────────────────────────────┘
```

### 3.3 זרימת נתונים (Data Flow)

```
                    Gmail API
                       │
                       ▼
              ┌────────────────┐
              │ EmailIngestion │
              │ Service        │
              └───────┬────────┘
                      │ EmailInboxMessage (DB)
                      ▼
              ┌────────────────┐
              │ Email Context  │←── ThreadStatusMapping
              │ Engine         │←── TypeOfProjectInProject
              └───────┬────────┘←── Active WorkflowInstances
                      │ EmailContext (in-memory DTO)
                      ▼
              ┌────────────────┐
              │ Suggested      │
              │ Actions Engine │
              └───────┬────────┘
                      │ SuggestedAction[] (in-memory)
                      ▼
              ┌────────────────┐
              │ User Decision  │  ← UI: user picks action
              └───────┬────────┘
                      │
         ┌────────────┼────────────┐
         ▼            ▼            ▼
   ┌──────────┐ ┌──────────┐ ┌──────────┐
   │ Start    │ │ Create   │ │ Import   │
   │ Workflow │ │ Task     │ │ Files    │
   └────┬─────┘ └────┬─────┘ └────┬─────┘
        │             │            │
        ▼             ▼            ▼
   ┌──────────────────────────────────────┐
   │         SQL Server (SIData)          │
   │  WorkflowInstances │ ProjectAssign.  │
   │  TaskLinks │ ProjectFiles │ etc.     │
   └──────────────────────────────────────┘
        │
        ▼
   ┌──────────────────────────────────────┐
   │     Filesystem (ProjectPath)         │
   │  Physical files: naming convention   │
   └──────────────────────────────────────┘
        │
        ▼
   ┌──────────────────────────────────────┐
   │     ACC (Autodesk Cloud)             │
   │  Mirrored project files              │
   └──────────────────────────────────────┘
```

### 3.4 תקשורת בין מודולים

| From | To | מנגנון | תיאור |
|---|---|---|---|
| Email → Context | Email Context Engine | Direct call | ניתוח EmailInboxMessage |
| Context → Actions | Suggested Actions Engine | Direct call | יצירת רשימת פעולות |
| Actions → Workflow | Workflow Engine | Command pattern | StartWorkflow / AttachToWorkflow |
| Workflow → Task | Task Module | Direct call | יצירת משימות בשלב |
| Workflow → ProjectWork | Event/Signal | ייבוא קבצים, פתיחת תיקייה |
| Any → Any | TaskLink | Polymorphic FK | קישור רוחבי בין ישויות |
| All → UI | ActiveProjectContext | Singleton + INotifyPropertyChanged | רענון Views |
| All → Log | AppLogger | Serilog wrapper | לוגינג מרכזי |

---

## 4. Domain Model — מודל תחום

### 4.1 Aggregates (שורשי צבירה)

```
┌─────────────────────────────────────────────────────────────┐
│  Aggregate 1: PROJECT (Root)                                 │
│  ──────────────────────────────                              │
│  Project                                                     │
│   ├── TypeOfProjectInProject[] (M:N with JobType)           │
│   ├── ProjectFolder[] (self-ref tree via Infolderid)        │
│   │    └── ProjectFile[] (metadata)                         │
│   │         └── ProjectFileRef[] (XRef links)               │
│   ├── ProjectAccMapping (ACC link)                          │
│   └── ProjectPath (filesystem root)                         │
│                                                              │
│  Invariants:                                                 │
│   • Project must have at least one TypeOfProjectInProject   │
│   • ProjectPath must be a valid directory                    │
│   • Project.Number is unique                                 │
├─────────────────────────────────────────────────────────────┤
│  Aggregate 2: TASK                                           │
│  ──────────────────────                                      │
│  ProjectAssignment                                           │
│   ├── ProjectAssignmentEvents[] (audit trail)               │
│   └── TaskLinks[] (polymorphic references)                  │
│                                                              │
│  Invariants:                                                 │
│   • Assignment must belong to a Project                      │
│   • Status transitions must follow configured rules          │
│   • WorkPriority is computed, not user-set                   │
├─────────────────────────────────────────────────────────────┤
│  Aggregate 3: EMAIL THREAD                                   │
│  ────────────────────────────                                │
│  EmailInboxMessage                                           │
│   ├── EmailInboxAttachment[] (SHA256 dedup)                 │
│   └── ThreadStatusMapping (thread-level tracking)           │
│                                                              │
│  Invariants:                                                 │
│   • MessageUniqueId is unique (dedup)                        │
│   • Attachment.HashSha256 prevents duplicates               │
│   • Lease-based processing (TTL on ProcessingStartedAtUtc)  │
├─────────────────────────────────────────────────────────────┤
│  Aggregate 4: INSPECTION SERIES                              │
│  ──────────────────────────────────                          │
│  InspectionSeries                                            │
│   ├── InspectionReport[]                                    │
│   │    └── InspectionNote[] (via Chapter → Section → Note)  │
│   └── Chapter[]                                              │
│        └── Section[]                                         │
│                                                              │
│  Invariants:                                                 │
│   • Reports are numbered sequentially within series          │
│   • Notes carry over between reports (open → next report)   │
├─────────────────────────────────────────────────────────────┤
│  Aggregate 5: WORKFLOW INSTANCE (חדש 🔴)                    │
│  ──────────────────────────────────────────                   │
│  WorkflowInstance                                            │
│   ├── CurrentStage (ref → WorkflowStageDefinition)          │
│   ├── WorkflowStageTransition[] (history)                   │
│   └── TaskLinks[] (tasks spawned per stage)                 │
│                                                              │
│  Invariants:                                                 │
│   • Only one active stage at a time                          │
│   • Transitions must follow allowed paths                    │
│   • Completed/Cancelled instances are immutable              │
├─────────────────────────────────────────────────────────────┤
│  Aggregate 6: DECISION                                       │
│  ────────────────────────                                    │
│  ProjectDecision                                             │
│   └── DecisionHistory[] (version history)                   │
│                                                              │
│  Invariants:                                                 │
│   • Every edit creates a history record                      │
│   • Content is never physically deleted (soft history)       │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 Entities — קיימות

| Entity | Aggregate | תיאור |
|---|---|---|
| `Project` | Project | ישות מרכזית — כל מודול מתקשר אליה |
| `TypeOfProjectInProject` | Project | M:N בין Project ↔ JobType, כולל AdminWorkerId |
| `ProjectFolder` | Project | עץ תיקיות (self-ref via Infolderid) |
| `ProjectFile` | Project | מטאדאטה קובץ (לא הקובץ הפיזי) |
| `ProjectFileRef` | Project | הפניות XRef בין קבצים |
| `ProjectAssignment` | Task | משימה — FK ל-Project, Handler, Status, TaskType |
| `ProjectAssignmentStatus` | Task | סטטוס משימה |
| `ProjectAssignmentEvent` | Task | אירוע audit trail |
| `TaskLink` | Task | קישור polymorphic (5 entity types × 4 roles) |
| `TaskType` | Task | סוג משימה |
| `EmailInboxMessage` | Email | הודעת מייל (lease-based processing) |
| `EmailInboxAttachment` | Email | קובץ מצורף (SHA256 dedup, ACC upload) |
| `ThreadStatusMapping` | Email | מעקב thread |
| `InspectionSeries` | Inspection | סדרת ביקורת |
| `InspectionReport` | Inspection | דוח ביקורת בודד |
| `Chapter` / `ChapterName` | Inspection | פרק (instance + dictionary) |
| `Section` / `SectionName` | Inspection | סעיף (instance + dictionary) |
| `InspectionNote` | Inspection | הערת ביקורת |
| `InspectionNoteStatus` | Inspection | סטטוס הערה |
| `CommentsBank` | Inspection | בנק הערות לשימוש חוזר |
| `ProjectDecision` | Decision | החלטה |
| `DecisionCategory` | Decision | קטגוריית החלטה |
| `DecisionHistory` | Decision | היסטוריית שינויים |
| `Siuser` | Core | משתמש מערכת |
| `Company` | Core | חברה |
| `Contact` | Core | איש קשר |
| `Place` | Core | מיקום |
| `JobType` | Core | סוג פרויקט / סוג קובץ |
| `ProjectStatus` | Core | סטטוס פרויקט |
| `SystemSetting` | Settings | הגדרת מערכת (key-value) |
| `AppUserRole` | Settings | תפקיד משתמש |

### 4.3 Entities — חדשות (Target State) 🔴

| Entity | Aggregate | תיאור |
|---|---|---|
| `WorkflowDefinition` | Workflow | תבנית תהליך (שם, תיאור) |
| `WorkflowStageDefinition` | Workflow | שלב בתבנית (סדר, שם, תיאור) |
| `WorkflowTransitionRule` | Workflow | חוק מעבר (מ-שלב → ל-שלב, תנאים) |
| `WorkflowInstance` | Workflow | מופע תהליך (per project + definition) |
| `WorkflowStageTransition` | Workflow | היסטוריית מעבר (from → to, user, timestamp, notes) |

### 4.4 Value Objects

| Value Object | מיקום | תיאור |
|---|---|---|
| `BaseFileVersion` | `[NotMapped]` class | פרסור/בניית שם קובץ לפי Naming Convention |
| `EmailContext` | In-memory DTO (חדש 🔴) | תוצאת ניתוח הקשר מייל |
| `SuggestedAction` | In-memory DTO (חדש 🔴) | פעולה מוצעת למשתמש |
| `FileDropInfo` | In-memory DTO | מידע Drag & Drop |
| `EnrichmentPreviewItem` | In-memory DTO | תצוגה מקדימה של העשרה |

### 4.5 Enums — קיימים

| Enum | ערכים | שימוש |
|---|---|---|
| `TaskLinkEntityType` | Task=1, InspectionReport=2, InspectionNote=3, EmailInboxMessage=4, ProjectDecision=5 | סוג ישות מקושרת ב-TaskLink |
| `TaskLinkRole` | Trigger=1, Related=2, BlockedBy=3, FollowUp=4 | תפקיד הקישור |

### 4.6 Enums — חדשים (Target State) 🔴

| Enum | ערכים מוצעים | שימוש |
|---|---|---|
| `WorkflowStatus` | Draft, Active, Paused, Completed, Cancelled | סטטוס מופע Workflow |
| `WorkflowTriggerType` | Manual, Email, System | מה הפעיל את התהליך |
| `SuggestedActionType` | StartWorkflow, AttachToWorkflow, CreateTask, ImportFiles, CreateDecision, StartReview, UploadToAcc | סוג פעולה מוצעת |
| `ContextConfidence` | High, Medium, Low | רמת ביטחון בניתוח הקשר |
| `TaskLinkEntityType` (extended) | +WorkflowInstance=6 | הרחבת polymorphic link ל-Workflow |

### 4.7 תרשים יחסים מלא (Current + Target)

```
                              ┌────────────┐
                              │   Siuser   │
                              └──────┬─────┘
                                     │ (Author/Editor/Handler/Inspector/Admin)
            ┌────────────────────────┼──────────────────────────┐
            ▼                        ▼                           ▼
    ┌──────────────┐      ┌─────────────────┐        ┌──────────────────┐
    │   Project    │──┐   │ ProjectAssign.  │        │ WorkflowInstance │
    └──────┬───────┘  │   │ (Task)          │        │ (חדש 🔴)        │
           │          │   └────────┬────────┘        └────────┬─────────┘
           │          │            │                           │
    ┌──────┴──────┐   │    ┌───────┴───────┐          ┌───────┴──────────┐
    │ TypeOfProj- │   │    │   TaskLink    │          │ WorkflowStage-   │
    │ InProject   │   │    │ (polymorphic) │←─────────│ Transition       │
    │ (M:N)       │   │    └───────────────┘          └──────────────────┘
    └─────────────┘   │            │
                      │    ┌───────┴────────────────────────────────┐
           ┌──────────┘    │                                        │
           ▼               ▼                                        ▼
    ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
    │ ProjectFolder│  │ EmailInbox-  │  │ Inspection-  │  │ Project-     │
    │ (tree)       │  │ Message      │  │ Report       │  │ Decision     │
    └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └──────────────┘
           │                  │                  │
    ┌──────┴───────┐  ┌──────┴───────┐  ┌──────┴───────┐
    │ ProjectFile  │  │ EmailInbox-  │  │ Inspection-  │
    │ (metadata)   │  │ Attachment   │  │ Note         │
    └──────┬───────┘  └──────────────┘  └──────────────┘
           │
    ┌──────┴───────┐
    │ ProjectFile- │
    │ Ref (XRef)   │
    └──────────────┘
```

---

## 5. Application Layer — שכבת אפליקציה

### 5.1 Use Cases — קיימים (Current State)

#### Email Use Cases

| Use Case | שירות נוכחי | תיאור |
|---|---|---|
| IngestEmails | `EmailIngestionService` | סריקת Gmail, lease-based, dedup, שמירת DB |
| UploadExternalFile | `EmailIngestionService` | הורדת קבצים מ-links חיצוניים |
| TagAttachment | `AttachmentTaggingService` | תיוג קובץ מצורף ← ProjectFile |
| ResolveThreadContext | `EmailContextService` | שליפת הקשר Thread (בסיסי) |

#### Task Use Cases

| Use Case | שירות נוכחי | תיאור |
|---|---|---|
| CreateTask | `TaskService` | יצירת משימה חדשה |
| UpdateTask | `TaskService` | עדכון משימה |
| ComputePriority | `TaskPriorityEngine` | חישוב עדיפות |
| ImportTasks | `TaskImportService` | ייבוא מ-TSV |
| MapStatus | `StatusMappingService` | מיפוי סטטוס משימה → סטטוס פרויקט |

#### ProjectWork Use Cases

| Use Case | שירות נוכחי | תיאור |
|---|---|---|
| LoadUnifiedTree | `ProjectWorkViewModel` | טעינת עץ קבצים (DB + filesystem) |
| ClassifyFile | `BaseFileVersion` | פרסור שם קובץ → מטאדאטה |
| CreateAlternative | `ProjectFileNode` | יצירת אלטרנטיבה חדשה |
| HandleFileDrop | `FileDropBehavior` | Drag & Drop קבצים לעץ |
| WatchFilesystem | `ProjectWorkViewModel` | FileSystemWatcher — מעקב שינויים |

#### Inspection Use Cases

| Use Case | שירות נוכחי | תיאור |
|---|---|---|
| SyncTemplate | `TemplateSyncService` | סנכרון תבנית Google Sheets → DB |
| EditNote | `FloatingInspectionViewModel` | עריכת הערת ביקורת |
| ExportReport | `GoogleReportExportService` | ייצוא דוח ל-Google Sheet |
| CarryOverNotes | `InspectionReportService` | העברת הערות פתוחות לדוח הבא |

#### Decision Use Cases

| Use Case | שירות נוכחי | תיאור |
|---|---|---|
| CreateDecision | `ProjectDecisionService` | יצירת החלטה |
| UpdateDecision | `ProjectDecisionService` | עדכון (+ שמירת היסטוריה) |

#### Report Use Cases

| Use Case | שירות נוכחי | תיאור |
|---|---|---|
| GenerateR01 | `R01ReportService` | דוח סטטוס פרויקט |
| GenerateR02 | `R02ReportService` | דוח מעקב תקציב |
| GenerateR03 | `R03ReportService` | דוח יומי |

### 5.2 Use Cases — חדשים (Target State) 🔴

#### Email Context Use Cases

| Use Case | שירות חדש | תיאור |
|---|---|---|
| **AnalyzeEmailContext** | `EmailContextAnalyzer` | ניתוח מלא של מייל ← EmailContext DTO |
| **BuildSuggestedActions** | `SuggestedActionsBuilder` | הצעת פעולות בהתבסס על Context |
| **ExecuteSuggestedAction** | `ActionExecutor` | ביצוע פעולה שהמשתמש בחר |

#### Workflow Use Cases

| Use Case | שירות חדש | תיאור |
|---|---|---|
| **StartWorkflow** | `WorkflowEngine` | יצירת WorkflowInstance חדש |
| **AdvanceStage** | `WorkflowEngine` | מעבר לשלב הבא |
| **PauseWorkflow** | `WorkflowEngine` | השהיית תהליך |
| **CompleteWorkflow** | `WorkflowEngine` | סיום תהליך |
| **CancelWorkflow** | `WorkflowEngine` | ביטול תהליך |
| **GetActiveWorkflows** | `WorkflowQueryService` | שליפת תהליכים פעילים לפרויקט |
| **GetWorkflowHistory** | `WorkflowQueryService` | שליפת היסטוריית מעברים |

#### File Integration Use Cases

| Use Case | שירות חדש | תיאור |
|---|---|---|
| **ImportAttachmentToProject** | `FileImportCoordinator` | ייבוא קובץ ממייל → תיקיית פרויקט (Naming Convention) |
| **SyncFileToAcc** | `AccFileSyncService` | סנכרון קובץ ← ACC (mapped folder) |

### 5.3 זרימת Use Case מרכזית — Email → Work

```
1. IngestEmails
   └── EmailIngestionService.IngestToInboxAsync()
       └── Result: EmailInboxMessage[] (saved to DB)

2. AnalyzeEmailContext  (חדש 🔴)
   └── EmailContextAnalyzer.AnalyzeAsync(emailId)
       ├── Reads: EmailInboxMessage + Attachments
       ├── Reads: Project + TypeOfProjectInProject
       ├── Reads: Active WorkflowInstances (if any)
       ├── Reads: Related TaskLinks
       └── Result: EmailContext DTO

3. BuildSuggestedActions  (חדש 🔴)
   └── SuggestedActionsBuilder.BuildAsync(emailContext)
       ├── Rule engine based on context properties
       └── Result: SuggestedAction[]

4. [User picks action in UI]

5a. StartWorkflow  (חדש 🔴)
    └── WorkflowEngine.StartAsync(definitionId, projectId, triggeredByEmailId)
        ├── Creates WorkflowInstance
        ├── Creates TaskLink (Email → Workflow, Role: Trigger)
        └── Result: WorkflowInstance

5b. CreateTask
    └── TaskService.CreateAsync(...)
        ├── Creates ProjectAssignment
        ├── Creates TaskLink (Email → Task, Role: Trigger)
        └── Result: ProjectAssignment

5c. ImportAttachmentToProject  (חדש 🔴)
    └── FileImportCoordinator.ImportAsync(attachmentId, targetFolderId)
        ├── Reads: EmailInboxAttachment
        ├── Builds: BaseFileVersion (naming convention)
        ├── Copies: file to ProjectPath + correct subfolder
        ├── Updates: Attachment.ProjectFileId (tag)
        └── Result: imported file path

6. Work in ProjectWork Workspace
   └── User opens ProjectWorkView → unified tree reflects imported files
```

---

## 6. Infrastructure — תשתית

### 6.1 Data Access — EF Core

**Pattern:** `IDbContextFactory<SiNetSQLDbContext>` → short-lived contexts

```
SiNetSQL/Data/
├── SiNetSQLDbContext.cs                  ← Main DbContext (55+ DbSets)
├── SiNetSQLDbContext.EmailInbox.cs       ← Partial — Email-related config
├── SiNetSQLDbContext.cs (Partial)        ← Additional config
└── Configurations/
    ├── InspectionSystemConfiguration.cs  ← Fluent API: Inspection entities
    ├── EmailInboxAttachmentConfig.cs     ← Fluent API: Email entities
    ├── TaskLinkConfiguration.cs          ← Fluent API: TaskLink polymorphic
    ├── ProjectDecisionConfigurations.cs  ← Fluent API: Decisions
    ├── SystemSettingConfiguration.cs     ← Fluent API: Settings
    └── (future: WorkflowConfiguration.cs) 🔴
```

**חריגה ידועה:** `ProjectWorkViewModel` משתמש ב-`new SiNetSQLDbContext()` ישירות (לא IDbContextFactory).
מומלץ לתקן בעתיד — אבל עובד כי ה-ViewModel חי לאורך חיי ה-View.

### 6.2 Repositories — מצב נוכחי

**לא קיימת שכבת Repository נפרדת.** הגישה ל-DB היא ישירה דרך:
- `IDbContextFactory<SiNetSQLDbContext>` → LINQ queries בתוך Services
- `Dapper` (MasterPlan.SyncEngine בלבד)

> **📌 החלטה ארכיטקטונית:** בשלב זה **לא** מתוכנן מעבר ל-Repository Pattern מלא.
> שירותים חדשים ימשיכו לעבוד עם `IDbContextFactory` ישירות.
> אם בעתיד יידרש separation of concerns חזק יותר — ניתן להכניס שכבת Repository בהדרגה.

### 6.3 File System

| רכיב | מיקום | תיאור |
|---|---|---|
| Project Files | `Project.ProjectPath` (network share) | קבצי פרויקט פיזיים |
| Naming Convention | `BaseFileVersion` | `(ProjectNumber)-ProjectType-FileNumber-Alternative-Version-Name.ext` |
| Folder ACL | `FolderOpener.SetFolderSecurityForGroup` | SI-ENG\\שרטטים — Create yes, Delete no |
| File Watcher | `FileSystemWatcher` per root | IncludeSubdirectories, NotifyFilter: Dir+File+LastWrite |
| Excluded Extensions | HashSet | .bak, .dwt, .dwl, .dwl2, .ini, .$ds, .err, .tmp, .log, .exe |

### 6.4 External Integrations

#### Google APIs (SiOffice.GoogleConnector)

| API | שימוש | Auth |
|---|---|---|
| Gmail API | קליטת מיילים, קריאת גוף הודעה, קבצים מצורפים | OAuth2 (user consent) |
| Drive API | ניהול תיקיות/קבצים, שיתוף, הורדה | OAuth2 |
| Sheets API | קריאה/כתיבה ב-Spreadsheets, ייצוא דוחות, סנכרון תבניות | OAuth2 |

**Rate Limiting:** `GmailRateLimiter` + `GmailThrottleService` — מכבד Gmail API quotas.

#### Autodesk ACC (SiOffice.AutodeskConnector)

| יכולת | Method | Auth |
|---|---|---|
| Upload file | `Bim360Service.Upload` | 2-legged/3-legged OAuth |
| Create folder | `Bim360Service.CreateFolder` | 2-legged |
| Provision user | `AccUserBootstrapService` | 2-legged |
| Read markups | `Bim360Service.GetMarkups` | 3-legged |

**מיפוי:** `ProjectAccMapping` — קישור Project ↔ ACC Project.

#### MasterPlan (MasterPlan.SyncEngine)

| יכולת | שירות |
|---|---|
| API sync | `ApiDailySyncService` — HTTP → SQL (Dapper) |
| Offline sync | `OfflineDailySyncService` — NDJSON → SQL |
| Backup/Restore | `MonthlyBackupRestoreService` |

**אחסון:** Replica Tables ב-SQL Server (לא EF — Dapper ישיר).

### 6.5 Background Jobs

| Job | מנגנון | תיאור |
|---|---|---|
| Email ingestion | `EmailIngestionService` (user-triggered + lease) | סריקת Gmail + עיבוד |
| ACC bootstrap | `Task.Run()` on startup | Provisioning משתמשים ל-ACC |
| FileSystemWatcher | OS-level events | מעקב שינויים בתיקיות פרויקט |
| MasterPlan sync | Console app (scheduled externally) | סנכרון יומי |

> **📌 מידע חסר:** אין scheduler מובנה (כמו Hangfire/Quartz). כל ה-background work הוא manual trigger או startup task.
> אם בעתיד יידרשו jobs מתוזמנים (למשל: סנכרון Workflow אוטומטי) — יש לשקול הוספת scheduler.

### 6.6 Logging & Monitoring

| רכיב | יכולת |
|---|---|
| `Serilog` | File sink: `Logs/SiNet-{Date}.log`, 5MB rolling, 7 days |
| `AppLogger` | Wrapper עם context (User, Project, Operation) |
| `SyncRunFailures` | טבלת DB — לוג כשלונות סנכרון |
| `GmailMetricsCollector` | מעקב quota usage (Gmail API) |

### 6.7 Security

| שכבה | מנגנון |
|---|---|
| Authentication | Windows Auth → `Environment.UserName` → `Siuser` lookup |
| Authorization | DB roles: `IsInUsers`, `IsInProjectManagers`, `IsInAdmins` |
| Secrets | Windows Credential Manager (Connection String) |
| Provisioning | Encrypted file + password → `SecretProvisioningService` |
| Folder ACL | NTFS permissions: Create yes, Delete no (for group) |

---

## 7. UI — ממשק משתמש

### 7.1 מבנה כללי

```
MainWindow (BaseWindow)
├── Menu Bar (role-based visibility)
├── Content Area (switching UserControls)
│   ├── TaskPanelView           — משימות
│   ├── FloatingProjectTasksView — משימות צפות
│   ├── FloatingInspectionView  — ביקורת
│   ├── EmailManagementView     — מיילים
│   ├── ProjectWorkView         — בעבודה (Unified Tree + ACC)
│   ├── FileManagerView         — ניהול קבצים
│   ├── ProjectFolderTreeView   — עץ תיקיות
│   ├── CreateProjectUserControl— יצירת פרויקט
│   └── WindowEditProject       — עריכת פרויקט
├── Dialogs (on demand)
│   ├── TaskImportWindow, AddUserWindow, UserManagementWindow
│   ├── ProjectDecisionsWindow, ProjectTypeRulesWindow
│   ├── StatusMappingWindow, MasterPlanMappingWindow
│   ├── R01/R02/R03 ReportDialog
│   ├── InspectionHelpWindow, MigrationPocWindow
│   ├── SettingsWindow, ManagementSettingsWindow
│   ├── SecretSetupWindow, ProvisioningPasswordDialog
│   ├── ActionProofDialog, SyncFailuresWindow
│   ├── ExternalBrowserWindow, BackgroundUploadsDialog
│   ├── DownloadAssociationDialog, RenameProjectWindow
│   └── AlternativeNameWindow
└── Common Controls
    ├── SearchableProjectSelector (ComboBox + search)
    └── RichTextNoteEditor (RTF editing)
```

### 7.2 ViewModels — מיפוי נוכחי

| ViewModel | View | Lines | תפקיד |
|---|---|---|---|
| `MainWindowViewModel` | MainWindow | — | תפריט, הרשאות, ניווט |
| `TaskPanelViewModel` | TaskPanelView | — | משימות CRUD + סינון |
| `FloatingProjectTasksViewModel` | FloatingProjectTasksView | — | משימות צפות |
| `FloatingInspectionViewModel` | FloatingInspectionView | ~83K | עץ ביקורת |
| `TaskImportViewModel` | TaskImportWindow | — | ייבוא TSV |
| `EmailManagementViewModel` | EmailManagementView | ~150K | מיילים מלא |
| `ProjectDecisionsViewModel` | ProjectDecisionsWindow | — | החלטות |
| `ProjectWorkViewModel` | ProjectWorkView | ~683 | בעבודה |
| `UserManagementViewModel` | UserManagementWindow | — | ניהול משתמשים |
| `AddUserViewModel` | AddUserWindow | — | הוספת משתמש |
| `CompanyViewModel` | WindowCompany | — | חברות |
| `ProjectTypeRulesViewModel` | ProjectTypeRulesWindow | — | חוקי סוג פרויקט |
| `StatusMappingViewModel` | StatusMappingWindow | — | מיפוי סטטוסים |
| `MasterPlanMappingViewModel` | MasterPlanMappingWindow | — | מיפוי MasterPlan |
| `R01/R02/R03ReportViewModel` | R01/R02/R03Dialog | — | דוחות |

### 7.3 Views חדשים (Target State) 🔴

| View חדש | ViewModel חדש | תיאור |
|---|---|---|
| **EmailContextPanel** | `EmailContextViewModel` | פאנל צד: מציג Context + Suggested Actions ליד רשימת המיילים |
| **WorkflowDashboardView** | `WorkflowDashboardViewModel` | מסך ניהול תהליכים — רשימת instances פעילים, סטטוס, שלב נוכחי |
| **WorkflowInstanceView** | `WorkflowInstanceViewModel` | צפייה בתהליך בודד — שלבים, היסטוריה, משימות משויכות |
| **WorkflowDefinitionEditor** | `WorkflowDefinitionViewModel` | עריכת תבנית תהליך (Admin) — שלבים, מעברים |
| **FileImportDialog** | `FileImportViewModel` | דיאלוג ייבוא קובץ ממייל ← תיקיית פרויקט (בחירת יעד + naming) |

### 7.4 שילוב UI — Email + Context + Actions

```
┌──────────────────────────────────────────────────────────────────┐
│ EmailManagementView (existing)                                    │
│                                                                   │
│ ┌────────────────────────┐│┌────────────────────────────────────┐│
│ │ Email List             ││ │ Email Content (WebView2)          ││
│ │ ┌────────────────────┐ ││ │                                   ││
│ │ │ 📧 Subject 1       │ ││ │ [rendered HTML body]              ││
│ │ │ 📧 Subject 2  ◄────┤ ││ │                                   ││
│ │ │ 📧 Subject 3       │ ││ │                                   ││
│ │ └────────────────────┘ ││ │                                   ││
│ └────────────────────────┘│ │                                   ││
│ ┌────────────────────────┐│ │                                   ││
│ │ Context Panel (חדש 🔴)││ └────────────────────────────────────┘│
│ │                        ││                                       │
│ │ 📋 הקשר:              ││                                       │
│ │  פרויקט: 1234 אורלנד  ││                                       │
│ │  סוג: אדריכלות        ││                                       │
│ │  תהליך פעיל: תכנון    ││                                       │
│ │  שלב: ביצוע           ││                                       │
│ │                        ││                                       │
│ │ 💡 פעולות מוצעות:     ││                                       │
│ │  [📁 ייבא קבצים]      ││                                       │
│ │  [🔄 קדם תהליך]       ││                                       │
│ │  [📝 צור משימה]        ││                                       │
│ │  [📋 רשום החלטה]       ││                                       │
│ └────────────────────────┘│                                       │
└──────────────────────────────────────────────────────────────────┘
```

---

## 8. Development Breakdown — פירוט פיתוח

### 8.1 רכיבים חדשים לפיתוח

#### Tier 1: Domain Entities (DB)

| רכיב | סוג | קובץ | תיאור |
|---|---|---|---|
| `WorkflowDefinition` | Entity | `Models/WorkflowDefinition.cs` | תבנית תהליך |
| `WorkflowStageDefinition` | Entity | `Models/WorkflowStageDefinition.cs` | שלב בתבנית |
| `WorkflowTransitionRule` | Entity | `Models/WorkflowTransitionRule.cs` | חוק מעבר |
| `WorkflowInstance` | Entity | `Models/WorkflowInstance.cs` | מופע תהליך |
| `WorkflowStageTransition` | Entity | `Models/WorkflowStageTransition.cs` | היסטוריית מעבר |
| `WorkflowStatus` | Enum | `Models/WorkflowStatus.cs` | סטטוס מופע |
| `WorkflowTriggerType` | Enum | `Models/WorkflowTriggerType.cs` | סוג טריגר |
| `WorkflowConfiguration` | EF Config | `Data/Configurations/WorkflowConfiguration.cs` | Fluent API config |
| Migration | EF Migration | `Migrations/AddWorkflowSystem.cs` | DB schema |

#### Tier 2: Domain Services

| רכיב | סוג | קובץ | תיאור |
|---|---|---|---|
| `WorkflowEngine` | Service | `Services/Workflow/WorkflowEngine.cs` | Start, Advance, Pause, Complete, Cancel |
| `WorkflowQueryService` | Service | `Services/Workflow/WorkflowQueryService.cs` | Get active, history, by project |
| `WorkflowValidationService` | Service | `Services/Workflow/WorkflowValidationService.cs` | Validate transitions, rules |
| `WorkflowSeedService` | Service | `Services/Workflow/WorkflowSeedService.cs` | Seed default definitions (Design, Review, Opinion, etc.) |

#### Tier 3: Application Layer (Use Cases)

| רכיב | סוג | קובץ | תיאור |
|---|---|---|---|
| `EmailContextAnalyzer` | Service | `Services/EmailContext/EmailContextAnalyzer.cs` | ניתוח מלא של מייל |
| `EmailContext` | DTO | `Services/EmailContext/EmailContext.cs` | תוצאת ניתוח |
| `SuggestedActionsBuilder` | Service | `Services/EmailContext/SuggestedActionsBuilder.cs` | בניית רשימת פעולות |
| `SuggestedAction` | DTO | `Services/EmailContext/SuggestedAction.cs` | פעולה בודדת |
| `SuggestedActionType` | Enum | `Services/EmailContext/SuggestedActionType.cs` | סוג פעולה |
| `ActionExecutor` | Coordinator | `Services/EmailContext/ActionExecutor.cs` | ביצוע פעולה שנבחרה |
| `FileImportCoordinator` | Coordinator | `Services/FileImport/FileImportCoordinator.cs` | ייבוא קובץ ממייל → ProjectWork |

#### Tier 4: ViewModels

| רכיב | סוג | קובץ | תיאור |
|---|---|---|---|
| `EmailContextViewModel` | ViewModel | `MVVM/EmailContextViewModel.cs` | Context panel (suggested actions) |
| `WorkflowDashboardViewModel` | ViewModel | `MVVM/WorkflowDashboardViewModel.cs` | רשימת תהליכים |
| `WorkflowInstanceViewModel` | ViewModel | `MVVM/WorkflowInstanceViewModel.cs` | תהליך בודד |
| `WorkflowDefinitionViewModel` | ViewModel | `MVVM/WorkflowDefinitionViewModel.cs` | עריכת תבנית (Admin) |
| `FileImportViewModel` | ViewModel | `MVVM/FileImportViewModel.cs` | דיאלוג ייבוא קובץ |

#### Tier 5: Views (UI)

| רכיב | סוג | קובץ | תיאור |
|---|---|---|---|
| `EmailContextPanel` | UserControl | `WPFUserControl/EmailContextPanel.xaml` | Context + Actions |
| `WorkflowDashboardView` | UserControl | `WPFUserControl/WorkflowDashboardView.xaml` | Dashboard |
| `WorkflowInstanceView` | UserControl | `WPFUserControl/WorkflowInstanceView.xaml` | Instance detail |
| `WorkflowDefinitionEditor` | Window | `Dialogs/WorkflowDefinitionEditor.xaml` | Definition editor |
| `FileImportDialog` | Window | `Dialogs/FileImportDialog.xaml` | File import |

#### Tier 6: הרחבות לרכיבים קיימים

| רכיב קיים | שינוי | תיאור |
|---|---|---|
| `TaskLinkEntityType` enum | +WorkflowInstance=6 | הוספת Workflow כישות מקושרת |
| `SiNetSQLDbContext` | +DbSets for Workflow | הוספת 5 DbSets חדשים |
| `EmailManagementView` | +EmailContextPanel integration | שילוב פאנל Context בתוך View קיים |
| `MainWindow` menu | +Workflow items | הוספת תפריט תהליכים |
| `ActiveProjectContext` | +ActiveWorkflows property | חשיפת תהליכים פעילים |

### 8.2 סיכום כמותי

| קטגוריה | כמות רכיבים חדשים |
|---|---|
| Entities (DB Models) | 5 |
| Enums | 4 |
| EF Configuration | 1 |
| Migration | 1 |
| Domain Services | 4 |
| Application Services (Use Cases) | 3 |
| Coordinators | 2 |
| DTOs | 3 |
| ViewModels | 5 |
| Views (XAML) | 5 |
| Modifications to existing | 5 |
| **סה"כ** | **~38 רכיבים** |

---

## 9. Technical Roadmap — מפת דרכים

### 9.1 שלבי פיתוח

```
Phase 1: Workflow Foundation          ← DB + Domain
Phase 2: Workflow Engine              ← Business Logic
Phase 3: Email Context Engine         ← Context Analysis
Phase 4: Suggested Actions            ← Action Recommendations
Phase 5: File Import Pipeline         ← Email → ProjectWork
Phase 6: UI Integration               ← Views + UX
Phase 7: ACC Enhancement              ← WebView2 mapping
```

### Phase 1: Workflow Foundation (תשתית)

**מטרה:** יצירת סכמת DB ותשתית הנתונים לתהליכים.

| משימה | תיאור | תלות |
|---|---|---|
| 1.1 | יצירת Entities: WorkflowDefinition, WorkflowStageDefinition, WorkflowTransitionRule, WorkflowInstance, WorkflowStageTransition | — |
| 1.2 | יצירת Enums: WorkflowStatus, WorkflowTriggerType | — |
| 1.3 | יצירת WorkflowConfiguration (Fluent API) | 1.1 |
| 1.4 | הוספת DbSets ל-SiNetSQLDbContext | 1.1, 1.3 |
| 1.5 | הרחבת TaskLinkEntityType (+WorkflowInstance=6) | 1.1 |
| 1.6 | יצירת EF Migration | 1.4, 1.5 |
| 1.7 | יצירת WorkflowSeedService (תבניות ברירת מחדל) | 1.6 |

**תוצר:** סכמת DB מוכנה + Seed data ל-5 תבניות Workflow.

### Phase 2: Workflow Engine (לוגיקה)

**מטרה:** מנוע ניהול תהליכים עסקיים.

| משימה | תיאור | תלות |
|---|---|---|
| 2.1 | `WorkflowEngine` — Start, Advance, Pause, Complete, Cancel | Phase 1 |
| 2.2 | `WorkflowValidationService` — validate transitions | 2.1 |
| 2.3 | `WorkflowQueryService` — get active, history, by project | Phase 1 |
| 2.4 | Integration: TaskLink creation on stage transition | 2.1 |
| 2.5 | Unit tests for WorkflowEngine | 2.1, 2.2 |

**תוצר:** מנוע Workflow פעיל עם API שלם.

### Phase 3: Email Context Engine (ניתוח)

**מטרה:** ניתוח מייל ← הבנת הקשר.

| משימה | תיאור | תלות |
|---|---|---|
| 3.1 | `EmailContext` DTO + `ContextConfidence` enum | — |
| 3.2 | `EmailContextAnalyzer` — ניתוח: project, types, workflows, tasks, attachments | Phase 2 (for active workflows) |
| 3.3 | הרחבת `EmailContextService` הקיים — integrate with Analyzer | 3.2 |
| 3.4 | Tests for context analysis accuracy | 3.2 |

**תוצר:** מנגנון ניתוח הקשר שמחזיר EmailContext מלא.

### Phase 4: Suggested Actions (המלצות)

**מטרה:** הצעת פעולות למשתמש.

| משימה | תיאור | תלות |
|---|---|---|
| 4.1 | `SuggestedAction` DTO + `SuggestedActionType` enum | — |
| 4.2 | `SuggestedActionsBuilder` — rule engine based on context | Phase 3 |
| 4.3 | `ActionExecutor` — coordinator שמבצע פעולה שנבחרה | 4.2, Phase 2 |
| 4.4 | Tests for action suggestions | 4.2 |

**תוצר:** מנגנון הצעות שמחזיר רשימת פעולות מתועדפות.

### Phase 5: File Import Pipeline (ייבוא קבצים)

**מטרה:** ייבוא קבצים ממייל ← ProjectWork tree.

| משימה | תיאור | תלות |
|---|---|---|
| 5.1 | `FileImportCoordinator` — orchestrate: attachment → naming → copy → tag | Phase 4 (ActionExecutor integration) |
| 5.2 | Integration with `BaseFileVersion` for naming | — |
| 5.3 | Integration with `AttachmentTaggingService` for tagging | — |
| 5.4 | FileImportViewModel + FileImportDialog | 5.1 |
| 5.5 | Tests for import pipeline | 5.1 |

**תוצר:** זרימה מלאה: קובץ מצורף ממייל → תיקיית פרויקט עם Naming Convention.

### Phase 6: UI Integration (ממשק)

**מטרה:** שילוב כל המנגנונים ב-UI.

| משימה | תיאור | תלות |
|---|---|---|
| 6.1 | `EmailContextPanel` UserControl | Phase 4 |
| 6.2 | שילוב EmailContextPanel ב-EmailManagementView | 6.1 |
| 6.3 | `WorkflowDashboardView` + ViewModel | Phase 2 |
| 6.4 | `WorkflowInstanceView` + ViewModel | Phase 2 |
| 6.5 | `WorkflowDefinitionEditor` + ViewModel (Admin) | Phase 2 |
| 6.6 | הוספת תפריט Workflow ב-MainWindow | 6.3 |
| 6.7 | הרחבת `ActiveProjectContext` — ActiveWorkflows | Phase 2 |
| 6.8 | DI registration for all new services/ViewModels | All phases |

**תוצר:** UI מלא ומשולב.

### Phase 7: ACC Enhancement (אופציונלי)

**מטרה:** מיפוי ACC ← WebView2 viewer פעיל.

| משימה | תיאור | תלות |
|---|---|---|
| 7.1 | מיפוי ProjectAccMapping → AccViewerUrl | — |
| 7.2 | הפעלת WebView2 viewer ב-ProjectWorkView | 7.1 |
| 7.3 | `AccFileSyncService` — סנכרון קבצים ← ACC mapped folder | Phase 5 |
| 7.4 | `ProjectFileInstance` — מעקב הצבת קבצים (FileServer / ACC) | Phase 5 |
| 7.5 | `StorageDestination` routing — ניתוב קבצים לפי ProjectFile | 7.4 |

**תוצר:** ACC viewer פעיל + סנכרון קבצים.

### 9.2 תרשים תלויות

```
Phase 1 ──→ Phase 2 ──→ Phase 3 ──→ Phase 4 ──→ Phase 5
  (DB)       (Engine)     (Context)   (Actions)   (Files)
                │                        │            │
                └────────────────────────┼────────────┘
                                         │
                                         ▼
                                    Phase 6 (UI)
                                         │
                                         ▼
                                    Phase 7 (ACC)
                                    [אופציונלי]
```

### 9.3 הערכת מורכבות

| Phase | מורכבות | סיכון | הערות |
|---|---|---|---|
| 1 — Workflow Foundation | 🟢 נמוכה | נמוך | DB + CRUD — מוכר |
| 2 — Workflow Engine | 🟡 בינונית | בינוני | State machine + validation rules |
| 3 — Email Context Engine | 🟡 בינונית | בינוני | תלוי באיכות הנתונים (project mapping, thread resolution) |
| 4 — Suggested Actions | 🟡 בינונית | בינוני | Rule engine — צריך tuning |
| 5 — File Import | 🟢 נמוכה | נמוך | ברובו שילוב קוד קיים (BaseFileVersion, FileHelpers) |
| 6 — UI Integration | 🟡 בינונית | נמוך | WPF — טכנולוגיה מוכרת, אבל integration scope גדול |
| 7 — ACC Enhancement | 🟠 גבוהה | גבוה | תלות באינטגרציה חיצונית (Autodesk API) |

---

## 📌 מידע חסר — דגשים לבירור

| נושא | מה חסר | השפעה |
|---|---|---|
| **Workflow Definitions** | אין רשימה סופית של תהליכים ושלבים. הוגדרו 5 סוגים ראשוניים — נדרש אישור עסקי. | Phase 1 seed data |
| **Email Context Rules** | אין הגדרה מדויקת של חוקי ניתוח הקשר. למשל: איך לזהות סוג בקשה ממייל? Keyword matching? AI? ידני? | Phase 3 algorithm |
| **Suggested Actions Ranking** | אין הגדרה של אלגוריתם תיעדוף פעולות. Rule-based? ML? Static weights? | Phase 4 engine |
| **ACC Project Mapping** | חלק מהפרויקטים לא ממופים ל-ACC. מה קורה כשאין מיפוי? | Phase 7 fallback |
| **Concurrent Workflows** | האם לפרויקט יכולים להיות מספר instances פעילים של אותו סוג Workflow? | Phase 2 validation rules |
| **Workflow Permissions** | מי יכול להתחיל/לקדם/לבטל תהליך? לפי Role? לפי AdminWorker? | Phase 2 authorization |
| **Scheduler** | אין scheduler מובנה. האם נדרש לעתיד (auto-reminders, auto-escalation)? | Cross-cutting concern |
| **File Import — Conflict Resolution** | מה קורה כשקובץ מיובא מתנגש עם קובץ קיים? (אותו naming convention, אותה גרסה) | Phase 5 edge case |

---

## 15. סיכום

```
┌─────────────────────────────────────────────────────────────────┐
│                SiNet — Technical Architecture Summary            │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Current State:                                                  │
│   • 6 projects, ~55 tables, ~40 services, ~20 ViewModels        │
│   • Fully working: Email, Tasks, ProjectWork, Inspection,       │
│     Decisions, Reports, ACC, Google, MasterPlan                 │
│   • Missing: Workflow Engine, Context Engine, Suggested Actions  │
│                                                                  │
│  Target State:                                                   │
│   • Email → Context → Process → Work → Files → Review → Delivery│
│   • Email = trigger only, ProjectWork = central workspace        │
│   • Workflow ≠ Task (process vs. unit of work)                  │
│                                                                  │
│  New Components:                                                 │
│   • ~38 new components (5 entities, 4 enums, 9 services,       │
│     2 coordinators, 3 DTOs, 5 ViewModels, 5 Views, 5 mods)    │
│                                                                  │
│  7-Phase Roadmap:                                                │
│   • Phase 1-2: Workflow (DB + Engine)                            │
│   • Phase 3-4: Email Context + Actions                           │
│   • Phase 5: File Import Pipeline                                │
│   • Phase 6: UI Integration                                      │
│   • Phase 7: ACC Enhancement (optional)                          │
│                                                                  │
│  Key Principles:                                                 │
│   • Workflow ≠ Task                                              │
│   • Email = trigger, ProjectWork = workspace                     │
│   • DB = metadata, Filesystem = files                            │
│   • TaskLink = universal cross-module connector                  │
│   • No code in this phase — design only                         │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```
