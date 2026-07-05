using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Per-<c>ProjectType</c> discipline activation profile.
/// <para>
/// Disciplines are not a global fixed list — each ProjectType activates the
/// <c>TaskType</c> rows that act as planning disciplines for it (e.g. Traffic,
/// Drainage, Physical, External Coordination). Disciplines are referenced by
/// <see cref="TaskTypeCodes"/>; the actual <c>TaskType.Id</c> is resolved at
/// seed time from <c>TaskType.Code</c>.
/// </para>
/// </summary>
public static class ProjectTypeDisciplineSeedData
{
    /// <summary>
    /// Default discipline profile for project types that don't match a more
    /// specific entry in <see cref="Profiles"/>.
    /// </summary>
    public static readonly DisciplineActivation[] DefaultProfile = new[]
    {
        new DisciplineActivation(TaskTypeCodes.GeneralPlanning,             IsRequired: true,  SortOrder: 10),
    };

    /// <summary>
    /// Per-project-type overrides. The first matching profile wins;
    /// <see cref="DefaultProfile"/> is used otherwise.
    /// </summary>
    public static readonly ProjectTypeDisciplineProfile[] Profiles = new[]
    {
        // Street development → traffic + drainage + physical disciplines.
        new ProjectTypeDisciplineProfile(
            new ProjectTypeMatch("פיתוח רחוב"),
            new[]
            {
                new DisciplineActivation(TaskTypeCodes.TrafficPlanning,             IsRequired: true,  SortOrder: 10),
                new DisciplineActivation(TaskTypeCodes.DrainagePlanning,            IsRequired: true,  SortOrder: 20),
                new DisciplineActivation(TaskTypeCodes.PhysicalPlanning,            IsRequired: true,  SortOrder: 30),
                new DisciplineActivation(TaskTypeCodes.ExternalPlannerCoordination, IsRequired: false, SortOrder: 40),
            }),

        // Traffic-arrangement → traffic-only.
        new ProjectTypeDisciplineProfile(
            new ProjectTypeMatch("הסדר תנועה"),
            new[]
            {
                new DisciplineActivation(TaskTypeCodes.TrafficPlanning,             IsRequired: true,  SortOrder: 10),
            }),

        // Drainage projects → drainage primary, physical optional.
        new ProjectTypeDisciplineProfile(
            new ProjectTypeMatch("ניקוז"),
            new[]
            {
                new DisciplineActivation(TaskTypeCodes.DrainagePlanning,            IsRequired: true,  SortOrder: 10),
                new DisciplineActivation(TaskTypeCodes.PhysicalPlanning,            IsRequired: false, SortOrder: 20),
            }),
    };

    /// <summary>One ProjectType → activated disciplines.</summary>
    public sealed record ProjectTypeDisciplineProfile(
        ProjectTypeMatch Match,
        DisciplineActivation[] Disciplines);

    /// <summary>How to find the target ProjectType (matched against <c>JobType.Title</c>).</summary>
    public sealed record ProjectTypeMatch(string TitleContains);

    /// <summary>Per-discipline activation entry for a ProjectType.</summary>
    public sealed record DisciplineActivation(
        string TaskTypeCode,
        bool IsRequired,
        int SortOrder);
}
