using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Clean baseline seed for <see cref="Models.ProjectAssignmentStatus"/>.
/// <para>
/// Generic execution statuses ONLY. Professional/business outcomes
/// (e.g. "מאושר תנועתית", "ממתין לתיקון הערות", "בתכנון") must be modeled as
/// <see cref="Models.TaskResultDefinition"/>, <see cref="Models.WorkflowStageDefinition"/>,
/// or inspection statuses — not as TaskStatus rows.
/// </para>
/// <para>
/// <b>IsOpen</b>: Task is still alive (not completed/cancelled). Includes waiting states.<br/>
/// <b>IsActionable</b>: Ball is in our court — the office has work to do.
/// </para>
/// </summary>
public static class ProjectAssignmentStatusSeedData
{
    public static readonly StatusDefinition[] Definitions = new[]
    {
        new StatusDefinition(TaskStatusCodes.Open,              "פתוח",                  IsOpen: true,  IsActionable: true,  SortOrder: 10),
        new StatusDefinition(TaskStatusCodes.InProgress,        "בעבודה",                IsOpen: true,  IsActionable: true,  SortOrder: 20),
        new StatusDefinition(TaskStatusCodes.WaitingExternal,   "ממתין לגורם חיצוני",     IsOpen: true,  IsActionable: false, SortOrder: 30),
        new StatusDefinition(TaskStatusCodes.WaitingAssignment, "ממתין לשיוך",            IsOpen: true,  IsActionable: false, SortOrder: 40),
        new StatusDefinition(TaskStatusCodes.Blocked,           "חסום",                  IsOpen: true,  IsActionable: false, SortOrder: 50),
        new StatusDefinition(TaskStatusCodes.Completed,         "הושלם",                 IsOpen: false, IsActionable: false, SortOrder: 60),
        new StatusDefinition(TaskStatusCodes.Cancelled,         "בוטל",                  IsOpen: false, IsActionable: false, SortOrder: 70),
        new StatusDefinition(TaskStatusCodes.Skipped,           "דולג / לא נדרש",         IsOpen: false, IsActionable: false, SortOrder: 80),
    };

    /// <summary>
    /// Represents a ProjectAssignmentStatus seed definition.
    /// <see cref="Code"/> is the stable machine identifier; <see cref="Name"/> is the display label.
    /// </summary>
    public record StatusDefinition(string Code, string Name, bool IsOpen, bool IsActionable, int SortOrder);
}
