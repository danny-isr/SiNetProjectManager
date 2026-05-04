# 🎯 תוכנית ביצוע — Implementation Execution Plan

> **תאריך:** יוני 2026  
> **סטטוס:** תוכנית מאושרת לביצוע  
> **מטרה:** תרגום כל מסמכי האפיון לתוכנית עבודה מסודרת שניתן להתחיל לממש

---

## 🔄 עדכון מצב ביצוע (2026)

חלקים מרכזיים של התוכנית כבר בוצעו ואוחדו:

- ✅ **Phase 1 (DB) — בוצע:** הישויות `WorkflowDefinition`, `WorkflowStageDefinition`,
  `WorkflowTransitionRule`, `WorkflowInstance`, `WorkflowStageTransition` קיימות,
  כולל `ProjectTypeWorkflowDefinition`, `ProjectTypeWorkflowStage`, `ProjectTypeDiscipline`.
- ✅ **Phase 2 (Engine) — בוצע:** קיימים `WorkflowEngine`, `WorkflowQueryService`,
  `WorkflowValidationService`, `WorkflowSeedService`, `WorkflowActionExecutor`.
- ✅ **קונסולידציה:** הזרמים הישנים (Design / Review / Opinion / Intake / ScopeExpansion)
  הוסרו. נשאר **PlanningWorkflow קנוני אחד** עם `PLN.*` stages.
- ✅ **TaskResult:** משימות נסגרות עם `RecordTaskResult`. שינויי סטטוס פרויקט נעשים
  דרך פעולות מעבר במנוע (`SetProjectStatus`, `SetBillingPending`, `CloseProject`).
- ✅ **UI:** `WorkflowManagementWindow` הוא ה-hub היחיד עם 5 לשוניות
  (Builder / Visual Designer / Policy / Dashboard / Behaviors / Help).
- ✅ **Policy UI:** הורחבה ל-3 לשוניות-משנה — תהליכים מותרים, שלבים פעילים,
  תחומים פעילים — פר-סוג-פרויקט.
- ✅ **TFM:** הפרויקט עבר ל-.NET 10.
- ✅ **אוטומציה End-to-End:**
  - יצירת פרויקט ממייל מפעילה אוטומטית `PlanningWorkflow` (`WorkflowCreateProjectWindow` →
    `WorkflowTaskOrchestrator.StartWorkflowAsync`).
  - עדכון סטטוס משימה inline משדר ל-`WorkflowTaskOrchestrator.CheckAndAutoAdvanceAsync`
    (טריגרים `AllRequiredTasksClosed` + `TaskStatusChanged`).
  - סגירת משימה פותחת `TaskResultPickerDialog` ושומרת `LastTaskResultId` +
    `ProjectAssignmentEvent.TaskResultId`.
  - `WorkflowTransitionEvaluator` תומך ב-`TaskResultEquals` (משווה
    `ChangedTaskResultCode` ל-`TaskResultDefinition.Code`).
  - ה-UI מציג את התוצאה האחרונה בעמודה ב-`TaskPanelView` ובכרטיסים של
    `FloatingProjectTasksView`.

ההפניות בהמשך המסמך לזרמים הישנים (Design / Review / Opinion וכו') ול-.NET 8
נשארו כקונטקסט היסטורי בלבד.

---

## מה ניתחתי

קראתי ועיבדתי את כל המסמכים הבאים:

| מסמך | תוכן מרכזי |
|---|---|
| `0_SYSTEM_MASTER_SPEC.md` | אפיון מלא: מודולים, ישויות, יחסים, כללי מימוש, סדר ביצוע |
| `01_Copilot_General_Instructions.md` | עקרונות עבודה: layers, flow, Workflow ≠ Task |
| `02_Database_Migration_Rules.md` | לעולם לא ליצור migrations אוטומטית |
| `03_System_Architecture_Implementation_Spec.md` | סדר מימוש, key services, Use Case pattern |
| `04_System_Vision_Big_Picture.md` | Pipeline: Email → Context → Workflow → Work → Files → Review → Delivery |
| `05_Domain_Model_Map.md` | 7 domain areas, entities, relationships, architectural rules |
| `06_Solution_Folder_Structure.md` | folder rules, naming rules, dependency rules |
| `0 אפיון תהליך עבודה למיילים וקבצים.txt` | זרימת עבודה מלאה: מייל → ACC Inbox → תיוג → העתקה לפרויקט |
| `0 אפיון משימות ממיילים – מטריצת Wor.txt` | מטריצת Workflows: Design / Review / Opinion, משימות לפי שיוך |

בנוסף, סרקתי את ה-codebase כדי להבין מה **כבר קיים** ומה **צריך לבנות**.

---

## מצב נוכחי — מה קיים בקוד

### ✅ קיים ועובד

| רכיב | מיקום | סטטוס |
|---|---|---|
| **Models — flat folder** | `SiNetSQL/Models/` (אין תיקיות משנה) | ~50 entities בתיקייה שטוחה |
| **Services — חלקית מאורגן** | `SiNetSQL/Services/` + תיקיות: `EmailIngestion/`, `InspectionSync/`, `AccBootstrap/`, `SeedData/`, `TaskImport/` | שירותים קיימים עובדים |
| **MVVM — flat** | `SiNetSQL/MVVM/` (רק `interface/` כתיקיית משנה) | ~30 ViewModels בתיקייה שטוחה |
| **Data/Configurations** | `SiNetSQL/Data/Configurations/` | 12 configuration files |
| **DTOs** | **לא קיים** | אין תיקיית DTOs כלל |
| **TaskLink (polymorphic)** | `Models/TaskLink.cs` | 5 entity types, 4 roles — **עובד** |
| **EmailContextService** | `Services/EmailContextService.cs` | **בסיסי** — רק ThreadStatusMapping lookup |
| **EmailIngestionService** | `Services/EmailIngestion/` | **מלא** — lease-based, dedup, ACC upload |
| **TaskService** | `Services/TaskService.cs` | **מלא** — CRUD + priority + events |
| **ProjectWorkViewModel** | `MVVM/ProjectWorkViewModel.cs` | **מלא** — Unified Tree, naming convention, alternatives/versions |
| **Inspection system** | Models + `InspectionSync/` + `FloatingInspectionViewModel` | **מלא** |
| **Decisions** | `ProjectDecision` + `ProjectDecisionService` | **מלא** |
| **DI Registration** | `App.xaml.cs` — `ConfigureServices()` | Factory pattern, Singleton/Transient |

### 🔴 לא קיים — צריך לבנות

| רכיב | סטטוס |
|---|---|
| **Workflow entities** (Definition, Stage, TransitionRule, Instance, StageTransition) | לא קיים כלל |
| **Workflow enums** (WorkflowStatus, WorkflowTriggerType) | לא קיים |
| **WorkflowEngine service** | לא קיים |
| **WorkflowQueryService** | לא קיים |
| **Email Context Analyzer** (מנוע הקשר מלא) | לא קיים — רק ThreadStatusMapping |
| **SuggestedActionsBuilder** | לא קיים |
| **ActionExecutor** | לא קיים |
| **FileImportCoordinator** | לא קיים |
| **DTOs** (EmailContext, SuggestedAction) | לא קיים |
| **Workflow ViewModels** | לא קיים |
| **Workflow Views** | לא קיים |
| **EmailContextPanel** (UI) | לא קיים |
| **TaskLinkEntityType.WorkflowInstance = 6** | לא קיים — צריך הרחבה |

---

## מה אני הולך לעשות — סדר ביצוע

הסדר מבוסס על מסמך `03_System_Architecture_Implementation_Spec.md` + תלויות טכניות:

```
Phase 1 ──→ Phase 2 ──→ Phase 3 ──→ Phase 4 ──→ Phase 5 ──→ Phase 6
 (DB)       (Engine)     (Context)   (Actions)   (Files)      (UI)
```

---

## Phase 1: Workflow Foundation — ישויות DB

**מטרה:** ליצור את כל הישויות של ה-Workflow + EF Configuration + הרחבת TaskLink.

### מה ייווצר:

```
SiNetSQL/Models/
├── WorkflowDefinition.cs          ← תבנית תהליך (שם, תיאור, סוג)
├── WorkflowStageDefinition.cs     ← שלב בתבנית (סדר, שם, תיאור)
├── WorkflowTransitionRule.cs      ← חוק מעבר בין שלבים
├── WorkflowInstance.cs            ← מופע תהליך (per project)
├── WorkflowStageTransition.cs     ← היסטוריית מעבר
├── WorkflowStatus.cs              ← enum (Draft, Active, Paused, Completed, Cancelled)
└── WorkflowTriggerType.cs         ← enum (Manual, Email, System)

SiNetSQL/Data/Configurations/
└── WorkflowConfiguration.cs       ← Fluent API: relationships, indexes, constraints
```

### שינויים ברכיבים קיימים:

| רכיב | שינוי |
|---|---|
| `TaskLinkEntityType` enum | הוספת `WorkflowInstance = 6` |
| `SiNetSQLDbContext` | הוספת 5 DbSets חדשים: `WorkflowDefinitions`, `WorkflowStageDefinitions`, `WorkflowTransitionRules`, `WorkflowInstances`, `WorkflowStageTransitions` |
| `Project` model | הוספת navigation property: `ICollection<WorkflowInstance>` |

### כללי מיקום (לפי 06_Solution_Folder_Structure):

המסמך מציע `Models/Workflow/` כתיקיית משנה. אבל המצב בפועל:
- **Models — תיקייה שטוחה** (כל ~50 הקבצים ב-`Models/` ישירות)
- אין תקדים לתיקיות משנה ב-Models

**ההחלטה:** אני אתאים לקונבנציה הקיימת — **קבצים ישירות ב-`Models/`**, כי:
1. זה מה שקיים ועובד
2. שינוי מבנה תיקיות של 50 קבצים הוא refactor גדול ומיותר כרגע
3. ה-prefix `Workflow` בשם הקובץ מספיק לארגון

### Migration:

> **⚠️ לא אריץ migration.** אספק את ה-Entity + Configuration, ואכתוב הערה מפורשת:
> "Database migration must be created manually by the developer."

---

## Phase 2: Workflow Engine — לוגיקה עסקית

**מטרה:** בניית מנוע תהליכים שמנהל מחזור חיים מלא.

### מה ייווצר:

```
SiNetSQL/Services/Workflow/
├── WorkflowEngine.cs              ← Start, Advance, Pause, Complete, Cancel
├── WorkflowQueryService.cs        ← GetActiveByProject, GetHistory, GetDefinitions
├── WorkflowValidationService.cs   ← Validate transitions, check rules
└── WorkflowSeedService.cs         ← Seed default definitions (Design, Review, Opinion)
```

### WorkflowEngine — API:

| Method | תיאור |
|---|---|
| `StartAsync(definitionId, projectId, triggerType, triggerEntityId?, userId, ct)` | יצירת WorkflowInstance חדש |
| `AdvanceStageAsync(instanceId, targetStageId, userId, notes?, ct)` | מעבר לשלב הבא |
| `PauseAsync(instanceId, userId, notes?, ct)` | השהיה |
| `CompleteAsync(instanceId, userId, notes?, ct)` | סיום |
| `CancelAsync(instanceId, userId, notes?, ct)` | ביטול |

### WorkflowSeedService — תבניות ברירת מחדל:

לפי מסמך `0 אפיון משימות ממיילים`:

| Workflow | שלבים |
|---|---|
| **Design** | קבלת חומר → בדיקת התכנות → תכנון → בקרה פנימית → הגשה → אישור |
| **Review** | קליטת חומר → בדיקה מקצועית → כתיבת הערות → שליחת הערות → מעקב תיקון → קליטת גרסה מתוקנת → אישור/סגירה |
| **Opinion** | קליטת חומר → ניתוח → הכנת טיוטה → בדיקה פנימית → שליחה → סגירה |
| **Intake** (קליטת מייל) | זיהוי → סיווג → הפניה → טיפול |
| **Scope Expansion** | בקשה → הערכה → אישור/דחייה |

### DI Registration:

הוספה ל-`App.xaml.cs` — `ConfigureServices()`:
```
services.AddTransient<WorkflowEngine>();
services.AddTransient<WorkflowQueryService>();
services.AddTransient<WorkflowValidationService>();
services.AddTransient<WorkflowSeedService>();
```

---

## Phase 3: Email Context Engine — ניתוח הקשר

**מטרה:** מנגנון שמקבל EmailInboxMessage ומחזיר הקשר עסקי מלא.

### מה ייווצר:

```
SiNetSQL/DTOs/
└── Email/
    ├── EmailContext.cs              ← DTO: project, types, workflows, tasks, attachments
    └── ContextConfidence.cs         ← enum: High, Medium, Low

SiNetSQL/Services/EmailContext/
└── EmailContextAnalyzer.cs         ← Main analyzer service
```

### EmailContext DTO — מה הוא מכיל:

| Property | Type | מקור |
|---|---|---|
| `EmailMessageId` | int | הקלט |
| `Project` | Project? | `EmailInboxMessage.ProjectId` → load |
| `ProjectTypes` | List<JobType> | `TypeOfProjectInProject` by project |
| `WorkflowFamily` | string? | מיפוי: ProjectType → "Design"/"Review"/"Opinion" (מהמטריצה) |
| `ActiveWorkflows` | List<WorkflowInstance> | Phase 2 query |
| `RelatedTasks` | List<ProjectAssignment> | TaskLink lookup |
| `RelatedDecisions` | List<ProjectDecision> | TaskLink lookup |
| `AttachmentAnalysis` | AttachmentAnalysisResult | כמות, סוגים, naming match |
| `IsAssociatedToProject` | bool | `ProjectId != null` |
| `Confidence` | ContextConfidence | חישוב |

### לוגיקה מרכזית (מבוסס על מסמך המטריצה):

**ציר החלטה ראשי:** `IsAssociatedToProject`?
- **לא** → "פתיחה / שיוך / איסוף / החלטה / תיעוד"
- **כן** → בדוק `WorkflowFamily` → "Design" / "Review" / "Opinion" → משימות ספציפיות

### שינוי ברכיב קיים:

`EmailContextService` הקיים ממשיך לחיות — הוא עדיין משמש לצביעת threads.
`EmailContextAnalyzer` הוא שירות **חדש ונפרד** שמבצע ניתוח מלא.

---

## Phase 4: Suggested Actions Engine — הצעות פעולה

**מטרה:** בהתבסס על EmailContext, מציע למשתמש רשימת פעולות אפשריות.

### מה ייווצר:

```
SiNetSQL/DTOs/Email/
├── SuggestedAction.cs              ← DTO: ActionType, DisplayText, Confidence, PrefilledData
└── SuggestedActionType.cs          ← enum

SiNetSQL/Services/EmailContext/
├── SuggestedActionsBuilder.cs      ← Rule engine: context → actions
└── ActionExecutor.cs               ← Coordinator: executes selected action
```

### SuggestedActionType — ערכים (מהמטריצה):

**מייל לא משויך:**
- `CreateNewProject` — פתיחת פרויקט חדש
- `CreatePriceQuote` — פתיחת הצעת מחיר
- `CreateNewReview` — פתיחת בדיקה חדשה
- `CreateOpinionProject` — פתיחת חוות דעת
- `AssociateToExistingProject` — שיוך לפרויקט קיים
- `CollectMaterial` — איסוף חומר
- `ForwardToDecision` — העברה להחלטה
- `FileOnly` — תיוק בלבד

**מייל משויך — משותפות:**
- `AddMaterialToProject` — הוספת חומר
- `RequestCompletion` — בקשת השלמות
- `PrepareResponse` — הכנת מענה
- `InternalReview` — העברה לבדיקה פנימית
- `AddNewDiscipline` — הוספת תחום חדש

**משויך — Design:**
- `HandleComments` — טיפול בהערות
- `UploadNewVersion` — העלאת גרסה
- `UpdateDesign` — עדכון תכנון
- `PrepareSubmission` — הכנת הגשה
- `CoordinateWithConsultants` — תיאום עם יועצים
- `SendUpdatedMaterial` — שליחת חומר מעודכן
- `ReceiveSupplementaryMaterial` — קליטת חומר משלים

**משויך — Review:**
- `ReceiveMaterialForReview` — קליטת חומר
- `OpenReviewRound` — פתיחת סבב בדיקה
- `PerformReview` — ביצוע בדיקה
- `WriteComments` — כתיבת הערות
- `SendComments` — שליחת הערות
- `TrackCorrections` — מעקב תיקונים
- `ReceiveCorrectedVersion` — קליטת גרסה מתוקנת
- `ApproveOrClose` — אישור/סגירה

**משויך — Opinion:**
- `ReceiveMaterialForOpinion` — קליטת חומר
- `AnalyzeDocuments` — ניתוח מסמכים
- `RequestMissingMaterial` — בקשת חומר חסר
- `PrepareDraftOpinion` — הכנת טיוטה
- `UpdateOpinion` — עדכון חוות דעת
- `SendOpinion` — שליחת חוות דעת
- `CloseOpinion` — סגירה

### ActionExecutor — Coordinator:

מקבל `SuggestedAction` שהמשתמש בחר ומפנה לשירות המתאים:
- `StartWorkflow` → `WorkflowEngine.StartAsync`
- `CreateTask` → `TaskService.CreateAsync`
- `ImportFiles` → `FileImportCoordinator.ImportAsync` (Phase 5)
- `AssociateToProject` → `EmailContextService` update
- וכו'

---

## Phase 5: File Import Pipeline — ייבוא קבצים

**מטרה:** ייבוא קובץ ממייל לתוך ProjectWork tree.

### מה ייווצר:

```
SiNetSQL/Services/Coordinators/
└── FileImportCoordinator.cs        ← Orchestrate: attachment → naming → copy → tag
```

### Flow:

```
1. Input: EmailInboxAttachment + target ProjectFolder
2. Read: attachment file data (from ACC Inbox or local)
3. Build: BaseFileVersion (naming convention)
4. Copy: file to ProjectPath + correct subfolder
5. Update: Attachment.ProjectFileId (tag)
6. Result: imported file path → ProjectWork tree refreshes via FileSystemWatcher
```

### מבוסס על `0 אפיון תהליך עבודה`:

- הקבצים כבר ב-ACC Inbox (שלב קיים)
- FileImportCoordinator מטפל בהעתקה ל-Filesystem + Naming Convention
- FileSystemWatcher של ProjectWork מזהה אוטומטית ומרענן את העץ

---

## Phase 6: UI Integration — ממשק משתמש

**מטרה:** שילוב כל המנגנונים ב-UI.

### מה ייווצר:

```
SiNetSQL/MVVM/
├── EmailContextViewModel.cs       ← Context + Suggested Actions panel
├── WorkflowDashboardViewModel.cs  ← רשימת תהליכים פעילים
└── WorkflowInstanceViewModel.cs   ← תהליך בודד — שלבים, היסטוריה

SiNetProjectManager/WPFUserControl/
├── EmailContextPanel.xaml (+.cs)  ← Context panel (integrates into EmailManagementView)
└── WorkflowDashboardView.xaml     ← Dashboard

SiNetProjectManager/WPF_Window/
├── WorkflowInstanceWindow.xaml    ← Instance detail
└── FileImportDialog.xaml          ← File import from email
```

### שינויים ברכיבים קיימים:

| רכיב | שינוי |
|---|---|
| `EmailManagementView.xaml` | הוספת אזור ל-EmailContextPanel |
| `MainWindow` menu | הוספת פריט "תהליכי עבודה" |
| `App.xaml.cs` — `ConfigureServices()` | רישום כל השירותים וה-ViewModels החדשים |

---

## סיכום כמותי — מה ייבנה

| Phase | קבצים חדשים | שינויים בקיימים |
|---|---|---|
| **1 — Workflow Foundation** | 9 files (7 models + 1 config + seed) | 3 (TaskLink enum, DbContext, Project) |
| **2 — Workflow Engine** | 4 files (4 services) | 1 (App.xaml.cs DI) |
| **3 — Email Context** | 3 files (1 DTO folder + 2 files + 1 service) | 0 |
| **4 — Suggested Actions** | 4 files (2 DTOs + 2 services) | 0 |
| **5 — File Import** | 1 file (coordinator) | 0 |
| **6 — UI Integration** | 7 files (3 VMs + 4 Views) | 3 (EmailView, MainWindow, DI) |
| **סה"כ** | **~28 קבצים חדשים** | **~7 שינויים** |

---

## כללים שאשמור עליהם

| כלל | מקור |
|---|---|
| ❌ לעולם לא אריץ migration | `02_Database_Migration_Rules.md` |
| ❌ לא אשנה קוד עובד ללא הנחיה | `.github/copilot-instructions.md` |
| ✅ `CancellationToken` בכל async | `.github/copilot-instructions.md` |
| ✅ `IDbContextFactory` — לא DbContext ישיר | קונבנציה קיימת |
| ✅ ViewModels רזים — לוגיקה ב-Services | `01_Copilot_General_Instructions.md` |
| ✅ Workflow ≠ Task | `05_Domain_Model_Map.md` |
| ✅ Email = trigger only | `04_System_Vision_Big_Picture.md` |
| ✅ DB = metadata, Filesystem = files | Master Spec |
| ✅ אתאים למבנה תיקיות קיים | ניתוח codebase |
| ✅ PascalCase / _camelCase | `.github/copilot-instructions.md` |

---

## מה אני **לא** אעשה

1. **לא אשנה מבנה תיקיות של Models** — הם שטוחים וזה עובד
2. **לא אפנה ישויות קיימות לתיקיות משנה** — refactor מיותר
3. **לא ארחיב שירותים קיימים שעובדים** (כמו `EmailIngestionService`) — אלא אוסיף חדשים
4. **לא אריץ `Add-Migration`** — אספק description בלבד
5. **לא אמציא מודולים שלא הוגדרו** במסמכי האפיון

---

## הצעד הבא

כשתאשר, אתחיל ב-**Phase 1: Workflow Foundation** — יצירת הישויות, Enums, EF Configuration, והרחבת TaskLink + DbContext.

אעשה את זה קובץ-קובץ, עם `run_build` בסוף לוודא שהכל מתקמפל.
