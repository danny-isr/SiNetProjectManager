using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Wave A/B wiring guardrails for Email Manual QA resume.
/// </summary>
public sealed class EmailManualQaBoundaryTests
{
    [Fact]
    public void WaveA_email_window_has_auto_refresh_and_background_feedback()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.Contains("AutoRefreshOnOpenAsync", vm, StringComparison.Ordinal);
        Assert.Contains("IEmailAccBackgroundWorkTracker", vm, StringComparison.Ordinal);
        Assert.Contains("BackgroundWorkDisplay", vm, StringComparison.Ordinal);
        Assert.Contains("TryBlockCloseForBackgroundWork", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void WaveA_surface_binds_background_work_display()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        Assert.Contains("BackgroundWorkDisplay", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WaveA_close_dialog_distinguishes_email_window_from_app_exit()
    {
        var scope = ReadRepoFile("SiNetProjectManagerV2/Dialogs/BackgroundCloseScope.cs");
        Assert.Contains("EmailWindow", scope, StringComparison.Ordinal);
        Assert.Contains("Application", scope, StringComparison.Ordinal);

        var dialog = ReadRepoFile("SiNetProjectManagerV2/Dialogs/BackgroundUploadsDialog.xaml.cs");
        Assert.Contains("המשך ברקע", dialog, StringComparison.Ordinal);
        Assert.Contains("BackgroundCloseScope.EmailWindow", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void WaveB_create_price_quote_action_code_exists()
    {
        var codes = ReadRepoFile("src/SiNet.Application/Email/Detail/IEmailWorkflowContextService.cs");
        Assert.Contains("CreatePriceQuote", codes, StringComparison.Ordinal);
        Assert.Contains("RejectPriceQuote", codes, StringComparison.Ordinal);
    }

    [Fact]
    public void WaveB_wf_debug_instrumentation_and_runbook_present()
    {
        var trace = ReadRepoFile("src/SiNet.Application/Diagnostics/WorkflowDebugTrace.cs");
        Assert.Contains("[WF-STEP]", trace, StringComparison.Ordinal);
        Assert.Contains("workflow-manual-debug.log", trace, StringComparison.Ordinal);
        Assert.Contains("TEMP WF-DEBUG", trace, StringComparison.Ordinal);

        var runbook = ReadRepoFile("docs/manual-tests/PROPOSAL_WORKFLOW_MANUAL_TEST.md");
        Assert.Contains("CreatePriceQuote", runbook, StringComparison.Ordinal);
        Assert.Contains("פתיחת הצעת מחיר", runbook, StringComparison.Ordinal);
        Assert.Contains("Email.Action", runbook, StringComparison.Ordinal);
        Assert.Contains("PRP.ProjectSetup", runbook, StringComparison.Ordinal);
    }

    [Fact]
    public void WaveB_email_detail_offers_create_price_quote()
    {
        var detail = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        Assert.Contains("CreatePriceQuote", detail, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
