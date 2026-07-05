namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Durable seed data for the <see cref="Models.InspectionNoteStatus"/> lookup table.
/// <para>
/// This is the runtime source of truth for the inspection-note status ComboBox
/// in the floating inspection window. The legacy migration-era seed
/// (<see cref="Data.Configurations.InspectionNoteStatusConfiguration"/> <c>HasData</c>)
/// is not sufficient when the database is deleted and recreated outside of
/// migrations, so this seed is wired into <see cref="TaskManagementSeedService"/>
/// and runs idempotently on app startup.
/// </para>
/// <para>
/// Upsert rules:
/// <list type="bullet">
///   <item><c>StatusKey</c> is the business key.</item>
///   <item>If the key exists: <c>HebrewLabel</c>, <c>SortOrder</c>, <c>IsActive</c> are updated.</item>
///   <item>If the key does not exist: a new row is inserted.</item>
///   <item>Existing rows whose key is not in this list are left untouched (never deleted).</item>
/// </list>
/// </para>
/// </summary>
public static class InspectionNoteStatusSeedData
{
    // NOTE: StatusKey values intentionally match the keys already used throughout the
    // existing code (InspectionStatusKeys, GoogleReportExportService, NoteTreeItem,
    // ManagementSettingsWindow). Do NOT introduce parallel keys like "Comment" or
    // "RecurringComment" here without a coordinated refactor of those call sites —
    // duplicate keys would produce duplicate ComboBox entries with the same meaning.
    public static readonly InspectionNoteStatusDefinition[] Definitions = new[]
    {
        new InspectionNoteStatusDefinition("Passed",          "מקובל",              SortOrder: 1, IsActive: true),
        new InspectionNoteStatusDefinition("Failed",          "הערה",               SortOrder: 2, IsActive: true),
        new InspectionNoteStatusDefinition("RecurringFailed", "הערה חוזרת",         SortOrder: 3, IsActive: true),
        new InspectionNoteStatusDefinition("NotApplicable",   "לא רלוונטי",         SortOrder: 4, IsActive: true),
        new InspectionNoteStatusDefinition("ManagerReview",   "הערה לבדיקת המנהל",  SortOrder: 5, IsActive: true),
    };

    public sealed record InspectionNoteStatusDefinition(
        string StatusKey,
        string HebrewLabel,
        int SortOrder,
        bool IsActive);
}
