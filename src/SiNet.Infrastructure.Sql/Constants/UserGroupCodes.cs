namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable, workflow-agnostic <see cref="Models.UserGroup.Code"/> values.
/// <para>
/// Groups that are meaningful across more than one workflow (e.g. office
/// management, senior management, planners) live here so seed data, workflow
/// stage assignments, and stage-task templates share a single source of truth.
/// </para>
/// <para>
/// Workflow-specific group codes (e.g. Review’s <c>ReviewIntake</c>) remain
/// in their per-workflow seed-data files (e.g.
/// <see cref="Services.SeedData.ReviewUserGroupCodes"/>).
/// </para>
/// </summary>
public static class UserGroupCodes
{
    /// <summary>ניהול משרד — אחראי על ניהול שוטף, פתיחת פרויקטים, תיוק, סגירת פרויקטים.</summary>
    public const string OfficeManagement = "OfficeManagement";

    /// <summary>הנהלה בכירה — בדיקות שלמות חומר, אישור הצעות, החלטות עסקיות.</summary>
    public const string SeniorManagement = "SeniorManagement";

    /// <summary>מתכננים — תכנון, הכנת הצעות מחיר, בדיקה מקצועית.</summary>
    public const string Planners = "Planners";
}
