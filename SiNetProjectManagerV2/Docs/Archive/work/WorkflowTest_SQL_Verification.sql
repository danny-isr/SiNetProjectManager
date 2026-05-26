-- ═══════════════════════════════════════════════════════════════════════════════
-- Workflow End-to-End Test — SQL Verification Script
-- ═══════════════════════════════════════════════════════════════════════════════
-- 
-- איך להשתמש:
-- 1. הריצו כל בלוק STEP אחרי שביצעתם את הפעולה המתאימה ב-UI
-- 2. השוו את התוצאות לעמודת "מה אמור לקרות"
-- 3. אם משהו חסר — בדקו ב-Output Window של VS את הלוגים
--
-- טיפ: החליפו את הערכים בשורות SET @... לפי מה שקיבלתם
-- ═══════════════════════════════════════════════════════════════════════════════


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║  STEP 0: בדיקת תנאים מוקדמים (הריצו לפני הכל)                         ║
-- ║  ודאו שה-Seed Data קיים בטבלאות                                        ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝

-- 0A: TaskBehaviorDefinitions — צריך לראות MaterialFiling + ProfessionalReview
SELECT Id, Code, DisplayName, IsActive, AutoCreateOnTrigger, AutoCloseOnCompletion, TaskTypeId
FROM TaskBehaviorDefinitions;

-- 0B: TaskTriggerRules — צריך לראות EmailAssignedToProject + AttachmentTagged
SELECT tr.Id, tr.BehaviorDefinitionId, bd.Code AS BehaviorCode, tr.TriggerType, tr.IsActive
FROM TaskTriggerRules tr
JOIN TaskBehaviorDefinitions bd ON bd.Id = tr.BehaviorDefinitionId;

-- 0C: TaskCompletionRules — צריך לראות AllAttachmentsTagged + EmailReplySent
SELECT cr.Id, cr.BehaviorDefinitionId, bd.Code AS BehaviorCode, 
       cr.CompletionType, cr.ResultingStatusId, s.Name AS ResultingStatusName, cr.IsActive
FROM TaskCompletionRules cr
JOIN TaskBehaviorDefinitions bd ON bd.Id = cr.BehaviorDefinitionId
LEFT JOIN ProjectAssignmentStatus s ON s.Id = cr.ResultingStatusId;

-- 0D: TaskType — צריך לראות MaterialFiling + ProfessionalReview (בנוסף ל-General, OfficePlanning, PlanReview)
SELECT Id, Code, Name FROM TaskType ORDER BY Id;

-- 0E: WorkflowDefinition — צריך לראות Design (תהליך תכנון)
SELECT Id, Code, Name, IsActive FROM WorkflowDefinition WHERE Code = 'Design';

-- 0F: שלבי ה-Design Workflow — צריך 13 שלבים
SELECT sd.Id, sd.Code, sd.Name, sd.SortOrder, sd.IsInitial, sd.IsFinal
FROM WorkflowStageDefinition sd
JOIN WorkflowDefinition wd ON wd.Id = sd.WorkflowDefinitionId
WHERE wd.Code = 'Design'
ORDER BY sd.SortOrder;

-- 0G: מעברים (Transitions) של Design — צריך מעבר ליניארי בין כל שלב
SELECT tr.Id, 
       fs.Name AS FromStage, fs.SortOrder AS FromOrder,
       ts.Name AS ToStage, ts.SortOrder AS ToOrder,
       tr.Name AS TransitionName
FROM WorkflowTransitionRule tr
JOIN WorkflowStageDefinition fs ON fs.Id = tr.FromStageId
JOIN WorkflowStageDefinition ts ON ts.Id = tr.ToStageId
JOIN WorkflowDefinition wd ON wd.Id = tr.WorkflowDefinitionId
WHERE wd.Code = 'Design'
ORDER BY fs.SortOrder;

-- 0H: ProjectTypeWorkflowDefinition — בדקו שיש מיפוי בין סוג הפרויקט ל-Design workflow
-- *** חשוב: IsDefault=1 וגם IsEnabled=1 ***
SELECT ptwd.Id, ptwd.ProjectTypeId, jt.Title AS ProjectTypeName,
       ptwd.WorkflowDefinitionId, wd.Name AS WorkflowName,
       ptwd.IsDefault, ptwd.IsEnabled
FROM ProjectTypeWorkflowDefinition ptwd
JOIN JobType jt ON jt.Id = ptwd.ProjectTypeId
JOIN WorkflowDefinition wd ON wd.Id = ptwd.WorkflowDefinitionId;

-- 0I: בדקו שלפרויקט שלכם יש TypeOfProject שמקושר ל-Workflow
-- *** החליפו <ProjectId> ב-Id של הפרויקט שלכם ***
DECLARE @MyProjectId INT = 0; -- ← שנו לערך הנכון
SELECT tp.Id, tp.ProjectId, tp.ProjectTypeId, jt.Title AS ProjectTypeName,
       p.Title AS ProjectName
FROM TypeOfProjectInProject tp
JOIN JobType jt ON jt.Id = tp.ProjectTypeId
JOIN Projects p ON p.Id = tp.ProjectId
WHERE tp.ProjectId = @MyProjectId;


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║  STEP 1: אחרי קליטת מייל (Email Ingested)                              ║
-- ║  הריצו אחרי שמייל עם צרופות נקלט במערכת                                ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝

-- 1A: EmailInboxMessage — שורה חדשה צריכה להיווצר
-- שימו לב: ProjectId צריך להיות של פרויקט ברירת המחדל (Inbox), Status=Uploaded
SELECT TOP 5
    Id, MessageUniqueId, ProjectId, 
    Status, Subject, FromAddress,
    ReceivedUtc, CreatedByLogin, CreatedAtUtc,
    InboxAccProjectId, InboxAccFolderId
FROM EmailInboxMessage
ORDER BY Id DESC;

-- 1B: EmailInboxAttachment — שורה לכל צרופה
-- שימו לב: AccItemId צריך להיות מלא (הועלה ל-ACC), ProjectFileId=NULL (עדיין לא תויק)
-- *** החליפו <MessageId> ב-Id של המייל מ-1A ***
DECLARE @MessageId INT = 0; -- ← שנו לערך מ-1A
SELECT Id, MessageId, AttachmentIndex, OriginalFileName, SavedFileName,
       ContentSha256, AccItemId, AccVersionId, ProjectFileId
FROM EmailInboxAttachment
WHERE MessageId = @MessageId
ORDER BY AttachmentIndex;


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║  STEP 2: אחרי שיוך מייל לפרויקט (Email → Project)                     ║
-- ║  הריצו אחרי שלחצתם "שייך לפרויקט" ב-UI                                ║
-- ║                                                                         ║
-- ║  מה אמור לקרות אוטומטית:                                               ║
-- ║  • EmailInboxMessage.ProjectId משתנה                                    ║
-- ║  • משימת MaterialFiling נוצרת (ProjectAssignment)                       ║
-- ║  • TaskLink נוצר (מייל ← משימה)                                        ║
-- ║  • WorkflowInstance נוצר (תהליך תכנון מופעל)                            ║
-- ║  • TaskLink נוסף (משימה ← WorkflowInstance)                             ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝

DECLARE @MsgId2 INT = 0;      -- ← Id של EmailInboxMessage (מ-Step 1A)
DECLARE @ProjId2 INT = 0;     -- ← Id של הפרויקט ששייכתם אליו

-- 2A: EmailInboxMessage.ProjectId עודכן לפרויקט החדש
SELECT Id, MessageUniqueId, ProjectId, Status
FROM EmailInboxMessage
WHERE Id = @MsgId2;

-- 2B: משימת MaterialFiling נוצרה
-- צריך לראות: TaskTypeId של MaterialFiling, StatusId של "פתוח", Title מתחיל ב-"AUTO-"
SELECT pa.Id AS TaskId, pa.ProjectId, pa.AssignedToId, 
       pa.TaskTypeId, tt.Code AS TaskTypeCode, tt.Name AS TaskTypeName,
       pa.StatusId, s.Name AS StatusName, s.IsOpen, s.IsActionable,
       pa.Title, pa.WorkPriority, pa.Created
FROM ProjectAssignment pa
JOIN TaskType tt ON tt.Id = pa.TaskTypeId
JOIN ProjectAssignmentStatus s ON s.Id = pa.StatusId
WHERE pa.ProjectId = @ProjId2
  AND tt.Code = 'MaterialFiling'
ORDER BY pa.Id DESC;

-- 2C: אירוע "Created" נרשם למשימה
-- *** החליפו <TaskId> ב-Id מ-2B ***
DECLARE @FilingTaskId2 INT = 0; -- ← Id של משימת MaterialFiling מ-2B
SELECT Id, ProjectAssignmentId, EventType, OldStatusId, NewStatusId,
       CreatedByUserId, CreatedDate, Note
FROM ProjectAssignmentEvent
WHERE ProjectAssignmentId = @FilingTaskId2
ORDER BY CreatedDate;

-- 2D: TaskLink — קישור מייל ← משימה
-- צריך לראות: LinkedEntityType=4 (EmailInboxMessage), Role=Trigger
SELECT Id, TaskId, LinkedEntityType, LinkedEntityId, Role, Description, CreatedAtUtc
FROM TaskLink
WHERE TaskId = @FilingTaskId2
ORDER BY Id;

-- 2E: ★ WorkflowInstance — תהליך עבודה הופעל אוטומטית ★
-- צריך לראות: Status=Active(1), CurrentStageId=שלב ראשון (קבלת חומר), TriggerType=Email(1)
SELECT wi.Id AS InstanceId, 
       wi.WorkflowDefinitionId, wd.Name AS WorkflowName,
       wi.ProjectId, wi.Status,
       wi.CurrentStageId, cs.Name AS CurrentStageName, cs.SortOrder,
       wi.TriggerType, wi.TriggerEntityId,
       wi.CreatedByUserId, wi.CreatedAtUtc, wi.Notes
FROM WorkflowInstance wi
JOIN WorkflowDefinition wd ON wd.Id = wi.WorkflowDefinitionId
LEFT JOIN WorkflowStageDefinition cs ON cs.Id = wi.CurrentStageId
WHERE wi.ProjectId = @ProjId2
ORDER BY wi.Id DESC;

-- 2F: WorkflowStageTransition — כניסה לשלב הראשון
-- *** החליפו <InstanceId> ב-Id מ-2E ***
DECLARE @WfInstanceId2 INT = 0; -- ← Id של WorkflowInstance מ-2E
SELECT wst.Id, wst.WorkflowInstanceId, wst.FromStageId, wst.ToStageId,
       fs.Name AS FromStageName, ts.Name AS ToStageName,
       wst.TransitionedByUserId, wst.TransitionedAtUtc, wst.Notes
FROM WorkflowStageTransition wst
LEFT JOIN WorkflowStageDefinition fs ON fs.Id = wst.FromStageId
JOIN WorkflowStageDefinition ts ON ts.Id = wst.ToStageId
WHERE wst.WorkflowInstanceId = @WfInstanceId2
ORDER BY wst.TransitionedAtUtc;

-- 2G: ★ TaskLink — משימה מקושרת גם ל-WorkflowInstance ★
-- צריך לראות 2 שורות: אחת ל-EmailInboxMessage(4) ואחת ל-WorkflowInstance(6)
SELECT Id, TaskId, LinkedEntityType, LinkedEntityId, Role, Description, CreatedAtUtc
FROM TaskLink
WHERE TaskId = @FilingTaskId2
ORDER BY Id;


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║  STEP 3: אחרי תיוג צרופה (Tag Attachment)                              ║
-- ║  הריצו אחרי שבחרתם ProjectFile לצרופה אחת                              ║
-- ║                                                                         ║
-- ║  מה אמור לקרות:                                                        ║
-- ║  • EmailInboxAttachment.ProjectFileId מתעדכן                            ║
-- ║  • משימת ProfessionalReview נוצרת (אם קיים Behavior)                   ║
-- ║  • אם לא כל הצרופות תויקו — MaterialFiling נשאר "פתוח"                ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝

DECLARE @MsgId3 INT = 0;   -- ← Id של EmailInboxMessage
DECLARE @ProjId3 INT = 0;  -- ← Id של הפרויקט

-- 3A: EmailInboxAttachment — בדקו ProjectFileId (אמור להתמלא לצרופה שתויקה)
SELECT Id, MessageId, AttachmentIndex, OriginalFileName, 
       ProjectFileId, AccItemId
FROM EmailInboxAttachment
WHERE MessageId = @MsgId3
ORDER BY AttachmentIndex;

-- 3B: משימת ProfessionalReview — אמורה להיווצר אוטומטית
SELECT pa.Id AS TaskId, pa.ProjectId,
       tt.Code AS TaskTypeCode, tt.Name AS TaskTypeName,
       s.Name AS StatusName, s.IsOpen,
       pa.Title, pa.Created
FROM ProjectAssignment pa
JOIN TaskType tt ON tt.Id = pa.TaskTypeId
JOIN ProjectAssignmentStatus s ON s.Id = pa.StatusId
WHERE pa.ProjectId = @ProjId3
  AND tt.Code = 'ProfessionalReview'
ORDER BY pa.Id DESC;

-- 3C: MaterialFiling — צריך עדיין להיות "פתוח" (אם לא כל הצרופות תויקו)
SELECT pa.Id AS TaskId, tt.Code, s.Name AS StatusName, s.IsOpen
FROM ProjectAssignment pa
JOIN TaskType tt ON tt.Id = pa.TaskTypeId
JOIN ProjectAssignmentStatus s ON s.Id = pa.StatusId
WHERE pa.ProjectId = @ProjId3
  AND tt.Code = 'MaterialFiling'
ORDER BY pa.Id DESC;


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║  STEP 4: אחרי תיוג כל הצרופות (All Attachments Tagged)                ║
-- ║  הריצו אחרי שכל הצרופות קיבלו ProjectFileId                           ║
-- ║                                                                         ║
-- ║  מה אמור לקרות אוטומטית:                                               ║
-- ║  • MaterialFiling task → Status="הושלם", WorkPriority=NULL              ║
-- ║  • ProjectAssignmentEvent עם EventType="StatusChanged"                  ║
-- ║  • ★ WorkflowInstance.CurrentStageId מתקדם לשלב הבא ★                  ║
-- ║  • ★ WorkflowStageTransition חדש נרשם ★                                ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝

DECLARE @MsgId4 INT = 0;          -- ← Id של EmailInboxMessage
DECLARE @ProjId4 INT = 0;         -- ← Id של הפרויקט
DECLARE @FilingTaskId4 INT = 0;   -- ← Id של משימת MaterialFiling (מ-Step 2B)
DECLARE @WfInstanceId4 INT = 0;   -- ← Id של WorkflowInstance (מ-Step 2E)

-- 4A: כל הצרופות תויקו? (ProjectFileId NOT NULL לכולם)
SELECT Id, OriginalFileName, ProjectFileId,
       CASE WHEN ProjectFileId IS NOT NULL THEN '✓' ELSE '✗' END AS Tagged
FROM EmailInboxAttachment
WHERE MessageId = @MsgId4
ORDER BY AttachmentIndex;

-- 4B: ★ MaterialFiling — סטטוס אמור להיות "הושלם", WorkPriority=NULL ★
SELECT pa.Id AS TaskId, tt.Code, 
       s.Name AS StatusName, s.IsOpen,
       pa.WorkPriority
FROM ProjectAssignment pa
JOIN TaskType tt ON tt.Id = pa.TaskTypeId
JOIN ProjectAssignmentStatus s ON s.Id = pa.StatusId
WHERE pa.Id = @FilingTaskId4;

-- 4C: אירוע StatusChanged נרשם
SELECT pae.Id, pae.EventType, pae.OldStatusId, pae.NewStatusId,
       os.Name AS OldStatusName, ns.Name AS NewStatusName,
       pae.CreatedDate, pae.Note
FROM ProjectAssignmentEvent pae
LEFT JOIN ProjectAssignmentStatus os ON os.Id = pae.OldStatusId
LEFT JOIN ProjectAssignmentStatus ns ON ns.Id = pae.NewStatusId
WHERE pae.ProjectAssignmentId = @FilingTaskId4
ORDER BY pae.CreatedDate;

-- 4D: ★★★ WorkflowInstance — CurrentStageId אמור להתקדם לשלב הבא ★★★
-- אם עבד: SortOrder יהיה 2 (תיוק חומר) במקום 1 (קבלת חומר)
SELECT wi.Id AS InstanceId, wi.Status,
       wi.CurrentStageId, cs.Name AS CurrentStageName, cs.SortOrder
FROM WorkflowInstance wi
LEFT JOIN WorkflowStageDefinition cs ON cs.Id = wi.CurrentStageId
WHERE wi.Id = @WfInstanceId4;

-- 4E: ★ WorkflowStageTransition — אמורות להיות 2 שורות: כניסה ראשונית + מעבר ★
SELECT wst.Id, wst.WorkflowInstanceId, 
       fs.Name AS FromStage, fs.SortOrder AS FromOrder,
       ts.Name AS ToStage, ts.SortOrder AS ToOrder,
       wst.TransitionedAtUtc, wst.Notes
FROM WorkflowStageTransition wst
LEFT JOIN WorkflowStageDefinition fs ON fs.Id = wst.FromStageId
JOIN WorkflowStageDefinition ts ON ts.Id = wst.ToStageId
WHERE wst.WorkflowInstanceId = @WfInstanceId4
ORDER BY wst.TransitionedAtUtc;


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║  BONUS: סיכום כל המשימות בפרויקט                                       ║
-- ║  הריצו בכל שלב לראות תמונה מלאה                                       ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝

DECLARE @ProjIdSummary INT = 0;  -- ← Id של הפרויקט

SELECT pa.Id AS TaskId, 
       tt.Code AS TaskType, tt.Name AS TaskTypeName,
       s.Name AS Status, s.IsOpen,
       pa.WorkPriority,
       pa.Title, pa.Created,
       (SELECT COUNT(*) FROM TaskLink tl 
        WHERE tl.TaskId = pa.Id AND tl.LinkedEntityType = 4) AS EmailLinks,
       (SELECT COUNT(*) FROM TaskLink tl 
        WHERE tl.TaskId = pa.Id AND tl.LinkedEntityType = 6) AS WorkflowLinks
FROM ProjectAssignment pa
LEFT JOIN TaskType tt ON tt.Id = pa.TaskTypeId
JOIN ProjectAssignmentStatus s ON s.Id = pa.StatusId
WHERE pa.ProjectId = @ProjIdSummary
ORDER BY pa.Id;
