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

        Assert.Contains("BillingLocalDecisionApplier.Apply", source, StringComparison.Ordinal);
        Assert.Contains("IBillingReviewDecisionStore", source, StringComparison.Ordinal);
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
        Assert.Contains("IBillingReviewDecisionService", billing, StringComparison.Ordinal);
        Assert.Contains("IBillingReviewDecisionStore", billing, StringComparison.Ordinal);
        Assert.Contains("SqlBillingReviewDecisionService", billing, StringComparison.Ordinal);
        Assert.Contains("AddSiNetBillingSql", users, StringComparison.Ordinal);
        Assert.Contains("IBillingPreparationService", billing, StringComparison.Ordinal);
        Assert.Contains("BillingPreparationService", billing, StringComparison.Ordinal);
        Assert.Contains("SqlBillingPreparationComponentSource", billing, StringComparison.Ordinal);
    }

    [Fact]
    public void Preparation_component_source_uses_masterplan_name_columns()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Billing/SqlBillingPreparationComponentSource.cs");

        Assert.Contains("sc.FeeTypeID", source, StringComparison.Ordinal);
        Assert.Contains("c.ContractNum", source, StringComparison.Ordinal);
        Assert.Contains("sc.SubContractNum", source, StringComparison.Ordinal);
        Assert.Contains("st.OrderNum", source, StringComparison.Ordinal);
        Assert.Contains("CONCAT(c.FirstName, ' ', c.LastName)", source, StringComparison.Ordinal);
        Assert.Contains("NULLIF(LTRIM(RTRIM(comp.Name)), '')", source, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN dbo.Companies comp ON comp.ID = c.CompanyID", source, StringComparison.Ordinal);
        Assert.Contains("CONCAT(e.FirstName, ' ', e.LastName)", source, StringComparison.Ordinal);
        Assert.Contains("e.FirstName", source, StringComparison.Ordinal);
        Assert.Contains("e.LastName", source, StringComparison.Ordinal);
        Assert.Contains("MasterPlanDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(comp.Name, c.Name)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("c.Name AS CustomerName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("comp.Name AS CustomerName", source, StringComparison.Ordinal);
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
        Assert.Contains("BillingRecordReviewDecision", features, StringComparison.Ordinal);
        Assert.Contains("Billing.RecordReviewDecision", features, StringComparison.Ordinal);

        var auth = File.ReadAllText(RepoPath("src/SiNet.Application/Identity/AppFeatureAuthorization.cs"));
        Assert.Contains("ShellOpenBillingCenter", auth, StringComparison.Ordinal);
        Assert.Contains("BillingRecordReviewDecision", auth, StringComparison.Ordinal);
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
        Assert.Contains("IBillingReviewPrompts", di, StringComparison.Ordinal);
        Assert.Contains("IBillingReviewDecisionService", di, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IBillingPreparationService>()", di, StringComparison.Ordinal);
    }

    [Fact]
    public void Billing_wpf_consumes_read_service_and_local_decision_writes()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardViewModel.cs");
        var view = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardView.xaml");
        var formatters = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardFormatters.cs");
        var write = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Billing/SqlBillingReviewDecisionService.cs");
        var groupVm = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingPreparationSubContractGroupVm.cs");
        var stageVm = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingPreparationRequestRowVm.cs");
        var hourlyChoice = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingHourlySubContractChoiceVm.cs");
        var brushes = ReadRepoFile("src/SiNet.App.Wpf/Theme/BrushResources.xaml");

        Assert.Contains("IBillingDashboardReadService", vm, StringComparison.Ordinal);
        Assert.Contains("ContinuePrepareBillAsync", vm, StringComparison.Ordinal);
        Assert.Contains("EnsureFromPrepareBillAsync", vm, StringComparison.Ordinal);
        Assert.Contains("OperationErrorMessage", vm, StringComparison.Ordinal);
        Assert.Contains("ListForPreparationTabAsync", vm, StringComparison.Ordinal);
        Assert.Contains("BillingProjectFinancialSummaryCalculator", vm, StringComparison.Ordinal);
        Assert.Contains("FindSelectedProjectCandidate", vm, StringComparison.Ordinal);
        Assert.Contains("CurrentFeeSum", vm, StringComparison.Ordinal);
        Assert.Contains("SnapshotBalance", vm, StringComparison.Ordinal);
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
        Assert.Contains("החלטת ניהול", view, StringComparison.Ordinal);
        Assert.Contains("להכין חשבון", view, StringComparison.Ordinal);
        Assert.Contains("המשך להכנת חשבון", view, StringComparison.Ordinal);
        Assert.Contains("תוספת בחשבון הזה", view, StringComparison.Ordinal);
        Assert.Contains("בחר הכל", view, StringComparison.Ordinal);
        Assert.Contains("נקה הכל", view, StringComparison.Ordinal);
        Assert.Contains("בתת החוזה", view, StringComparison.Ordinal);
        Assert.Contains("משקל השלב בתת החוזה", view, StringComparison.Ordinal);
        Assert.Contains("תת חוזה: ", groupVm, StringComparison.Ordinal);
        Assert.Contains("משקל השלב בתת החוזה: ", stageVm, StringComparison.Ordinal);
        Assert.Contains("הרחב הכל", view, StringComparison.Ordinal);
        Assert.Contains("כווץ הכל", view, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.StageSearch", view, StringComparison.Ordinal);
        Assert.Contains("חיפוש תת חוזה או שלב", view, StringComparison.Ordinal);
        Assert.Contains("GridSplitter", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<ColumnDefinition Width=\"460\" MinWidth=\"320\" />", view, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"420\" />", view, StringComparison.Ordinal);
        Assert.Contains("BillingObservedBrush", view, StringComparison.Ordinal);
        Assert.Contains("BillingAdditionBrush", view, StringComparison.Ordinal);
        Assert.Contains("BillingRemainingBrush", view, StringComparison.Ordinal);
        Assert.Contains("BillingStageWeightBrush", view, StringComparison.Ordinal);
        Assert.Contains("AdditionAutomationId", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"{Binding AutomationId}\"", view, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.HourlySubContract.", hourlyChoice, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"BillingDashboard.ApprovePreparation\"", view, StringComparison.Ordinal);
        var approveIdx = view.IndexOf("BillingDashboard.ApprovePreparation", StringComparison.Ordinal);
        Assert.True(approveIdx >= 0);
        var approveWindow = view[Math.Max(0, approveIdx - 450)..Math.Min(view.Length, approveIdx + 80)];
        Assert.Contains("Command=\"{Binding ApprovePreparationCommand}\"", approveWindow, StringComparison.Ordinal);
        Assert.Contains("ApproveAndCreateTaskAsync", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"BillingDashboard.StageAddition\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("יעד מצטבר", view, StringComparison.Ordinal);
        Assert.DoesNotContain("0 ל-1", view, StringComparison.Ordinal);
        Assert.Contains("ShowContinuePrepareBillButton", view, StringComparison.Ordinal);
        Assert.Contains("ShowOperationErrorBanner", view, StringComparison.Ordinal);
        Assert.Contains("לא עכשיו", view, StringComparison.Ordinal);
        Assert.Contains("בטל החלטה", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"פעולות\"", view, StringComparison.Ordinal);
        Assert.Contains("CurrentBalanceForbiddenLabel", formatters, StringComparison.Ordinal);
        Assert.Contains("ShowOperationalChrome", view, StringComparison.Ordinal);
        Assert.Contains("ShowBlockedPanel", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"מצב\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"החלטה\"", view, StringComparison.Ordinal);
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
        Assert.Contains("מצב כספי לאחר החשבון", view, StringComparison.Ordinal);
        Assert.Contains("יתרה להגשה לפני החשבון", view, StringComparison.Ordinal);
        Assert.Contains("יתרה להגשה אחרי החשבון", view, StringComparison.Ordinal);
        Assert.Contains("כבר חויב", view, StringComparison.Ordinal);
        Assert.Contains("החשבון הזה", view, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.ProjectFinancialSummary", view, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.ProjectFinancialBar", view, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.ProjectFinancialSource", view, StringComparison.Ordinal);
        Assert.Contains("ProjectObservedBarShare", view, StringComparison.Ordinal);
        Assert.Contains("BillingStageWeightBrush", view, StringComparison.Ordinal);
        Assert.DoesNotContain("יתרה נוכחית", view, StringComparison.Ordinal);
        Assert.DoesNotContain("#047857", view, StringComparison.Ordinal);
        Assert.DoesNotContain("#7B1FA2", view, StringComparison.Ordinal);
        Assert.Contains("BillingObservedBrush", brushes, StringComparison.Ordinal);
        Assert.Contains("BillingAdditionBrush", brushes, StringComparison.Ordinal);
        Assert.Contains("BillingRemainingBrush", brushes, StringComparison.Ordinal);
        Assert.Contains("BillingStageWeightBrush", brushes, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireReplica", write, StringComparison.Ordinal);
        Assert.DoesNotContain("IReplicaBillingDataSource", write, StringComparison.Ordinal);
    }

    [Fact]
    public void Healthy_visual_fixture_is_not_a_production_replica_bypass()
    {
        var fixture = ReadRepoFile("src/SiNet.App.Wpf/Billing/BillingDashboardHealthyVisualFixture.cs");
        var di = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        var billingSql = ReadRepoFile("src/SiNet.Infrastructure.Sql/BillingServiceCollectionExtensions.cs");

        Assert.Contains("IBillingDashboardReadService", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlBillingDashboardReadService", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlBillingReviewDecisionService", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireReplica", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<IBillingDashboardReadService", di, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IBillingDashboardReadService>()", di, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IBillingPreparationService>()", di, StringComparison.Ordinal);
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
