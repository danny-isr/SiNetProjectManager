# 📂 מסך "בעבודה 2" — תיעוד מקיף

> **מודול:** ProjectWork — ניהול קבצים בפרויקט  
> **סוג:** UserControl (WPF) + ViewModel  
> **תאריך:** יוני 2026

---

## 1. תיאור כללי

מסך **"בעבודה 2"** (ProjectWorkView) הוא חלון העבודה המרכזי של המשתמש לניהול קבצי פרויקט.
המסך מציג **עץ תיקיות ומסמכים היררכי** (Unified Tree) בצד ימין, עם **צופה ACC (WebView2)** בצד שמאל.

### מה עושים במסך הזה?
- **בוחרים פרויקט** דרך ComboBox חכם (עם חיפוש)
- **מסננים** לפי סוג פרויקט, סטטוס ומשתמש
- **צופים בעץ קבצים** שמשלב:
  - תיקיות DB (מוגדרות מראש) + תיקיות filesystem (שנוצרו ידנית)
  - קבצי פרויקט מזוהים לפי תבנית שם (Naming Convention)
  - אלטרנטיבות (עותקים שונים של אותו קובץ)
  - גרסאות (כל אלטרנטיבה יכולה להכיל מספר גרסאות)
  - קבצים חיצוניים ("לא משויך לפרויקט") — קבצים שלא תואמים לתבנית
- **גוררים ומשחררים** קבצים לעץ (Drag & Drop)
- **פותחים קבצים** בדאבל-קליק, שולפים לתוך ACC
- **WebView2 ACC** — מראה תצוגת קבצים מ-Autodesk Construction Cloud (כשממופה)

---

## 2. ארכיטקטורה — View ↔ ViewModel

```
ProjectWorkView.xaml                    (UI — WPF UserControl)
ProjectWorkView.xaml.cs                 (Code-behind — Drag events, DataContext init)
    │
    └── DataContext = ProjectWorkViewModel
         │
         ├── SiNetSQLDbContext (ישיר — לא IDbContextFactory)
         ├── IDialogService (דיאלוגים — AlternativeNameWindow)
         ├── ActiveProjectContext (Singleton — הפרויקט הפעיל)
         │
         ├── Collections:
         │    ├── Projects → ProjectsView (ICollectionView + Filter)
         │    ├── JobTypes, ProjectStatuses, Users (פילטרים)
         │    └── RootFolders (ObservableCollection<ProjectFolderNode>)
         │
         ├── Tree Node Types:
         │    ├── ProjectFolderNode (תיקייה — DB/User)
         │    │    ├── Children: ObservableCollection<ProjectFolderNode>
         │    │    └── FolderFiles: ObservableCollection<ProjectFileNode>
         │    ├── ProjectFileNode (קובץ מזוהה)
         │    │    └── Alternatives: ObservableCollection<AlternativeNode>
         │    ├── AlternativeNode (אלטרנטיבה — עותק של הקובץ)
         │    │    └── Versions: ObservableCollection<VersionNode>
         │    └── VersionNode (גרסה בודדת — קובץ פיזי)
         │
         └── Background:
              ├── FileSystemWatcher (מעקב שינויים)
              └── Task.Run() (סריקת filesystem)
```

---

## 3. מסד נתונים — מודלים רלוונטיים

### 3.1 `ProjectFolder` — תיקיות פרויקט

```
┌────────────────────────────────────────────────────────────┐
│                    ProjectFolders                           │
├──────────────┬──────────┬──────────────────────────────────┤
│ Column       │ Type     │ Description                      │
├──────────────┼──────────┼──────────────────────────────────┤
│ Id           │ int PK   │ מזהה ייחודי                      │
│ Title        │ string?  │ שם התיקייה                       │
│ Infolderid   │ int? FK  │ תיקיית אב (self-ref → ProjectFolder) │
│ SecurityLevel│ float?   │ רמת אבטחה (ACL)                 │
│ Modified     │ DateTime?│ תאריך עדכון                      │
│ Created      │ DateTime?│ תאריך יצירה                      │
│ AuthorId     │ int? FK  │ יוצר → Siuser                   │
│ EditorId     │ int? FK  │ עורך → Siuser                   │
├──────────────┴──────────┴──────────────────────────────────┤
│ Navigation Properties:                                      │
│  • Infolder → ProjectFolder (parent)                       │
│  • InverseInfolder → ICollection<ProjectFolder> (children) │
│  • ProjectFiles → ICollection<ProjectFile>                 │
│  • Author, Editor → Siuser                                 │
└────────────────────────────────────────────────────────────┘
```

**מבנה היררכי:** התיקיות מאורגנות כעץ דרך `Infolderid` (self-referencing).
- שורש: תיקייה עם `Infolder.Title == "תיקית הפרויקט"` (convention)
- ילדים: תיקיות שה-`Infolder` שלהן מצביעה לתיקייה מסוימת

### 3.2 `ProjectFile` — קבצי פרויקט (מטאדאטה)

```
┌────────────────────────────────────────────────────────────┐
│                      ProjectFiles                           │
├──────────────────┬──────────┬──────────────────────────────┤
│ Column           │ Type     │ Description                  │
├──────────────────┼──────────┼──────────────────────────────┤
│ Id               │ int PK   │ מזהה ייחודי                  │
│ Title            │ string?  │ שם הקובץ (ללא סיומת)        │
│ Number           │ float?   │ מספר סידורי של הקובץ         │
│ Des              │ string?  │ תיאור                        │
│ Folderid         │ int? FK  │ תיקייה → ProjectFolder       │
│ Typefile         │ string?  │ סוג קובץ ("docx", "pdf"...) │
│ TypeProjId       │ int? FK  │ סוג פרויקט → JobType         │
│ TemplateLocation │ string?  │ נתיב תבנית מקור              │
│ LookAtDes        │ bool?    │ האם להציג תיאור               │
│ OutSidData       │ bool?    │ האם נתונים חיצוניים          │
│ Modified         │ DateTime?│ תאריך עדכון                   │
│ Created          │ DateTime?│ תאריך יצירה                   │
│ AuthorId         │ int? FK  │ יוצר → Siuser                │
│ EditorId         │ int? FK  │ עורך → Siuser                │
├──────────────────┴──────────┴──────────────────────────────┤
│ Navigation Properties:                                      │
│  • Folder → ProjectFolder                                  │
│  • TypeProj → JobType (סוג פרויקט)                        │
│  • Author, Editor → Siuser                                 │
│  • ProjectFileRefFiles → ICollection<ProjectFileRef>       │
│  • ProjectFileRefXrefs → ICollection<ProjectFileRef>       │
├────────────────────────────────────────────────────────────┤
│ Partial Class Extension (ProjectFile.TagDisplay.cs):       │
│  • TagDisplayLabel → "FolderName / FileTitle" (for ComboBox)│
└────────────────────────────────────────────────────────────┘
```

**תפקיד:** `ProjectFile` הוא **הגדרה** של קובץ (מטאדאטה) — לא הקובץ הפיזי עצמו.
הקובץ הפיזי נמצא על הדיסק ומזוהה דרך ה-**Naming Convention** (ראה סעיף 5).

### 3.3 `ProjectFileRef` — הפניות בין קבצים (XRef)

```
┌────────────────────────────────────────────────────────────┐
│                    ProjectFileRefs                           │
├──────────────┬──────────┬──────────────────────────────────┤
│ Column       │ Type     │ Description                      │
├──────────────┼──────────┼──────────────────────────────────┤
│ Id           │ int PK   │ מזהה ייחודי                      │
│ Title        │ string?  │ כותרת ההפניה                     │
│ XrefId       │ int? FK  │ קובץ מפנה → ProjectFile          │
│ FileId       │ int? FK  │ קובץ מופנה → ProjectFile         │
│ Modified     │ DateTime?│                                   │
│ Created      │ DateTime?│                                   │
│ AuthorId     │ int? FK  │                                   │
│ EditorId     │ int? FK  │                                   │
└────────────────────────────────────────────────────────────┘
```

**תפקיד:** קישור XRef בין קבצי CAD/BIM — קובץ אחד מפנה לקובץ אחר.

### 3.4 `TypeOfProjectInProject` — שיוך סוג פרויקט

```
┌────────────────────────────────────────────────────────────┐
│                TypeOfProjectInProjects                       │
├──────────────────┬──────────┬──────────────────────────────┤
│ Column           │ Type     │ Description                  │
├──────────────────┼──────────┼──────────────────────────────┤
│ Id               │ int PK   │ מזהה ייחודי                  │
│ Title            │ string?  │ כותרת                        │
│ ProjectTypeId    │ int? FK  │ סוג פרויקט → JobType         │
│ ProjectId        │ int? FK  │ פרויקט → Project             │
│ AdminWorkerId    │ int? FK  │ עובד אחראי → Siuser         │
│ Modified/Created │ DateTime?│                               │
│ AuthorId/EditorId│ int? FK  │                               │
├──────────────────┴──────────┴──────────────────────────────┤
│ Navigation: Project, ProjectType (JobType), AdminWorker    │
└────────────────────────────────────────────────────────────┘
```

**תפקיד:** טבלת many-to-many בין `Project` ל-`JobType`.
פרויקט אחד יכול להיות **כמה סוגים** (אדריכלות + קונסטרוקציה, למשל).
כל שיוך כולל **עובד אחראי** (`AdminWorkerId`).

### 3.5 תרשים יחסים

```
Project ←──(1:N)──→ TypeOfProjectInProject ──(N:1)──→ JobType
   │                        │
   │                     AdminWorker → Siuser
   │
   ├──(1:N)──→ ProjectFolder ──(self-ref: Infolderid)
   │                │
   │                └──(1:N)──→ ProjectFile ──→ JobType (TypeProjId)
   │                                │
   │                                └──(1:N)──→ ProjectFileRef (XRef)
   │
   └── ProjectPath (filesystem root for this project)
```

---

## 4. ViewModel — `ProjectWorkViewModel`

### 4.1 אתחול (Constructor)

```csharp
public ProjectWorkViewModel(IDialogService dialogs)
{
    _db = new SiNetSQLDbContext();   // ⚠ DbContext ישיר (לא Factory)
    
    // טעינה מקדימה של כל הטבלאות הנדרשות:
    _db.Projects.Load();
    _db.ProjectFolders.Load();
    _db.ProjectFiles.Load();
    _db.Places.Load();
    _db.Companies.Load();
    _db.JobTypes.Load();
    _db.ProjectStatuses.Load();
    _db.Siusers.Load();
    _db.TypeOfProjectInProjects.Load();
    
    // יצירת CollectionView לסינון:
    Projects = _db.Projects.Local.ToObservableCollection();
    ProjectsView = CollectionViewSource.GetDefaultView(Projects);
    ProjectsView.Filter = FilterProject;
    
    // פילטרים עם "כולם" כאפשרות ראשונה:
    JobTypes = [{ Id=-1, Title="סנן סוג פרויקט" }, ...sorted...];
    ProjectStatuses = [{ Id=-1, Title="סנן סטטוס" }, ...sorted...];
    Users = [{ Id=-1, Name="סנן משתמש" }, ...active users with email...];
}
```

### 4.2 פילטרים

| Property | סוג | תיאור |
|---|---|---|
| `SelectedJobTypeFilter` | JobType | סינון לפי סוג פרויקט (דרך TypeOfProjectInProject) |
| `SelectedStatusFilter` | ProjectStatus | סינון לפי סטטוס פרויקט |
| `SelectedUserFilter` | Siuser | סינון לפי עובד אחראי (AdminWorker ב-TypeOfProjectInProject) |
| `ProjectFilterPredicate` | Predicate<object>? | Predicate מחושב שמועבר ל-SearchableProjectSelector |

**לוגיקת סינון:**
```
FilterProject(Project p):
  ✓ JobType: p.TypeOfProjectInProjects.Any(x => x.ProjectTypeId == selected.Id)
  ✓ Status: p.ProjectStatusId == selected.Id
  ✓ User: p.TypeOfProjectInProjects.Any(x => x.AdminWorkerId == selected.Id)
  (כל פילטר רק אם Id > 0 — אחרת "הכל")
```

### 4.3 בחירת פרויקט

```csharp
SelectedProject = value
    → ActiveProjectContext.Instance.SetActiveProject(value)  // עדכון global
    → StopWatchingAll()                                       // ביטול FileSystemWatchers
    → LoadUnifiedTree()                                       // טעינת עץ חדש
    → StartWatchingRoots()                                    // הפעלת watchers חדשים
    → UpdateProjectDetails()                                  // עדכון שורת פרטי פרויקט
```

### 4.4 פרטי פרויקט

מוצגים ב-3 שורות:
```
שורה 1: פרויקט: {NameAndNumber} | כותרת: {Title} | מזמין: {Company} | מקום: {Place}
שורה 2: סוג: {Types} | סטטוס: {Status} | משתמש: {AuthorId} | עובד: {Worker}
שורה 3: תאריכים: {Start}–{End} | מנהל: {Admin} | נתיב: {ProjectPath}
```

---

## 5. Naming Convention — תבנית שם קובץ

### מבנה שם הקובץ

```
(ProjectNumber)-ProjectType-FileNumber-Alternative-Version-Name.extension
```

**דוגמה:**
```
(1234)-5-10-1-3-report.docx
  │     │  │  │ │   │     │
  │     │  │  │ │   │     └─ סיומת (.docx)
  │     │  │  │ │   └─ שם מקורי (Name)
  │     │  │  │ └─ מספר גרסה (Version = 3)
  │     │  │  └─ שם אלטרנטיבה (Alternative = "1")
  │     │  └─ מספר קובץ (FileNumber = 10)
  │     └─ סוג פרויקט/JobType (ProjectType = 5)
  └─ מספר פרויקט (ProjectNumber = 1234)
```

### `BaseFileVersion` — פרסור שם קובץ

מחלקה `[NotMapped]` שמפרסרת שם קובץ לפי התבנית:

| Property | Type | תיאור |
|---|---|---|
| `ProjectNumber` | int | מספר פרויקט (מתוך הסוגריים) |
| `ProjectType` | int | סוג פרויקט (JobType.Id) |
| `Number` | int | מספר קובץ |
| `Alternative` | string | שם אלטרנטיבה (יכול לכלול `~date` לתקופה) |
| `Version` | int | מספר גרסה |
| `Name` | string | שם מקורי |
| `Extension` | string | סיומת |
| `FileInfo` | FileInfo | מידע על הקובץ הפיזי |
| `FileName` | string | בניית שם מחדש מהפרמטרים |

**פרסור:** אם הקובץ לא מתחיל ב-`(` → כל השדות = -1 → מסווג כ"לא משויך".

**בנייה (Constructors):**
- `BaseFileVersion(FileInfo)` — פרסור שם קובץ קיים
- `BaseFileVersion(projectNumber, projectType, fileNumber, alternative, version, name, extension)` — יצירת שם חדש

---

## 6. עץ אחיד (Unified Tree)

### 6.1 מבנה ההיררכיה

```
RootFolders (ObservableCollection<ProjectFolderNode>)
│
├── 📁 ProjectFolderNode (תיקייה — DB defined)
│   ├── 📁 ProjectFolderNode (תת-תיקייה — DB)
│   │   ├── 📁 ProjectFolderNode (תיקייה שמשתמש יצר — filesystem only)
│   │   ├── 📄 ProjectFileNode (קובץ מזוהה)
│   │   │   ├── 🔹 AlternativeNode ("1")
│   │   │   │   ├── 📋 VersionNode (v3 — 2.1 MB — 01/06/2026)
│   │   │   │   ├── 📋 VersionNode (v2 — 1.8 MB — 15/05/2026)
│   │   │   │   └── 📋 VersionNode (v1 — 1.5 MB — 01/05/2026)
│   │   │   └── 🔹 AlternativeNode ("תיקון")
│   │   │       └── 📋 VersionNode (v1 — 2.0 MB — 20/05/2026)
│   │   └── 📄 ProjectFileNode ("לא משויך לפרויקט" — external)
│   │       ├── 🔹 AlternativeNode (".dwg")
│   │       │   └── 📋 VersionNode (old-drawing.dwg)
│   │       └── 🔹 AlternativeNode (".pdf")
│   │           └── 📋 VersionNode (scan001.pdf)
│   └── 📄 ProjectFileNode (קובץ נוסף...)
│
└── 📁 ProjectFolderNode (תיקיית שורש נוספת)
    └── ...
```

### 6.2 `CompositeChildrenConverter` — איחוד ילדים

תיקייה (`ProjectFolderNode`) מכילה **שתי אוספים**:
- `Children` — תיקיות ילדות (`ProjectFolderNode`)
- `FolderFiles` — קבצים בתוך התיקייה (`ProjectFileNode`)

ה-TreeView דורש `ItemsSource` אחד → `CompositeChildrenConverter` ממזג את שניהם:

```xaml
<MultiBinding Converter="{selectors:CompositeChildrenConverter}">
    <Binding Path="Children"/>
    <Binding Path="FolderFiles"/>
    <Binding Path="Children.Count"/>     <!-- trigger refresh -->
    <Binding Path="FolderFiles.Count"/>  <!-- trigger refresh -->
</MultiBinding>
```

### 6.3 תהליך טעינת העץ

```
LoadUnifiedTree()
│
├── 1. Clear RootFolders
├── 2. Cancel previous CancellationToken
├── 3. Load DB folders:
│   ├── all = ProjectFolders.Where(Title != "תיקית הפרויקט")
│   └── roots = folders.Where(Infolder.Title == "תיקית הפרויקט")
│
├── 4. Prepare data on UI thread (no DbContext on background):
│   ├── validTypes = TypeOfProjectInProjects for current project
│   ├── dbMap = Dictionary<(TypeProjId, Number), ProjectFile>
│   └── folderFilesMap = Dictionary<FolderId, List<ProjectFile>>
│
├── 5. Build folder tree (UI thread):
│   └── foreach root → CreateFolderNode → BuildFolderTree (recursive)
│
└── 6. Task.Run() — background filesystem scan:
    └── foreach root:
        ├── ScanUserFolders(node) — discover filesystem-only directories
        └── LoadFilesIntoFolderNode(node, ...) — scan + classify files
            └── LoadFilesRecursive(children, ...) — recurse into sub-folders
```

### 6.4 סיווג קבצים (File Classification)

עבור כל קובץ ב-filesystem:

```
File → BaseFileVersion(FileInfo) → Parse name
│
├── Version/ProjectNumber/ProjectType = -1?
│   └── YES → "לא משויך" (ExcludeReason: "התבנית לא מתאימה")
│
├── ProjectNumber ≠ SelectedProject.Id?
│   └── YES → "לא משויך" (ExcludeReason: "מספר הפרויקט לא מתאים")
│
├── dbMap[(ProjectType, Number)] exists?
│   ├── YES → Matched to ProjectFile!
│   │   ├── Find/Create AlternativeNode by alternative name
│   │   ├── Filter: _recover files → external
│   │   ├── Filter: duplicate version → external
│   │   └── Add VersionNode (sorted desc by version number)
│   │
│   └── NO → "לא משויך" (no DB definition for this key)
│
└── Excluded extensions? (.bak, .dwl, .ini, .tmp, .log, .exe...)
    └── YES → Skip entirely
```

### 6.5 סיומות חסומות

```csharp
private static readonly HashSet<string> ExcludedExtensions =
{
    ".bak", ".dwt", ".dwl", ".dwl2", ".ini", ".$ds", 
    ".err", ".tmp", ".log", ".exe"
};
```

---

## 7. צמתי עץ (Tree Nodes)

### 7.1 `ProjectFolderNode` — תיקייה

| Property | Type | תיאור |
|---|---|---|
| `Id` | int | DB Id (חיובי = DB, שלילי = user/filesystem) |
| `Title` | string | שם התיקייה |
| `FullPath` | string | נתיב מלא (מורכב: ProjectPath + parent path + title) |
| `IsUserCreated` | bool | true = תיקייה שנוצרה ע"י משתמש (לא ב-DB) |
| `Parent` | ProjectFolderNode? | תיקיית אב |
| `Children` | ObservableCollection<ProjectFolderNode> | תת-תיקיות |
| `FolderFiles` | ObservableCollection<ProjectFileNode> | קבצים בתיקייה |
| `projectFolderData` | ProjectFolder | מודל DB מקורי |
| `IsExpanded` | bool | מצב פתיחה ב-TreeView |
| `IsSelected` | bool | מצב בחירה |

**פקודות ContextMenu (תיקיית DB):**
- "פתח תיקייה" → `FolderOpener.OpenFolder(FullPath)`
- "צור תיקייה" → `CreateSubdirectoryInteractive` + הוספה לעץ
- "שמור לזיכרון" → Clipboard copy

**פקודות ContextMenu (תיקיית User):**
- + "שנה שם" → `FolderOpener.RenameFolderWithInputBox`
- + "מחק תיקייה" → אישור + `Directory.Delete(recursive: true)`

**אבטחה:** בעת הגדרת `FullPath`, אם התיקייה היא DB-defined:
```csharp
FolderOpener.EnsureDirectoryExists(path, configureDirectory: di =>
    FolderOpener.SetFolderSecurityForGroup(di.FullName, "SI-ENG\\שרטטים"));
```
→ ACL: הקבוצה יכולה ליצור קבצים, אבל **לא למחוק** את התיקייה עצמה.

### 7.2 `ProjectFileNode` — קובץ מזוהה

| Property | Type | תיאור |
|---|---|---|
| `FileName` | string | שם הקובץ (Title מ-DB) |
| `Extension` | string | סיומת קובץ |
| `projectNumber` | int | מספר פרויקט נבחר |
| `projectFile` | ProjectFile | מודל DB |
| `Parent` | ProjectFolderNode | תיקיית אב |
| `IsExternal` | bool | true = "לא משויך לפרויקט" |
| `Alternatives` | ObservableCollection<AlternativeNode> | אלטרנטיבות |
| `AlternativeCount` | int | computed: Alternatives.Count |
| `VersionCount` | int | computed: Sum of all alternative versions |

**פקודות (קובץ רגיל):**
- "אלטרנטיבה נוספת מתבנית" → העתקת תבנית + יצירת שם חדש
- "אלטרנטיבה נוספת" → בחירה מ-file dialog

**פקודות (קובץ חיצוני):**
- "קבל קובץ" → `PickAndCopyFile`

**יצירת אלטרנטיבה:**
```csharp
GetAlternativeNode(name, sourceFile):
  1. IsSameFileGroup(dbType, sourceFile) — ולידציית סוג קובץ
  2. new BaseFileVersion(projectNumber, typeId, fileNumber, name, maxVersion+1, ...)
  3. FileHelpers.CopyFile(source, parent.FullPath + newFileName)
  4. FileHelpers.OpenFile(newFilePath)
```

### 7.3 `AlternativeNode` — אלטרנטיבה

| Property | Type | תיאור |
|---|---|---|
| `AlternativeName` | string | שם האלטרנטיבה ("1", "תיקון", ".dwg") |
| `Parent` | ProjectFileNode | קובץ אב |
| `Versions` | ObservableCollection<VersionNode> | גרסאות |
| `DisplayIcon` | ImageSource | אייקון: si.ico (internal) או Shell icon (external) |
| `VersionCount` | int | computed |
| `LatestDate` | DateTime? | computed: Max date from versions |

**פקודות:**
- "פתח גרסה אחרונה" → `OpenFile(MaxBy(VersionNumber).FullPath)`
- "שנה שם אלטרנטיבה" → dialog + `FileHelpers.RenameAlternative`
- "מחק אלטרנטיבה" → `FileHelpers.DeleteAlternativeFiles` (כל הגרסאות)

### 7.4 `VersionNode` — גרסה בודדת

| Property | Type | תיאור |
|---|---|---|
| `VersionNumber` | int | מספר גרסה |
| `Size` | string | גודל ("2.10 MB") |
| `Date` | string | תאריך שינוי אחרון |
| `Description` | string | שם הקובץ המלא |
| `FullPath` | string | נתיב מלא לקובץ |
| `Icon` | string | אייקון |
| `FileVersion` | BaseFileVersion | מידע פרסור |
| `ExcludeReason` | string? | סיבת חריגה (null = תקין) |
| `Parent` | AlternativeNode | אלטרנטיבה אב |

**פקודות (קובץ רגיל):**
- "פתח גרסה" → `FileHelpers.OpenFile(FullPath)`
- "גרסה חדשה" → Compare/version
- "מחק גרסה" → `FileHelpers.DeleteFile(FullPath)`
- "שמור לזיכרון" → Clipboard copy

**פקודות (קובץ חיצוני):**
- "פתח קובץ", "שנה שם", "מחק קובץ"
- "חלץ קובץ" (רק ל-.zip) → `FileHelpers.ExtractZipFile`

---

## 8. FileSystemWatcher — מעקב שינויים

```csharp
StartWatchingRoots():
  foreach root in RootFolders:
    if Directory.Exists(root.FullPath):
      new FileSystemWatcher(root.FullPath)
        IncludeSubdirectories = true
        NotifyFilter = DirectoryName | FileName | LastWrite
        Events: Created, Renamed, Deleted → OnFsChanged

OnFsChanged():
  Dispatcher.Invoke(() => 
    LoadUnifiedTree()     // reload everything
    StartWatchingRoots()  // recreate watchers
  )
```

**כשמחליפים פרויקט:** `StopWatchingAll()` → מנתק ומשחרר את כל ה-watchers.

---

## 9. Drag & Drop

### 9.1 Drag (גרירה מהעץ)

```csharp
// Code-behind: ProjectWorkView.xaml.cs
TreeViewItem_MouseMove():
  if LeftButton pressed + moved enough:
    if DataContext is VersionNode with FullPath:
      DragDrop.DoDragDrop(item, FileDrop data, DragDropEffects.Copy)
```

→ אפשר **לגרור גרסה** מהעץ לתוך Explorer או אפליקציה אחרת.

### 9.2 Drop (שחרור לעץ)

משתמש ב-**`FileDropBehavior`** (Behavior<TreeView>):

```csharp
OnDrop():
  1. Get dropped files (DataFormats.FileDrop)
  2. Validate: only 1 file (AllowMultipleFiles=False)
  3. Identify target node via HitTest
  4. WaitUntilFileReadyAsync (10 attempts × 100ms)
  5. Create FileDropInfo { FilePath, TargetNode }
  6. Execute HandleFileDropCommand

ViewModel.OnFileDropped(FileDropInfo):
  if targetNode is AlternativeNode:
    → Add version to existing alternative
  if targetNode is ProjectFileNode:
    → Show AlternativeNameDialog → Create new alternative
  else:
    → "אלמנט היעד לא מזוהה"
```

### 9.3 Double-Click

```csharp
TreeViewItem_MouseDoubleClick():
  if DataContext is VersionNode:
    FileHelpers.OpenFile(version.FullPath)
```

---

## 10. WebView2 — ACC Viewer

צד שמאל של המסך מכיל **WebView2** שאמור להציג את קבצי הפרויקט ב-ACC:

```xaml
<wv2:WebView2 selectors:WebView2Helper.NavigateUrl="{Binding AccViewerUrl}"/>
```

**מצב נוכחי:** `AccViewerUrl = null` (TODO — ממתין למיפוי ACC Project ID).

כשאין URL → מוצגת הודעה:
```
🔗
חיבור ACC לא מוגדר לפרויקט זה
לאחר מיפוי מזהה ACC, קבצי הפרויקט יוצגו כאן
```

---

## 11. שירותי עזר (Helper Classes)

### 11.1 `FileHelpers` (Static)

| Method | תיאור |
|---|---|
| `OpenFile(path)` | פתיחת קובץ (+ הסרת ReadOnly) |
| `CopyFile(src, dst)` | העתקת קובץ |
| `DeleteFile(path)` | מחיקה עם אישור |
| `RenameFile(old, new)` | שינוי שם |
| `IsFileLocked(path)` | בדיקת נעילה |
| `IsSameFileGroup(type, file)` | בדיקת תאימות סוג (Word↔docx↔pdf) |
| `PickAndCopyFile(dir)` | file dialog + copy |
| `PickAndCopyFile(dir, types, name)` | file dialog + filter + copy with name |
| `ExpandExtensions(choices)` | הרחבת קבוצות סיומות |
| `RenameAlternative(alt, name)` | שינוי שם אלטרנטיבה + כל הגרסאות |
| `DeleteAlternativeFiles(alt)` | מחיקת כל הגרסאות באלטרנטיבה |
| `ExtractZipFile(path)` | חילוץ ZIP לתיקייה |
| `ExtractZipRecursivelyAsync(path)` | חילוץ רקורסיבי (nested ZIPs) |

**קבוצות סוגי קבצים:**
| קבוצה | סיומות |
|---|---|
| doc | doc, docx, docm, dot, dotx, dotm, pdf |
| xls | xls, xlsx, xlsm, xlsb, xlt, xltx, xltm, pdf |
| ppt | ppt, pptx, pptm, pot, potx, potm, pps, ppsx, ppsm, pdf |
| PDF | pdf |
| DWF | dwf, dwfx, pdf |
| tif | tif, dwfx, pdf |

### 11.2 `FolderOpener` (Static)

| Method | תיאור |
|---|---|
| `OpenFolder(path)` | פתיחה ב-Explorer |
| `RenameFolderWithInputBox(path)` | InputBox + Directory.Move |
| `CreateSubdirectoryInteractive(parent)` | InputBox + Directory.CreateDirectory |
| `EnsureDirectoryExists(path, callback)` | יצירת תיקיות חסרות + ACL |
| `SetFolderSecurityForGroup(path, group)` | Deny Delete + Allow Create |

### 11.3 `ClipboardHelper` (Static)

| Method | תיאור |
|---|---|
| `CopyFolderPathToClipboard(path)` | Clipboard.SetText |
| `CopyFilePathToClipboard(path)` | Clipboard.SetText |

### 11.4 `AlternativeNameViewModel` — Dialog

ViewModel לדיאלוג שם אלטרנטיבה, עם ולידציה:
- **Required** — שם לא ריק
- **Max 18 characters**
- **No dash** (`-`) — כי מפריד בתבנית שם
- **No illegal filename chars**
- **Unique** — לא קיים כבר באלטרנטיבות

---

## 12. ממשק משתמש (UI Layout)

```
┌────────────────────────────────────────────────────────────┐
│ Row 0: Filters                                              │
│ [🔍 SearchableProjectSelector] [סוג] [סטטוס] [משתמש]     │
├────────────────────────────────────────────────────────────┤
│ Row 1: Project Details                                      │
│ פרויקט: 1234 אורלנד | כותרת: ... | מזמין: ... | מקום: ...│
│ סוג: אדריכלות, קונסטרוקציה | סטטוס: בעבודה | ...        │
│ תאריכים: 01/01/2025–31/12/2026 | נתיב: D:\Projects\...   │
├────────────────────────────────────────────────────────────┤
│ Row 2: Main Content (split)                                 │
│                                                             │
│ ┌─────────────────────┐│┌──────────────────────────────┐   │
│ │ 📁 Unified TreeView ││ │ 📁 ACC - קבצי פרויקט       │   │
│ │                     ││ │ (Autodesk Construction Cloud)│   │
│ │ 📁 תיקיית אב       ││ ├──────────────────────────────┤   │
│ │  ├── 📁 תת-תיקייה   ││ │                              │   │
│ │  │   ├── 📄 report   ││ │   🔗                         │   │
│ │  │   │  ├─ 🔹 Alt 1  ││ │   חיבור ACC לא מוגדר       │   │
│ │  │   │  │  ├─ v3     ││ │   לפרויקט זה                 │   │
│ │  │   │  │  ├─ v2     ││ │                              │   │
│ │  │   │  │  └─ v1     ││ │                              │   │
│ │  │   │  └─ 🔹 Alt 2  ││ │        [WebView2]           │   │
│ │  │   │     └─ v1     ││ │                              │   │
│ │  │   └── 📄 external  ││ │                              │   │
│ │  └── 📁 תיקיית user  ││ │                              │   │
│ └─────────────────────┘│└──────────────────────────────┘   │
│                   GridSplitter                              │
└────────────────────────────────────────────────────────────┘
```

### Templates ב-TreeView

| DataType | Template | תצוגה |
|---|---|---|
| `ProjectFolderNode` | HierarchicalDataTemplate | 📁 + Title (Bold) + icon: project/user |
| `ProjectFileNode` | HierarchicalDataTemplate | 📄 + FileName (Bold) + (Extension) + Alt count + Ver count |
| `AlternativeNode` | HierarchicalDataTemplate | Icon + AlternativeName (Bold) + (X גרסאות) |
| `VersionNode` | DataTemplate (leaf) | Icon + Description + Ver + Size + Date |

---

## 13. סיכום רכיבים

```
┌─────────────────────────────────────────────────────────┐
│                 ProjectWork Module                        │
├─────────────────────────────────────────────────────────┤
│ DB Models (4):                                           │
│  • ProjectFolder (self-ref tree)                        │
│  • ProjectFile (metadata — not physical file)           │
│  • ProjectFileRef (XRef links)                          │
│  • TypeOfProjectInProject (Project ↔ JobType M:N)       │
├─────────────────────────────────────────────────────────┤
│ Tree Nodes (4):                                          │
│  • ProjectFolderNode (folder — DB + user-created)       │
│  • ProjectFileNode (identified file + alternatives)     │
│  • AlternativeNode (named copy variant)                 │
│  • VersionNode (physical file instance)                 │
├─────────────────────────────────────────────────────────┤
│ ViewModel (1):                                           │
│  • ProjectWorkViewModel (~683 lines)                    │
├─────────────────────────────────────────────────────────┤
│ Helpers (4):                                             │
│  • BaseFileVersion (filename parser/builder)            │
│  • FileHelpers (static — CRUD files)                    │
│  • FolderOpener (static — folder operations + ACL)      │
│  • ClipboardHelper (static)                             │
├─────────────────────────────────────────────────────────┤
│ Behaviors (1):                                           │
│  • FileDropBehavior (TreeView drag & drop)              │
├─────────────────────────────────────────────────────────┤
│ Dialogs (1):                                             │
│  • AlternativeNameViewModel (name input + validation)   │
├─────────────────────────────────────────────────────────┤
│ Converters (1):                                          │
│  • CompositeChildrenConverter (merge folders + files)   │
├─────────────────────────────────────────────────────────┤
│ View (1):                                                │
│  • ProjectWorkView.xaml + .cs (UserControl)             │
├─────────────────────────────────────────────────────────┤
│ External:                                                │
│  • WebView2 ACC viewer (placeholder — pending mapping)  │
│  • FileSystemWatcher (live reload on changes)           │
│  • ActiveProjectContext (global project selection)       │
└─────────────────────────────────────────────────────────┘
```
