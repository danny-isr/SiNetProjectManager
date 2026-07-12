using System.IO;
using System.Xml.Linq;
using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Architecture guards for <see cref="docs/NEW_SYSTEM_BOUNDARY.md"/> — prevents New System from
/// referencing legacy projects or opening legacy admin windows.
/// </summary>
public sealed class NewSystemBoundaryTests
{
    private static readonly string[] ForbiddenLegacyIdentifiersInAppWpf =
    [
        "SiNetSQL.MVVM",
        "SiNetProjectManagerV2.Dialogs",
        "Dialogs.UserManagementWindow",
        "Dialogs.AddUserWindow",
        "Dialogs.ActionPermissionWindow",
        "EmailManagementViewModel",
        "FloatingInspectionViewModel",
        "ProjectWorkViewModel",
        "ProjectFolderTreeViewModel",
        "WorkflowTaskOrchestrator",
        "WorkflowEngine",
        "WorkflowManagementWindow",
        "MyOffice.AutodeskConnector",
        "Bim360Service",
    ];

    private static readonly string[] ForbiddenLegacyAdminInNewShellFactory =
    [
        "IActionPermissionAdminWindowFactory",
        "IUserManagementWindowFactory",
        "IAddUserWindowFactory",
        "SiNetProjectManagerV2.Dialogs",
        "new UserManagementWindow",
        "new AddUserWindow",
        "new ActionPermissionWindow",
        "Dialogs.ActionPermissionWindow",
    ];

    private static readonly string[] ForbiddenLegacyAdminFactoryFilesInShell =
    [
        "IActionPermissionAdminWindowFactory.cs",
        "IUserManagementWindowFactory.cs",
        "IAddUserWindowFactory.cs",
    ];

    [Fact]
    public void App_Wpf_csproj_does_not_reference_SiNetSQL()
    {
        var references = ReadProjectReferences(AppWpfCsprojPath);
        Assert.DoesNotContain(
            references,
            r => r.Contains("SiNetSQL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void App_Wpf_csproj_does_not_reference_SiNetProjectManagerV2()
    {
        var references = ReadProjectReferences(AppWpfCsprojPath);
        Assert.DoesNotContain(
            references,
            r => r.Contains("SiNetProjectManagerV2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void App_Wpf_assembly_does_not_reference_SiNetSQL_or_V2()
    {
        var wpfAssembly = typeof(NewShellFactory).Assembly;
        var names = wpfAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(names, n => n == "SiNetSQL");
        Assert.DoesNotContain(names, n => n == "SiNetProjectManagerV2");
    }

    public static IEnumerable<object[]> AppWpfSourceFiles()
    {
        foreach (var file in EnumerateAppWpfSourceFiles())
        {
            yield return [Path.GetRelativePath(AppWpfRoot, file)];
        }
    }

    [Theory]
    [MemberData(nameof(AppWpfSourceFiles))]
    public void App_Wpf_source_does_not_contain_forbidden_legacy_identifiers(string relativePath)
    {
        var fullPath = Path.Combine(AppWpfRoot, relativePath);
        var content = File.ReadAllText(fullPath);

        foreach (var forbidden in ForbiddenLegacyIdentifiersInAppWpf)
        {
            Assert.False(
                content.Contains(forbidden, StringComparison.Ordinal),
                $"Forbidden legacy identifier '{forbidden}' found in src/SiNet.App.Wpf/{relativePath}");
        }
    }

    [Fact]
    public void New_system_boundary_doc_records_gmail_foundation_rules()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "NEW_SYSTEM_BOUNDARY.md"));

        Assert.Contains("Native Gmail foundation", doc, StringComparison.Ordinal);
        Assert.Contains("IConnectorAuthService", doc, StringComparison.Ordinal);
        Assert.Contains("Drive / Sheets / report/export work is **not** part of Gmail window migration", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void New_system_boundary_doc_records_acc_read_first_and_write_defer_rules()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "NEW_SYSTEM_BOUNDARY.md"));

        Assert.Contains("ACC write-side rule", doc, StringComparison.Ordinal);
        Assert.Contains("server-only or deferred", doc, StringComparison.Ordinal);
        Assert.Contains("provisioning / upload / move / metadata-write behavior", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellFactory_native_user_admin_uses_App_Wpf_host_windows()
    {
        var source = File.ReadAllText(NewShellFactoryPath);
        Assert.Contains("UserListWindow", source, StringComparison.Ordinal);
        Assert.Contains("AddUserDialogWindow", source, StringComparison.Ordinal);
        Assert.Contains("ActionPermissionsWindow", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.UsersManage", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.ActionPermissionsManage", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellFactory_does_not_wire_legacy_admin_menu_or_factories()
    {
        var source = File.ReadAllText(NewShellFactoryPath);

        foreach (var forbidden in ForbiddenLegacyAdminInNewShellFactory)
        {
            Assert.False(
                source.Contains(forbidden, StringComparison.Ordinal),
                $"NewShellFactory must not contain legacy admin wiring: '{forbidden}'");
        }
    }

    [Fact]
    public void App_Wpf_Shell_has_no_legacy_admin_window_factory_interfaces()
    {
        var shellDir = Path.Combine(AppWpfRoot, "Shell");
        var shellFiles = Directory.Exists(shellDir)
            ? Directory.GetFiles(shellDir, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToList()
            : [];

        foreach (var forbiddenFile in ForbiddenLegacyAdminFactoryFilesInShell)
        {
            Assert.DoesNotContain(forbiddenFile, shellFiles);
        }
    }

    [Fact]
    public void Admin_capabilities_policy_only_native_window_factories_allowed_in_App_Wpf()
    {
        // Legacy admin used host *WindowFactory ports. Rebuild admin UI as App.Wpf views — not wrappers.
        var allSource = string.Concat(EnumerateAppWpfSourceFiles()
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("IActionPermissionAdminWindowFactory", allSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IUserManagementWindowFactory", allSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IAddUserWindowFactory", allSource, StringComparison.Ordinal);
        Assert.Contains("IEmailWindowFactory", allSource, StringComparison.Ordinal);
    }

    private static string RepoRoot => RepoPaths.RepoRoot;

    private static string AppWpfRoot => Path.Combine(RepoRoot, "src", "SiNet.App.Wpf");

    private static string AppWpfCsprojPath => Path.Combine(AppWpfRoot, "SiNet.App.Wpf.csproj");

    private static string NewShellFactoryPath => Path.Combine(AppWpfRoot, "Shell", "NewShellFactory.cs");

    private static IEnumerable<string> EnumerateAppWpfSourceFiles()
    {
        if (!Directory.Exists(AppWpfRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(AppWpfRoot, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            yield return file;
        }
    }

    private static IReadOnlyList<string> ReadProjectReferences(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        return doc
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();
    }
}

/// <summary>Locates the GitHub repo root from test output directories.</summary>
internal static class RepoPaths
{
    internal static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not locate SiNet.App.Wpf.csproj from test output directory.");
        }
    }
}
