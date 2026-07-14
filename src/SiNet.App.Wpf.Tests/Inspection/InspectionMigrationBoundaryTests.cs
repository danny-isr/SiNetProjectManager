using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class InspectionMigrationBoundaryTests
{
    [Fact]
    public void Composition_registers_inspection_sql()
    {
        var source = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");
        Assert.Contains("AddSiNetInspectionSql()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void New_system_graph_registers_inspection_and_ai()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs");
        Assert.Contains("AddSiNetInspectionSql", source, StringComparison.Ordinal);
        Assert.Contains("AddSiNetAi", source, StringComparison.Ordinal);
        Assert.Contains("V2InspectionFileTreePickerHost", source, StringComparison.Ordinal);
        Assert.Contains("V2InspectionNoteLinkedFileHost", source, StringComparison.Ordinal);
    }

    [Fact]
    public void New_shell_exposes_inspection_window_entry()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("IInspectionWindowFactory", source, StringComparison.Ordinal);
        Assert.Contains("דוחות ביקורת", source, StringComparison.Ordinal);
    }

    [Fact]
    public void V2_menu_prefers_inspection_window_factory()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/MainWindow.xaml.cs");
        Assert.Contains("IInspectionWindowFactory", source, StringComparison.Ordinal);
        Assert.Contains("OpenFloatingInspection_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspection_area_view_models_exist()
    {
        Assert.NotNull(typeof(SiNet.App.Wpf.Surfaces.Inspection.InspectionQuestionnaireViewModel));
        Assert.NotNull(typeof(SiNet.App.Wpf.Surfaces.Inspection.InspectionNoteEditorViewModel));
        Assert.NotNull(typeof(SiNet.App.Wpf.Surfaces.Inspection.InspectionNoteRichEditor));
        Assert.NotNull(typeof(SiNet.Application.Abstractions.Inspection.IInspectionNoteAiReviewer));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !File.Exists(Path.Combine(dir.FullName, "SiNetProjectManager_GitHub.sln"))
               && !File.Exists(Path.Combine(dir.FullName, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var root = dir!.FullName.EndsWith("SiNetProjectManager_GitHub", StringComparison.OrdinalIgnoreCase)
            ? dir.FullName
            : Path.Combine(dir.FullName, "SiNetProjectManager_GitHub");
        if (!Directory.Exists(root))
        {
            root = dir.FullName;
        }

        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
