using System.Text.Json;

namespace SiNetSQL.Models;

/// <summary>
/// טריגר שמפעיל יצירת Workflow Instance חדש.
/// כל <see cref="WorkflowDefinition"/> יכול להכיל מספר טריגרים —
/// כל אחד מהם מגדיר תנאי שבו ייווצר Instance חדש אוטומטית.
/// 
/// דוגמאות:
///   - פרויקט חדש עם סוג "תכנון" → יוצר Instance של Workflow תכנון
///   - מייל תויק כ"הזמנת עבודה" → יוצר Instance של Workflow קליטת הזמנה
///   - SubWorkflow מופעל ע"י Workflow אב → מקבל פרמטרים ממנו
/// </summary>
public class WorkflowStartTrigger
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public int Id { get; set; }

    /// <summary>FK ל-Workflow Definition שאליו שייך הטריגר.</summary>
    public int WorkflowDefinitionId { get; set; }

    /// <summary>סוג האירוע שמפעיל את ה-Workflow.</summary>
    public WorkflowStartTriggerSource TriggerSource { get; set; }

    /// <summary>
    /// JSON שמכיל את ה-Properties המוקלדים של הטריגר.
    /// Deserialized ל-<see cref="Properties"/> (polymorphic).
    /// </summary>
    public string? PropertiesJson { get; set; }

    /// <summary>
    /// מיפוי פרמטרים מהמקור ל-Workflow Instance.
    /// JSON object שהמפתח הוא שם הפרמטר ב-Workflow והערך הוא path במקור.
    /// דוגמה: {"ProjectId":"$.ProjectId","EmailId":"$.SourceId","EmailType":"$.FileType"}
    /// </summary>
    public string? ParameterMappingJson { get; set; }

    /// <summary>תיאור חופשי למנהל התהליך.</summary>
    public string? Description { get; set; }

    /// <summary>האם הטריגר פעיל.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>סדר הערכה (אם כמה טריגרים מאותו סוג).</summary>
    public int SortOrder { get; set; }

    // ═══ Typed Properties (not mapped — backed by PropertiesJson) ═══

    private WorkflowTriggerProperties? _properties;

    /// <summary>
    /// Strongly-typed properties object, deserialized from <see cref="PropertiesJson"/>.
    /// Auto-creates a default instance matching <see cref="TriggerSource"/> if null.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public WorkflowTriggerProperties Properties
    {
        get => _properties ??= DeserializeProperties();
        set
        {
            _properties = value;
            SyncPropertiesJson();
        }
    }

    /// <summary>Serialize Properties back to PropertiesJson. Call before saving.</summary>
    public void SyncPropertiesJson()
    {
        PropertiesJson = _properties is not null
            ? JsonSerializer.Serialize(_properties, _jsonOptions)
            : null;
    }

    private WorkflowTriggerProperties DeserializeProperties()
    {
        if (!string.IsNullOrWhiteSpace(PropertiesJson))
        {
            try
            {
                var result = JsonSerializer.Deserialize<WorkflowTriggerProperties>(PropertiesJson, _jsonOptions);
                if (result is not null) return result;
            }
            catch { /* fallback to default */ }
        }

        return WorkflowTriggerProperties.CreateDefault(TriggerSource);
    }

    // ═══ Navigation ═══

    public virtual WorkflowDefinition WorkflowDefinition { get; set; } = null!;
}
