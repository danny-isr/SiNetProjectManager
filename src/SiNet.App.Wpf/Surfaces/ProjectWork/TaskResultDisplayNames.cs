namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>One allowed task-result row for the ProjectWork completion ComboBox.</summary>
public sealed record TaskResultOption(string Code, string DisplayName);

/// <summary>
/// Hebrew labels for task-result codes shown in ProjectWork. Completion still uses the English code.
/// Material check uses the short phrasing requested in QA ("חסר חומר" / "לא חסר חומר").
/// </summary>
internal static class TaskResultDisplayNames
{
    public static string For(string code) => code switch
    {
        "MaterialComplete" => "לא חסר חומר",
        "MaterialMissing" => "חסר חומר",
        "QuoteMaterialComplete" => "חומר להצעה הושלם",
        "QuoteMaterialMissing" => "חסר חומר להצעה",
        _ => code,
    };
}
