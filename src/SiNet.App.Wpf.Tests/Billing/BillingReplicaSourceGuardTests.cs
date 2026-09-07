using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingReplicaSourceGuardTests
{
    [Fact]
    public void Replica_data_source_requires_replica_and_uses_extended_hours()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Billing/ReplicaBillingDataSource.cs");

        Assert.Contains("MasterPlanReportSqlSourceResolver.RequireReplica", source, StringComparison.Ordinal);
        Assert.Contains("MP_ProjectHoursExtended", source, StringComparison.Ordinal);
        Assert.Contains("MP_Projects", source, StringComparison.Ordinal);
        Assert.Contains("MP_Bills", source, StringComparison.Ordinal);
        Assert.Contains("MP_Intakes", source, StringComparison.Ordinal);
        Assert.Contains("Sync_State", source, StringComparison.Ordinal);
        Assert.Contains("MasterPlanHoursNormalizer", source, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", source, StringComparison.Ordinal);
        Assert.Contains("MP_ProjectHours h", source, StringComparison.Ordinal);
        Assert.Contains("MP_ProjectHoursExtended is missing", source, StringComparison.Ordinal);
        Assert.Contains("no live MasterPlan or MP_ProjectHours fallback", source, StringComparison.Ordinal);
        Assert.Contains("ProbeAsync", source, StringComparison.Ordinal);
        Assert.Contains("@@SERVERNAME", source, StringComparison.Ordinal);
        Assert.Contains("SERVERPROPERTY", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HoursReports", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMasterPlanMaxDate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectsExtraData", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MasterPlanReportSqlSourceResolver.Resolve", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Billing_sql_service_does_not_fill_snapshot_or_submitted_open_amount()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Billing/SqlBillingDashboardReadService.cs");

        Assert.Contains("SubmittedOpenAmount: null", source, StringComparison.Ordinal);
        Assert.Contains("BillingCandidateEngine.BuildRows", source, StringComparison.Ordinal);
        Assert.Contains("BillingSnapshotEnrichmentApplier.Apply", source, StringComparison.Ordinal);
        Assert.Contains("BillingReplicaFreshnessEvaluator.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("CandidatesBlocked", source, StringComparison.Ordinal);
        Assert.Contains("ProbeAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SI-WIN-2K19", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentOpenBillSum", source, StringComparison.Ordinal);

        var warnings = ReadRepoFile("src/SiNet.Application/Billing/BillingDashboardWarningBuilder.cs");
        Assert.Contains("RealBillSubmitDateFallback", warnings, StringComparison.Ordinal);
        Assert.Contains("HoursParityOnlyInBasic", warnings, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectsExtraData", source, StringComparison.Ordinal);
    }

    [Fact]
    public void R02_hours_conversion_delegates_to_shared_normalizer()
    {
        var r02 = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/MasterPlan/Reports/SqlR02ReportDataSource.cs");
        var helper = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/MasterPlan/MasterPlanHoursNormalizer.cs");

        Assert.Contains("MasterPlanHoursNormalizer.ConvertHoursRaw", r02, StringComparison.Ordinal);
        Assert.Contains("ConvertExtendedHours", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMasterPlanMaxDate", r02, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSiNetBillingSql_is_registered_from_user_management()
    {
        var billing = ReadRepoFile("src/SiNet.Infrastructure.Sql/BillingServiceCollectionExtensions.cs");
        var users = ReadRepoFile("src/SiNet.Infrastructure.Sql/UserManagementServiceCollectionExtensions.cs");

        Assert.Contains("IBillingDashboardReadService", billing, StringComparison.Ordinal);
        Assert.Contains("SqlBillingDashboardReadService", billing, StringComparison.Ordinal);
        Assert.Contains("ReplicaBillingDataSource", billing, StringComparison.Ordinal);
        Assert.Contains("IReplicaBillingDataSource", billing, StringComparison.Ordinal);
        Assert.Contains("IMonthlyBillingEnrichmentDataSource", billing, StringComparison.Ordinal);
        Assert.Contains("MonthlyBillingEnrichmentDataSource", billing, StringComparison.Ordinal);
        Assert.Contains("AddSiNetBillingSql", users, StringComparison.Ordinal);
    }

    [Fact]
    public void Monthly_enrichment_reads_snapshot_db_not_replica_current_facts()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Billing/MonthlyBillingEnrichmentDataSource.cs");

        Assert.Contains("MasterPlanDatabase", source, StringComparison.Ordinal);
        Assert.Contains("ProjectsExtraData", source, StringComparison.Ordinal);
        Assert.Contains("SubContracts", source, StringComparison.Ordinal);
        Assert.Contains("FeeTypeID", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireReplica", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MP_ProjectHoursExtended", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HoursReports", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentsStep", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMasterPlanMaxDate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void B4_shell_exposes_billing_center_under_finance_group()
    {
        Assert.True(File.Exists(RepoPath("src/SiNet.Application/Billing/IBillingDashboardReadService.cs")));
        Assert.True(File.Exists(RepoPath("docs/BILLING_CONTROL_CENTER_V1_IMPLEMENTATION_PLAN.md")));
        Assert.True(Directory.Exists(RepoPath("src/SiNet.App.Wpf/Billing")));
        Assert.True(File.Exists(RepoPath("src/SiNet.App.Wpf/Billing/BillingDashboardWindow.cs")));
        Assert.True(File.Exists(RepoPath("src/SiNet.App.Wpf/Billing/BillingDashboardViewModel.cs")));

        var features = File.ReadAllText(RepoPath("src/SiNet.Application/Identity/AppFeatureCodes.cs"));
        Assert.Contains("Shell.OpenBillingCenter", features, StringComparison.Ordinal);
        Assert.Contains("ShellOpenBillingCenter", features, StringComparison.Ordinal);

        var auth = File.ReadAllText(RepoPath("src/SiNet.Application/Identity/AppFeatureAuthorization.cs"));
        Assert.Contains("ShellOpenBillingCenter", auth, StringComparison.Ordinal);
        Assert.Contains("AppRole.Management", auth, StringComparison.Ordinal);

        var shell = File.ReadAllText(RepoPath("src/SiNet.App.Wpf/Shell/NewShellFactory.cs"));
        Assert.Contains("BillingDashboardWindow", shell, StringComparison.Ordinal);
        Assert.Contains("OpenNativeBillingDashboard", shell, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.ShellOpenBillingCenter", shell, StringComparison.Ordinal);
        Assert.Contains("כספים", shell, StringComparison.Ordinal);
        Assert.Contains("מרכז חיובים", shell, StringComparison.Ordinal);

        var di = File.ReadAllText(RepoPath("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs"));
        Assert.Contains("BillingDashboardViewModel", di, StringComparison.Ordinal);
        Assert.Contains("BillingDashboardWindow", di, StringComparison.Ordinal);
    }

    [Fact]
    public void Billing_wpf_consumes_read_service_only()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardViewModel.cs");
        var view = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardView.xaml");
        var formatters = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardFormatters.cs");

        Assert.Contains("IBillingDashboardReadService", vm, StringComparison.Ordinal);
        Assert.Contains("new BillingDashboardRequest(ActiveOnly: ActiveOnly)", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlConnection", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectsExtraData", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("BillingCandidateStateResolver", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireReplica", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("MP_ProjectHours", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("אין פרויקטים לחיוב", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("אין פרויקטים לחיוב", view, StringComparison.Ordinal);
        Assert.DoesNotContain("יתרה נוכחית", view, StringComparison.Ordinal);
        Assert.Contains("יתרה לפי snapshot", view, StringComparison.Ordinal);
        Assert.Contains("נתונים עדכניים — Replica", view, StringComparison.Ordinal);
        Assert.Contains("נתוני snapshot חודשי", view, StringComparison.Ordinal);
        Assert.Contains("למה הפרויקט מופיע כאן", view, StringComparison.Ordinal);
        Assert.Contains("CurrentBalanceForbiddenLabel", formatters, StringComparison.Ordinal);
        Assert.Contains("ShowOperationalChrome", view, StringComparison.Ordinal);
        Assert.Contains("ShowBlockedPanel", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"מצב\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"יתרה לפי snapshot\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"סיבה\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"ימי עבודה 30\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"סכום שכר טרחה\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"חשבונות פתוחים לפי snapshot\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"% מחויב לפי snapshot\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"סיווג שכר טרחה\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"עבודה אחרונה\"", view, StringComparison.Ordinal);
        Assert.Contains("ימי עבודה 30", view, StringComparison.Ordinal);
        Assert.Contains("סכום שכר טרחה נוכחי", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Healthy_visual_fixture_is_not_a_production_replica_bypass()
    {
        var fixture = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardHealthyVisualFixture.cs");
        var di = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        var billingSql = ReadRepoFile("src/SiNet.Infrastructure.Sql/BillingServiceCollectionExtensions.cs");

        Assert.Contains("IBillingDashboardReadService", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlBillingDashboardReadService", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireReplica", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<IBillingDashboardReadService", di, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IBillingDashboardReadService>()", di, StringComparison.Ordinal);
        Assert.Contains("SqlBillingDashboardReadService", billingSql, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoPath(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
