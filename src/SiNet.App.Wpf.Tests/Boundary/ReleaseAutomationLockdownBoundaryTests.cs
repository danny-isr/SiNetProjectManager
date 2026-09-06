using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf;
using SiNet.App.Wpf.Inspection;
using SiNet.Infrastructure.Sql.Services.DevTools;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards <c>docs/RELEASE_AUTOMATION_LOCKDOWN.md</c> — Release must not register or ship DEV/test automation.
/// </summary>
public sealed class ReleaseAutomationLockdownBoundaryTests
{
    [Fact]
    public void Standalone_host_wraps_inspection_harness_registrations_in_debug_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/StandaloneHostServiceCollectionExtensions.cs");

        Assert.Contains("InspectionShellViewModel", source, StringComparison.Ordinal);
        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseDevAutomationGuard.AssertStandaloneHostDescriptorsClean", source, StringComparison.Ordinal);

        var harnessIdx = source.IndexOf("TryAddSingleton<InspectionShellViewModel>", StringComparison.Ordinal);
        Assert.True(harnessIdx > 0, "expected InspectionShellViewModel registration");

        var debugBefore = source.LastIndexOf("#if DEBUG", harnessIdx, StringComparison.Ordinal);
        var endifAfter = source.IndexOf("#endif", harnessIdx, StringComparison.Ordinal);
        Assert.True(debugBefore >= 0 && endifAfter > harnessIdx,
            "InspectionShellViewModel registration must sit inside #if DEBUG … #endif");
    }

    [Fact]
    public void DevTools_mutating_seed_services_are_debug_only()
    {
        var source = ReadRepoFile(
            "src/SiNet.Infrastructure.Sql/Services/DevTools/DevToolsServiceCollectionExtensions.cs");

        Assert.Contains("AddTransient<SqlWorkflowSeedService>", source, StringComparison.Ordinal);
        Assert.Contains("AddTransient<SqlTaskDemoSeedService>", source, StringComparison.Ordinal);
        Assert.Contains("SqlStaticSeedServiceReleaseStub", source, StringComparison.Ordinal);
        Assert.Contains("SqlDevDataResetServiceReleaseStub", source, StringComparison.Ordinal);

        var workflowIdx = source.IndexOf("AddTransient<SqlWorkflowSeedService>", StringComparison.Ordinal);
        var debugBefore = source.LastIndexOf("#if DEBUG", workflowIdx, StringComparison.Ordinal);
        var endifAfter = source.IndexOf("#endif", workflowIdx, StringComparison.Ordinal);
        Assert.True(debugBefore >= 0 && endifAfter > workflowIdx);
    }

    [Fact]
    public void New_shell_dev_tools_menu_is_debug_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        var callIdx = source.IndexOf(
            "var devTools = await BuildDevToolsMenuItemsAsync",
            StringComparison.Ordinal);
        Assert.True(callIdx > 0);
        var debugBefore = source.LastIndexOf("#if DEBUG", callIdx, StringComparison.Ordinal);
        var endifAfter = source.IndexOf("#endif", callIdx, StringComparison.Ordinal);
        Assert.True(debugBefore >= 0 && endifAfter > callIdx);
        Assert.Contains("כלי פיתוח", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_wpf_csproj_does_not_reference_test_project()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.False(
            Regex.IsMatch(csproj, @"ProjectReference[^>\n]*SiNet\.App\.Wpf\.Tests", RegexOptions.IgnoreCase),
            "SiNet.App.Wpf must not ProjectReference the test project");
        Assert.DoesNotContain("<PackageReference Include=\"xunit", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.NET.Test.Sdk", csproj, StringComparison.OrdinalIgnoreCase);
        // InternalsVisibleTo for the test assembly is allowed (compile metadata only).
        Assert.Contains("InternalsVisibleTo Include=\"SiNet.App.Wpf.Tests\"", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_guard_lists_prohibited_harness_type_names()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/ReleaseDevAutomationGuard.cs");
        Assert.Contains("InspectionShellViewModel", source, StringComparison.Ordinal);
        Assert.Contains("SqlWorkflowSeedService", source, StringComparison.Ordinal);
        Assert.Contains("SqlDevDataResetService", source, StringComparison.Ordinal);
        Assert.Contains("AssertStandaloneHostDescriptorsClean", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_automation_lockdown_doc_exists_and_states_verdict_rule()
    {
        var doc = ReadRepoFile("docs/RELEASE_AUTOMATION_LOCKDOWN.md");
        Assert.Contains("RELEASE TEST/DEV AUTOMATION = DISABLED", doc, StringComparison.Ordinal);
        Assert.Contains("PilotSmoke", doc, StringComparison.Ordinal);
        Assert.Contains("InspectionShellViewModel", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Prohibited_env_vars_are_not_read_by_app_wpf_product_code()
    {
        var appRoot = Path.Combine(RepoPaths.RepoRoot, "src", "SiNet.App.Wpf");
        var hits = new List<string>();
        foreach (var path in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            if (Regex.IsMatch(text, @"SINET_(PILOT_SMOKE|SYSTEM_CERT|LIVE_SMOKE)\b"))
                hits.Add(Path.GetRelativePath(RepoPaths.RepoRoot, path));
        }

        Assert.True(
            hits.Count == 0,
            "App.Wpf must not read PilotSmoke/SystemCert/LiveSmoke env vars: " + string.Join(", ", hits));
    }

    [Fact]
    public void Release_guard_throws_when_inspection_harness_is_registered()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(InspectionShellViewModel), typeof(InspectionShellViewModel));

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReleaseDevAutomationGuard.AssertStandaloneHostDescriptorsClean(services));
        Assert.Contains("InspectionShellViewModel", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_guard_allows_clean_collection()
    {
        var services = new ServiceCollection();
        ReleaseDevAutomationGuard.AssertStandaloneHostDescriptorsClean(services);
    }

    [Fact]
    public void AddSiNetDevTools_in_this_configuration_matches_lockdown()
    {
        var services = new ServiceCollection();
        services.AddSiNetDevTools();

        var implNames = services
            .Where(d => d.ImplementationType is not null)
            .Select(d => d.ImplementationType!.Name)
            .ToHashSet(StringComparer.Ordinal);

#if DEBUG
        Assert.Contains("SqlWorkflowSeedService", implNames);
        Assert.Contains("SqlStaticSeedService", implNames);
#else
        Assert.DoesNotContain("SqlWorkflowSeedService", implNames);
        Assert.DoesNotContain("SqlTaskDemoSeedService", implNames);
        Assert.DoesNotContain("SqlStaticSeedService", implNames);
        Assert.DoesNotContain("SqlDevDataResetService", implNames);
        Assert.Contains("SqlStaticSeedServiceReleaseStub", implNames);
        Assert.Contains("SqlDevDataResetServiceReleaseStub", implNames);
#endif
        Assert.Contains("SqlSeedBaselineVerifyService", implNames);
    }

#if !DEBUG
    [Fact]
    public void Release_binary_excludes_devtools_and_inspection_harness_entry_points()
    {
        var shell = typeof(SiNet.App.Wpf.Shell.NewShellFactory);
        Assert.Null(shell.GetMethod(
            "OpenInspectionShell",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.Null(shell.GetMethod(
            "BuildDevToolsMenuItemsAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.Null(shell.GetMethod(
            "RunWatchdogNowAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));

        Assert.Null(Type.GetType("SiNet.App.Wpf.DevTools.DevToolsCoordinator, SiNet.App.Wpf"));
    }

    [Fact]
    public void Release_menu_source_has_no_devtools_titles_outside_debug()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        // Strip DEBUG regions then assert forbidden menu titles are gone from Release compile surface.
        var releaseSurface = System.Text.RegularExpressions.Regex.Replace(
            source,
            @"#if\s+DEBUG[\s\S]*?#endif",
            string.Empty,
            RegexOptions.Multiline);

        Assert.DoesNotContain("כלי פיתוח", releaseSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("ביקורת (מעטפת — DEBUG)", releaseSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("איפוס נתוני פיתוח", releaseSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("טעינת Seed בסיסי", releaseSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("טעינת משימות דמו", releaseSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("הרץ Watchdog עכשיו", releaseSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenInspectionShell", releaseSurface, StringComparison.Ordinal);
    }
#endif

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
