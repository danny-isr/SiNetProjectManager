using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class EmailListMigrationBoundaryTests
{
    [Fact]
    public void Email_list_migration_doc_exists()
    {
        var doc = ReadRepoFile("docs/EMAIL_LIST_MIGRATION.md");
        Assert.Contains("EmailListView", doc, StringComparison.Ordinal);
        Assert.Contains("IEmailGateway", doc, StringComparison.Ordinal);
        Assert.Contains("read-only", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Email_list_view_extracted_from_email_window()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("EmailListView", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding EmailList", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Emails}\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("IEmailGateway", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new EmailManagementView", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailListViewModel", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_write_gmail_labels_in_v1()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ShowDeferredWriteActions => false", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmailFilingService", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_filing_port_design_exists_without_sql_implementation()
    {
        var doc = ReadRepoFile("docs/EMAIL_FILING_SERVICE_DESIGN.md");
        var appSource = ReadRepoFile("src/SiNet.Application/Email/IEmailFilingService.cs");
        var composition = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");

        Assert.Contains("IEmailFilingService", doc, StringComparison.Ordinal);
        Assert.Contains("write policy", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interface IEmailFilingService", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmailFilingService", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_apply_context_and_work_surface_launcher_exist()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var launcherSource = ReadRepoFile("src/SiNet.App.Wpf/WorkSurfaces/WorkSurfaceLauncher.cs");

        Assert.Contains("ApplyTaskContextAsync", vmSource, StringComparison.Ordinal);
        Assert.Contains("PrimaryWorkTargetEntityId", vmSource, StringComparison.Ordinal);
        Assert.Contains("IEmailInboxQueryService", vmSource, StringComparison.Ordinal);
        Assert.Contains("WorkSurfaceComponentKeys.IsEmailSurface", launcherSource, StringComparison.Ordinal);
        Assert.Contains("IEmailWindowFactory", launcherSource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNetProjectManager_GitHub.sln"))
                || File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_LIST_MIGRATION.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
