using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.MasterPlan;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Tests.Shell;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.MasterPlan;

public sealed class MasterPlanMonthlyRestoreMenuTests
{
    [Fact]
    public void When_management_authorized_then_monthly_restore_appears_under_admin()
    {
        var top = BuildMenu(allowMonthly: true);
        var admin = top.Single(g => g.Title == "מנהלה");
        Assert.Contains(admin.Children, i => i.Title == "שחזור חודשי MasterPlan" && i.IsAvailable);
    }

    [Fact]
    public void When_employee_denied_then_monthly_restore_is_hidden()
    {
        var items = NewShellMenuReflection.Flatten(BuildMenu(allowMonthly: false));
        Assert.DoesNotContain(items, i => i.Title == "שחזור חודשי MasterPlan");
    }

    [Fact]
    public void NewShellFactory_opens_monthly_restore_window_via_feature_code()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("AppFeatureCodes.ShellOpenMasterPlanMonthlyRestore", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeMasterPlanMonthlyRestore", source, StringComparison.Ordinal);
        Assert.Contains("MasterPlanMonthlyRestoreWindow", source, StringComparison.Ordinal);
        Assert.Contains("שחזור חודשי MasterPlan", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Di_registers_monthly_restore_window()
    {
        var source = ReadRepoFile(
            "src/SiNet.App.Wpf/Admin/MasterPlan/MasterPlanAdminServiceCollectionExtensions.cs");
        Assert.Contains("MasterPlanMonthlyRestoreViewModel", source, StringComparison.Ordinal);
        Assert.Contains("MasterPlanMonthlyRestoreWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Launcher_targets_published_path_and_monthly_flag()
    {
        var source = ReadRepoFile(
            "src/SiNet.App.Wpf/Admin/MasterPlan/MasterPlanSyncEngineLauncher.cs");
        Assert.Contains(MasterPlanSyncEngineLauncher.PublishedExePath, source, StringComparison.Ordinal);
        Assert.Contains("--monthly --backup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.SqlServer.Management.Smo", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncEngine_monthly_pipeline_orders_gate_compare_drop_etl()
    {
        var monthly = ReadRepoFile("MasterPlan.SyncEngine/MonthlyBackupRestoreService.cs");
        Assert.Contains("STEP 0 – BACKUP DATE GATE", monthly, StringComparison.Ordinal);
        Assert.Contains("RequireBackupFinishDateAsync", monthly, StringComparison.Ordinal);
        Assert.Contains("MonthlyHoursComparePhase.PreDrop", monthly, StringComparison.Ordinal);
        Assert.Contains("InitializeReplicaDatabaseAsync", monthly, StringComparison.Ordinal);
        Assert.Contains("RunEtlPipelineAsync", monthly, StringComparison.Ordinal);
        Assert.Contains("MonthlyHoursComparePhase.PostEtl", monthly, StringComparison.Ordinal);
        Assert.Contains("StampMonthlyRestoreAsync", monthly, StringComparison.Ordinal);

        var gateIndex = monthly.IndexOf("STEP 0 – BACKUP DATE GATE", StringComparison.Ordinal);
        var restoreIndex = monthly.IndexOf("STEP 1 – RESTORE", StringComparison.Ordinal);
        var preCompareIndex = monthly.IndexOf("MonthlyHoursComparePhase.PreDrop", StringComparison.Ordinal);
        var initIndex = monthly.IndexOf("InitializeReplicaDatabaseAsync", StringComparison.Ordinal);
        var etlIndex = monthly.IndexOf("RunEtlPipelineAsync", StringComparison.Ordinal);
        var postCompareIndex = monthly.IndexOf("MonthlyHoursComparePhase.PostEtl", StringComparison.Ordinal);
        Assert.True(gateIndex < restoreIndex);
        Assert.True(restoreIndex < preCompareIndex);
        Assert.True(preCompareIndex < initIndex);
        Assert.True(initIndex < etlIndex);
        Assert.True(etlIndex < postCompareIndex);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenu(bool allowMonthly)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationQueryService>(new StubAuthorization(allowMonthly));
        services.AddSingleton<ICurrentUserContext>(new StubUserContext(1));
        services.AddTransient(_ => new MasterPlanMonthlyRestoreWindow(new MasterPlanMonthlyRestoreViewModel()));
        var sp = services.BuildServiceProvider();
        return NewShellMenuReflection.Build(new NewShellFactory(sp));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class StubAuthorization(bool allowMonthly) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult(allowMonthly && requiredRole <= AppRole.Management);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(allowMonthly && featureCode == AppFeatureCodes.ShellOpenMasterPlanMonthlyRestore);
    }

    private sealed class StubUserContext(int? userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }
}
