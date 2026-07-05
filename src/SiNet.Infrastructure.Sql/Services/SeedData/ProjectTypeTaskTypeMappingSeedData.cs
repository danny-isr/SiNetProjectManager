namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Seed data definitions for ProjectType ↔ TaskType mappings.
/// Defines which TaskTypes are allowed for each ProjectType (JobType).
/// </summary>
public static class ProjectTypeTaskTypeMappingSeedData
{
    /// <summary>
    /// Baseline ProjectType ↔ TaskType mappings.
    /// Key = ProjectTypeId (JobType.Id), Value = array of allowed TaskTypeIds.
    /// </summary>
    public static readonly Dictionary<int, int[]> Mappings = new()
    {
        // 1: כבישים - General, Office Planning, Plan Review
        [1] = new[] { 1, 2, 3 },
        
        // 2: תנועה - General, Office Planning
        [2] = new[] { 1, 2 },
        
        // 3: קונסטרוקציה - General, Office Planning, Plan Review
        [3] = new[] { 1, 2, 3 },
        
        // 4: תבע - General, Office Planning, Plan Review
        [4] = new[] { 1, 2, 3 },
        
        // 5: רישוי עסקים - General, Office Planning, Plan Review
        [5] = new[] { 1, 2, 3 },
        
        // 6: תיאום מערכות - General, Office Planning, Plan Review
        [6] = new[] { 1, 2, 3 },
        
        // 7: הגשה - General, Office Planning, Plan Review
        [7] = new[] { 1, 2, 3 },
        
        // 8: איחוד וחלוקה - General, Office Planning, Plan Review
        [8] = new[] { 1, 2, 3 },
        
        // 9: חומר כללי - General, Office Planning, Plan Review
        [9] = new[] { 1, 2, 3 },
        
        // 10: פיתוח - General, Office Planning, Plan Review
        [10] = new[] { 1, 2, 3 },
        
        // 11: חומר חיצוני - General, Office Planning, Plan Review
        [11] = new[] { 1, 2, 3 },
        
        // 12: קירות_תומכים - General, Office Planning, Plan Review
        [12] = new[] { 1, 2, 3 },
        
        // 13: בטיחות - General, Office Planning, Plan Review
        [13] = new[] { 1, 2, 3 },
        
        // 14: נגישות - General, Office Planning, Plan Review
        [14] = new[] { 1, 2, 3 },
        
        // 15: חשמל - General, Office Planning, Plan Review
        [15] = new[] { 1, 2, 3 },
        
        // 16: אינסטלציה - General, Office Planning, Plan Review
        [16] = new[] { 1, 2, 3 },
        
        // 17: אדריכלות - General, Office Planning, Plan Review
        [17] = new[] { 1, 2, 3 },
        
        // 18: מדידה - General, Office Planning, Plan Review
        [18] = new[] { 1, 2, 3 },
        
        // 19: לא נבחר פריט - General, Office Planning, Plan Review
        [19] = new[] { 1, 2, 3 },
        
        // 20: בדיקה_חוות_דעת - General, Plan Review only
        [20] = new[] { 1, 3 },
    };
}
