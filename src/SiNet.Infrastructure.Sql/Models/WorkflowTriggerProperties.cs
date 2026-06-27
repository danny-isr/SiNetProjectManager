using System.Text.Json.Serialization;

namespace SiNetSQL.Models;

/// <summary>
/// Base class for typed Workflow Start Trigger properties.
/// Each <see cref="WorkflowStartTriggerSource"/> has its own derived class
/// with strongly-typed fields relevant to that trigger.
/// 
/// Serialized to JSON in <see cref="WorkflowStartTrigger.PropertiesJson"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ManualStartProperties), "ManualStart")]
[JsonDerivedType(typeof(EmailFiledProperties), "EmailFiled")]
[JsonDerivedType(typeof(ProjectCreatedProperties), "ProjectCreated")]
[JsonDerivedType(typeof(ProjectTypeAssignedProperties), "ProjectTypeAssigned")]
[JsonDerivedType(typeof(ParentWorkflowProperties), "ParentWorkflow")]
[JsonDerivedType(typeof(ScheduledTimerProperties), "ScheduledTimer")]
[JsonDerivedType(typeof(ApiCallProperties), "ApiCall")]
public abstract class WorkflowTriggerProperties
{
    /// <summary>Factory: create the correct Properties instance for a given trigger source.</summary>
    public static WorkflowTriggerProperties CreateDefault(WorkflowStartTriggerSource source) => source switch
    {
        WorkflowStartTriggerSource.ManualStart => new ManualStartProperties(),
        WorkflowStartTriggerSource.EmailFiled => new EmailFiledProperties(),
        WorkflowStartTriggerSource.ProjectCreated => new ProjectCreatedProperties(),
        WorkflowStartTriggerSource.ProjectTypeAssigned => new ProjectTypeAssignedProperties(),
        WorkflowStartTriggerSource.ParentWorkflow => new ParentWorkflowProperties(),
        WorkflowStartTriggerSource.ScheduledTimer => new ScheduledTimerProperties(),
        WorkflowStartTriggerSource.ApiCall => new ApiCallProperties(),
        _ => new ManualStartProperties(),
    };
}

// ═══════════════════════════════════════════════════════════════════════
//  🖱️ הפעלה ידנית
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Properties for ManualStart trigger.
/// Defines who can start the workflow manually and whether a project is required.
/// </summary>
public class ManualStartProperties : WorkflowTriggerProperties
{
    /// <summary>האם חובה לבחור פרויקט בעת ההפעלה.</summary>
    public bool RequiresProject { get; set; } = true;

    /// <summary>תפקידים מורשים להפעלה (null = כולם מורשים).</summary>
    public List<string>? AllowedRoles { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  📧 תיוק מייל
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Properties for EmailFiled trigger.
/// Defines what happens when an email is filed — based on its type.
/// 
/// מצבים עיקריים:
///   • הזמנת עבודה — RequiresProject=true, RequiredAttachments=true
///   • חומר לתכנון — RequiresProject=true, RequiredAttachments=true
///   • הצעת מחיר — RequiresProject=false, CreateProjectIfMissing=true
///   • בקשת שינוי — RequiresProject=true
///   • תכתובת כללית — RequiresProject=false
/// </summary>
public class EmailFiledProperties : WorkflowTriggerProperties
{
    /// <summary>סוג התיוק שמפעיל את הטריגר.</summary>
    public EmailFileType EmailFileType { get; set; } = EmailFileType.General;

    /// <summary>האם התיוק חייב פרויקט קיים? (false = יכול בלי פרויקט).</summary>
    public bool RequiresProject { get; set; }

    /// <summary>אם אין פרויקט — ליצור אוטומטית? (רלוונטי להצעת מחיר).</summary>
    public bool CreateProjectIfMissing { get; set; }

    /// <summary>אם יוצרים פרויקט — איזה סוג? ("Proposal", "Planning", ...).</summary>
    public string? NewProjectTypeCode { get; set; }

    /// <summary>האם חובה קבצים מצורפים בתיוק?</summary>
    public bool RequiredAttachments { get; set; }

    /// <summary>תיאור חופשי — מוצג למשתמש בדיזיינר.</summary>
    public string? Note { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  📁 פרויקט נוצר
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Properties for ProjectCreated trigger.
/// Fires when a new project is created in the system — optionally filtered by type.
/// </summary>
public class ProjectCreatedProperties : WorkflowTriggerProperties
{
    /// <summary>רק עבור סוגי פרויקט אלו (null = כל הסוגים).</summary>
    public List<string>? ProjectTypeFilter { get; set; }

    /// <summary>האם להתחיל את התהליך מיד או לחכות?</summary>
    public bool AutoStart { get; set; } = true;
}

// ═══════════════════════════════════════════════════════════════════════
//  📋 סוג פרויקט הוגדר / השתנה
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Properties for ProjectTypeAssigned trigger.
/// Fires when a project's type changes — optionally filtered by from/to types.
/// </summary>
public class ProjectTypeAssignedProperties : WorkflowTriggerProperties
{
    /// <summary>מאיזה סוג (null = כל שינוי).</summary>
    public string? FromTypeCode { get; set; }

    /// <summary>לאיזה סוג (חובה).</summary>
    public string? ToTypeCode { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  🔄 תהליך אב (SubWorkflow)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Properties for ParentWorkflow trigger.
/// This workflow is started by another workflow as a sub-workflow.
/// Cannot be started independently.
/// </summary>
public class ParentWorkflowProperties : WorkflowTriggerProperties
{
    /// <summary>מאילו תהליכים אב ניתן להפעיל (null = כולם).</summary>
    public List<int>? AllowedParentDefinitionIds { get; set; }

    /// <summary>האם לרשת את ProjectId מהתהליך האב.</summary>
    public bool InheritProjectId { get; set; } = true;

    /// <summary>פרמטרים שחייבים לעבור מהאב (שמות).</summary>
    public List<string>? RequiredParameters { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  ⏰ טיימר מתוזמן (עתידי)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Properties for ScheduledTimer trigger (future).
/// </summary>
public class ScheduledTimerProperties : WorkflowTriggerProperties
{
    /// <summary>ביטוי Cron או interval.</summary>
    public string? CronExpression { get; set; }

    /// <summary>תיאור חופשי.</summary>
    public string? Note { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
//  🔌 קריאת API (עתידי)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Properties for ApiCall trigger (future).
/// </summary>
public class ApiCallProperties : WorkflowTriggerProperties
{
    /// <summary>מפתח/Route שמזהה את הקריאה.</summary>
    public string? EndpointKey { get; set; }

    /// <summary>פרמטרים צפויים.</summary>
    public List<string>? ExpectedParameters { get; set; }
}
