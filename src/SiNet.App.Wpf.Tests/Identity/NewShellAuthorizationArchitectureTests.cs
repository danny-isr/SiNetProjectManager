using System.IO;
using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

/// <summary>
/// Ensures New System shell code uses Application authorization ports, not legacy singletons.
/// </summary>
public sealed class NewShellAuthorizationArchitectureTests
{
    [Fact]
    public void NewShell_types_do_not_reference_SiNetSQL_assembly()
    {
        var wpfAssembly = typeof(NewShellFactory).Assembly;
        Assert.DoesNotContain(
            wpfAssembly.GetReferencedAssemblies(),
            a => a.Name == "SiNetSQL");
    }

    [Fact]
    public void NewShellViewModel_does_not_reference_authorization_singletons()
    {
        var source = ReadSourceRelativeToRepo("src/SiNet.App.Wpf/Shell/NewShellViewModel.cs");
        Assert.DoesNotContain("CurrentUserContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAdmin", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellFactory_uses_authorization_query_service_not_legacy_context()
    {
        var source = ReadSourceRelativeToRepo("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("IAuthorizationQueryService", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentUserContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAdmin", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellFactory_wires_action_permission_admin_via_factory_and_feature_code()
    {
        var source = ReadSourceRelativeToRepo("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("IActionPermissionAdminWindowFactory", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.ActionPermissionsManage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionPermissionWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_types_do_not_reference_action_permission_legacy_service()
    {
        var wpfAssembly = typeof(NewShellFactory).Assembly;
        Assert.DoesNotContain(
            wpfAssembly.GetReferencedAssemblies(),
            a => a.Name == "SiNetSQL");

        var factorySource = ReadSourceRelativeToRepo("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.DoesNotContain("IActionPermissionService", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionPermissionService", factorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellViewModel_does_not_reference_action_permission_context()
    {
        var source = ReadSourceRelativeToRepo("src/SiNet.App.Wpf/Shell/NewShellViewModel.cs");
        Assert.DoesNotContain("IActionPermissionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionPermissionService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_types_do_not_reference_user_management_legacy_service()
    {
        var wpfAssembly = typeof(NewShellFactory).Assembly;
        Assert.DoesNotContain(
            wpfAssembly.GetReferencedAssemblies(),
            a => a.Name == "SiNetSQL");

        var factorySource = ReadSourceRelativeToRepo("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.DoesNotContain("IUserService", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserService", factorySource, StringComparison.Ordinal);

        var vmSource = ReadSourceRelativeToRepo("src/SiNet.App.Wpf/Shell/NewShellViewModel.cs");
        Assert.DoesNotContain("IUserService", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserService", vmSource, StringComparison.Ordinal);
    }

    private static string ReadSourceRelativeToRepo(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from test output directory.");
    }
}
