using SiNet.Application.Tasks;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>One allowed task-result row for completion ComboBoxes (Hebrew + color; Code stays English).</summary>
public sealed record TaskResultOption(string Code, string DisplayName, TaskResultColorKind ColorKind)
{
    public static TaskResultOption FromCode(string code)
    {
        var d = TaskResultDisplayCatalog.Resolve(code);
        return new TaskResultOption(d.Code, d.DisplayName, d.ColorKind);
    }
}
