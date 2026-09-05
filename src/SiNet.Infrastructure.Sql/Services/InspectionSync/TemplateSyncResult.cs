namespace SiNetSQL.Services.InspectionSync;

/// <summary>
/// Result of a template sync operation against the Sections/Chapters tables.
/// </summary>
public sealed class TemplateSyncResult
{
    public int TotalRows { get; set; }

    /// <summary>Sections that did not exist before and were created.</summary>
    public int CreatedCount { get; set; }

    /// <summary>Sections that already existed with identical content and were re-activated.</summary>
    public int ReactivatedCount { get; set; }

    /// <summary>Sections whose content changed — old version deactivated, new version created.</summary>
    public int VersionedCount { get; set; }

    /// <summary>Sections that were already active with identical content — no changes made.</summary>
    public int UnchangedCount { get; set; }

    /// <summary>Sections in DB that are no longer present in the sheet and were deactivated.</summary>
    public int DeactivatedCount { get; set; }

    /// <summary>Chapters created during this sync.</summary>
    public int ChaptersCreatedCount { get; set; }

    /// <summary>
    /// Actual number of state entries written by SaveChangesAsync.
    /// -1 means SaveChanges was never called or rolled back.
    /// </summary>
    public int DbSavedCount { get; set; } = -1;

    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    public bool IsSuccess => !HasErrors && DbSavedCount >= 0;
}
