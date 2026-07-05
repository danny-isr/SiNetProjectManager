using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Clean baseline seed data for <see cref="Models.ProjectStatus"/>.
/// <para>
/// <c>Closed</c> is the single final state. <c>Completed</c> is intentionally
/// NOT a ProjectStatus.
/// </para>
/// </summary>
public static class ProjectStatusSeedData
{
    public static readonly ProjectStatusDefinition[] Definitions = new[]
    {
        new ProjectStatusDefinition(ProjectStatusCodes.LeadReceived,           "פנייה התקבלה",                              SortOrder: 10),
        new ProjectStatusDefinition(ProjectStatusCodes.QuotePreparation,       "בהכנת הצעת מחיר",                            SortOrder: 20),
        new ProjectStatusDefinition(ProjectStatusCodes.WaitingForQuoteApproval,"ממתין לאישור הצעת מחיר מהלקוח",                SortOrder: 30),
        new ProjectStatusDefinition(ProjectStatusCodes.WaitingForWorkOrder,    "ממתין להזמנת עבודה",                          SortOrder: 40),
        new ProjectStatusDefinition(ProjectStatusCodes.Active,                 "פרויקט פעיל",                                SortOrder: 50),
        new ProjectStatusDefinition(ProjectStatusCodes.WaitingForClient,       "ממתין ללקוח",                                SortOrder: 60),
        new ProjectStatusDefinition(ProjectStatusCodes.WaitingForAuthority,    "ממתין לרשות / גורם מאשר",                     SortOrder: 70),
        new ProjectStatusDefinition(ProjectStatusCodes.WaitingForMaterial,     "ממתין לחומר / השלמות חומר",                   SortOrder: 80),
        new ProjectStatusDefinition(ProjectStatusCodes.BillingPending,         "ממתין לחשבון / חשבון בטיפול",                 SortOrder: 90),
        new ProjectStatusDefinition(ProjectStatusCodes.Closed,                 "סגור",                                       SortOrder: 100),
        new ProjectStatusDefinition(ProjectStatusCodes.ClosedLost,             "נסגר — לא יצא לביצוע / הצעה נדחתה",            SortOrder: 110),
        new ProjectStatusDefinition(ProjectStatusCodes.Cancelled,              "בוטל",                                       SortOrder: 120),
    };

    public record ProjectStatusDefinition(string Code, string Title, int SortOrder);
}
