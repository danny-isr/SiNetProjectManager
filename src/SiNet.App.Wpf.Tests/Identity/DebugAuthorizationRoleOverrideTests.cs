using System.IO;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class DebugAuthorizationRoleOverrideTests
{
    [Fact]
    public void Identity_sql_registration_includes_debug_role_override_service()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.Infrastructure.Sql",
            "IdentitySqlServiceCollectionExtensions.cs"));

        Assert.Contains(
            "IDebugAuthorizationRoleOverrideService",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SqlDebugAuthorizationRoleOverrideService),
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Standalone_host_wires_debug_role_selector_only_under_debug_compile()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "App.xaml.cs"));

        Assert.Contains("RunDebugAuthorizationRoleSelectorAsync", source, StringComparison.Ordinal);
        Assert.Contains("SINET_SKIP_DEBUG_ROLE_SELECTOR", source, StringComparison.Ordinal);
        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Debug_role_selector_window_exists_in_app_wpf()
    {
        var xaml = Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "DevTools",
            "DebugAuthorizationRoleSelectorWindow.xaml");
        var code = Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "DevTools",
            "DebugAuthorizationRoleSelectorWindow.xaml.cs");

        Assert.True(File.Exists(xaml));
        Assert.True(File.Exists(code));
        Assert.Contains(
            "IDebugAuthorizationRoleOverrideService",
            File.ReadAllText(code),
            StringComparison.Ordinal);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                    || File.Exists(Path.Combine(dir.FullName, "docs", "MIGRATION_MAP.md")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }
    }
}
