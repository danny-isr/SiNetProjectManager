using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class EmailDetailBoundaryTests
{
    [Fact]
    public void Email_detail_component_doc_exists()
    {
        var doc = ReadRepoFile("docs/EMAIL_DETAIL_COMPONENT.md");
        Assert.Contains("EmailDetailView", doc, StringComparison.Ordinal);
        Assert.Contains("IEmailBodyRenderer", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_detail_extracted_from_email_window_without_legacy_bridge()
    {
        var windowXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var detailVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        var detailFolder = Path.Combine(
            ResolveRepoRoot(),
            "src/SiNet.App.Wpf/Surfaces/Email/Detail");

        Assert.Contains("EmailDetailView", windowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding EmailDetail", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Calendar", windowXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LegacyBridge", detailVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.MVVM", detailVmSource, StringComparison.Ordinal);

        foreach (var file in Directory.EnumerateFiles(detailFolder, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var content = File.ReadAllText(file);
                Assert.DoesNotContain("LegacyBridge", content, StringComparison.Ordinal);
                Assert.DoesNotContain("SiNetSQL.MVVM", content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Email_detail_ports_registered_in_composition()
    {
        var composition = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");
        var v2Graph = ReadRepoFile("SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs");
        var extensions = ReadRepoFile("src/SiNet.Infrastructure.Sql/EmailDetailServiceCollectionExtensions.cs");

        Assert.Contains("AddSiNetEmailDetailSql", composition, StringComparison.Ordinal);
        Assert.Contains("AddSiNetEmailDetailSql", v2Graph, StringComparison.Ordinal);
        Assert.Contains("IEmailMoveToProjectService", extensions, StringComparison.Ordinal);
        Assert.Contains("IEmailAccIngestionService", extensions, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_window_shell_delegates_selection_to_email_detail()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.Contains("EmailDetailViewModel", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailDetail.ApplySelectionAsync", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailDetailSelectionCoordinator", ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailSelectionCoordinator.cs"), StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var candidate = Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(candidate);
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_DETAIL_COMPONENT.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
