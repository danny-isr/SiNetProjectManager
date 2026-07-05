using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.DevTools;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.DevTools;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for New System dev-tools migration (reset/seed/demo tasks).
/// </summary>
public sealed class DevToolsBoundaryTests
{
    [Fact]
    public void New_system_registers_dev_tools_services()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/DevToolsServiceCollectionExtensions.cs");
        Assert.Contains("AddSiNetDevTools", source, StringComparison.Ordinal);
        Assert.Contains("IDevDataResetService", source, StringComparison.Ordinal);
        Assert.Contains("IStaticSeedService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dev_reset_service_is_new_system_implementation()
    {
        var resetSource = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlDevDataResetService.cs");
        var shellSource = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        var coordinatorSource = ReadRepoFile("src/SiNet.App.Wpf/DevTools/DevToolsCoordinator.cs");

        Assert.Contains("SqlDevDataResetService", resetSource, StringComparison.Ordinal);
        Assert.Contains("IDevDataResetService", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Services.DevDataResetService", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DevDataResetService.ResetAsync", coordinatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DevResetData_Click", shellSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Dev_reset_options_preserve_system_settings_by_default()
    {
        var options = new DevDataResetOptions();
        Assert.True(options.PreserveSystemSettings);
    }

    [Fact]
    public void Dev_reset_options_preserve_user_settings_by_default()
    {
        var options = new DevDataResetOptions();
        Assert.False(options.ResetUserSettings);
    }

    [Fact]
    public void Dev_reset_skips_missing_tables()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlDevDataResetService.cs");
        Assert.Contains("OBJECT_ID", source, StringComparison.Ordinal);
        Assert.Contains("continue", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dev_reset_reports_deleted_rows()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlDevDataResetService.cs");
        Assert.Contains("DevDataResetTableResult", source, StringComparison.Ordinal);
        Assert.Contains("tableResults.Add", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Static_seed_is_idempotent_by_design()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlTaskManagementSeedService.cs");
        Assert.Contains("Create missing only", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_seedingCompleted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_seed_is_idempotent_by_design()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlWorkflowSeedService.cs");
        Assert.Contains("SeedAllAsync", source, StringComparison.Ordinal);
        Assert.Contains("existing", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Task_demo_seed_creates_three_bucket_queues()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlTaskDemoSeedService.cs");
        Assert.Contains("WorkQueueBucketCodes.Quick", source, StringComparison.Ordinal);
        Assert.Contains("WorkQueueBucketCodes.Medium", source, StringComparison.Ordinal);
        Assert.Contains("WorkQueueBucketCodes.Long", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_demo_seed_is_idempotent_by_source()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlTaskDemoSeedService.cs");
        Assert.Contains("TitlePrefix", source, StringComparison.Ordinal);
        Assert.Contains("existingSet", source, StringComparison.Ordinal);
        Assert.Contains("DemoTaskTypeCodePrefix", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_demo_seed_uses_unique_demo_task_types_by_source()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlTaskDemoSeedService.cs");
        Assert.Contains("DEBUG_TASK_SEED_QUICK_1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("generalType.Id", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Task_demo_seed_non_actionable_task_not_in_open_queue()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/DevTools/SqlTaskDemoSeedService.cs");
        Assert.Contains("Closed (non-open)", source, StringComparison.Ordinal);
        Assert.Contains("closedStatus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dev_tools_menu_is_debug_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        var debugIdx = source.IndexOf("#if DEBUG", StringComparison.Ordinal);
        var devToolsIdx = source.IndexOf("AppendDevToolsMenuItems", StringComparison.Ordinal);
        Assert.True(debugIdx >= 0 && devToolsIdx > debugIdx);
    }

    [Fact]
    public void NewShell_dev_tools_menu_does_not_call_legacy_MainWindow()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.DoesNotContain("DevResetData_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetProjectManagerV2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_LegacyBridge_dependency_for_dev_tools()
    {
        foreach (var path in new[]
        {
            "src/SiNet.Infrastructure.Sql/Services/DevTools/SqlDevDataResetService.cs",
            "src/SiNet.App.Wpf/DevTools/DevToolsCoordinator.cs",
        })
        {
            var content = ReadRepoFile(path);
            Assert.DoesNotContain("LegacyBridge", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_new_migration_or_schema_change_for_dev_tools()
    {
        var migrationsDir = Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Migrations");
        var recent = Directory.Exists(migrationsDir)
            ? Directory.GetFiles(migrationsDir, "*DevTools*", SearchOption.AllDirectories)
            : [];
        Assert.Empty(recent);
    }

    [Fact]
    public void App_wpf_dev_tools_has_no_direct_SiNetSQL_services_dependency()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("SiNetSQL", csproj, StringComparison.OrdinalIgnoreCase);

        var coordinator = ReadRepoFile("src/SiNet.App.Wpf/DevTools/DevToolsCoordinator.cs");
        Assert.DoesNotContain("SiNetSQL.Services", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void Dev_tools_feature_codes_registered()
    {
        Assert.Equal(AppRole.Management, AppFeatureAuthorization.GetRequiredRole(AppFeatureCodes.DevToolsReset));
        Assert.Equal(AppRole.Management, AppFeatureAuthorization.GetRequiredRole(AppFeatureCodes.DevToolsSeed));
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
