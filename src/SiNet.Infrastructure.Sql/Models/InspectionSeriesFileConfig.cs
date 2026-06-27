namespace SiNetSQL.Models;

/// <summary>Role of a <see cref="ProjectFile"/> within an inspection series.</summary>
public enum InspectionFileRole
{
    /// <summary>File that can be selected for inspection (source drawing).</summary>
    Inspectable = 0,

    /// <summary>File slot where the stamped/approved plan is saved.</summary>
    ApprovedPlan = 1
}

/// <summary>
/// Junction entity linking an <see cref="InspectionSeries"/> to the <see cref="ProjectFile"/>
/// types that participate in the inspection workflow.
/// <para>
/// Each row means: "For this inspection template, this file type can serve as
/// an inspectable drawing (or as the approved-plan target)."
/// </para>
/// </summary>
public class InspectionSeriesFileConfig
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public int ProjectFileId { get; set; }

    public InspectionFileRole Role { get; set; }

    // Navigation
    public virtual InspectionSeries Series { get; set; } = null!;
    public virtual ProjectFile ProjectFile { get; set; } = null!;
}
