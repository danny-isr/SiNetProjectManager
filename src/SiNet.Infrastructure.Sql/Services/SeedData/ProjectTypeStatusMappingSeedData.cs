namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Seed data definitions for ProjectType ↔ Status mappings.
/// Defines which Statuses are allowed for each ProjectType (JobType).
/// </summary>
public static class ProjectTypeStatusMappingSeedData
{
    /// <summary>
    /// Baseline ProjectType ↔ Status mappings.
    /// Key = ProjectTypeId (JobType.Id), Value = array of allowed StatusIds.
    /// </summary>
    public static readonly Dictionary<int, int[]> Mappings = new()
    {
        // 1: כבישים - Limited statuses (Completed, Waiting External, In Planning)
        [1] = new[] { 2, 3, 14 },
        
        // 2: תנועה - Core + traffic approval statuses
        [2] = new[] { 1, 2, 3, 6, 9, 14, 15 },
        
        // 3: קונסטרוקציה - Core statuses only
        [3] = new[] { 1, 2, 3 },
        
        // 4: תבע - Core statuses only
        [4] = new[] { 1, 2, 3 },
        
        // 5: רישוי עסקים - Core statuses only
        [5] = new[] { 1, 2, 3 },
        
        // 6: תיאום מערכות - Core statuses only
        [6] = new[] { 1, 2, 3 },
        
        // 7: הגשה - Core statuses only
        [7] = new[] { 1, 2, 3 },
        
        // 8: איחוד וחלוקה - Core statuses only
        [8] = new[] { 1, 2, 3 },
        
        // 9: חומר כללי - Cancelled only
        [9] = new[] { 15 },
        
        // 10: פיתוח - Core statuses only
        [10] = new[] { 1, 2, 3 },
        
        // 11: חומר חיצוני - Core statuses only
        [11] = new[] { 1, 2, 3 },
        
        // 12: קירות_תומכים - Core statuses only
        [12] = new[] { 1, 2, 3 },
        
        // 13: בטיחות - Core statuses only
        [13] = new[] { 1, 2, 3 },
        
        // 14: נגישות - Core statuses only
        [14] = new[] { 1, 2, 3 },
        
        // 15: חשמל - Core statuses only
        [15] = new[] { 1, 2, 3 },
        
        // 16: אינסטלציה - Core statuses only
        [16] = new[] { 1, 2, 3 },
        
        // 17: אדריכלות - Core statuses only
        [17] = new[] { 1, 2, 3 },
        
        // 19: לא נבחר פריט - Core statuses only
        [19] = new[] { 1, 2, 3 },
        
        // 20: בדיקה_חוות_דעת - Full expert review flow
        [20] = new[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15 },
    };
}
