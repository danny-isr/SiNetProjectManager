using SiNet.Application.DevTools;
using SiNet.Application.Runtime;
using SiNet.Infrastructure.Sql.Services.Health;
using Xunit;

namespace SiNet.App.Wpf.Tests.DevTools;

public sealed class SeedBaselineVerifyResultTests
{
    [Fact]
    public void Evaluate_when_all_present_then_complete()
    {
        var result = SeedBaselineVerifyResult.Evaluate(
            SeedBaselineCatalog.RequiredWorkflowDefinitionCodes.ToList(),
            SeedBaselineCatalog.RequiredUserGroupCodes.ToList(),
            SeedBaselineCatalog.RequiredProjectFileCatalogCodes.ToList(),
            jobTypePresent: true,
            correspondenceFolderPresent: true);

        Assert.True(result.IsComplete);
        Assert.False(result.HasRequiredGaps);
        Assert.Contains("שלם", result.FormatSummaryHe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_when_Proposal_missing_then_required_gap()
    {
        var workflows = SeedBaselineCatalog.RequiredWorkflowDefinitionCodes
            .Where(c => !string.Equals(c, "Proposal", StringComparison.Ordinal))
            .ToList();

        var result = SeedBaselineVerifyResult.Evaluate(
            workflows,
            SeedBaselineCatalog.RequiredUserGroupCodes.ToList(),
            SeedBaselineCatalog.RequiredProjectFileCatalogCodes.ToList(),
            jobTypePresent: true,
            correspondenceFolderPresent: true);

        Assert.False(result.IsComplete);
        Assert.True(result.HasRequiredGaps);
        Assert.Contains("Proposal", result.MissingWorkflowDefinitionCodes, StringComparer.Ordinal);
        Assert.Contains("Proposal", result.FormatSummaryHe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_when_only_prerequisites_missing_then_warning_not_required_gap()
    {
        var result = SeedBaselineVerifyResult.Evaluate(
            SeedBaselineCatalog.RequiredWorkflowDefinitionCodes.ToList(),
            SeedBaselineCatalog.RequiredUserGroupCodes.ToList(),
            SeedBaselineCatalog.RequiredProjectFileCatalogCodes.ToList(),
            jobTypePresent: false,
            correspondenceFolderPresent: false);

        Assert.False(result.IsComplete);
        Assert.False(result.HasRequiredGaps);
        Assert.True(result.HasPrerequisiteWarnings);
        Assert.Contains(SeedBaselineCatalog.RequiredJobTypeTitle, result.FormatSummaryHe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedBaselineStatusContributor_when_Proposal_missing_then_degraded()
    {
        var verify = new StubVerify(SeedBaselineVerifyResult.Evaluate(
            SeedBaselineCatalog.RequiredWorkflowDefinitionCodes.Where(c => c != "Proposal").ToList(),
            SeedBaselineCatalog.RequiredUserGroupCodes.ToList(),
            SeedBaselineCatalog.RequiredProjectFileCatalogCodes.ToList(),
            jobTypePresent: true,
            correspondenceFolderPresent: true));

        var contributor = new SeedBaselineStatusContributor(verify);
        var status = await contributor.ContributeAsync();

        Assert.Equal(SeedBaselineStatusContributor.StatusKey, status.Key);
        Assert.Equal(SubsystemRuntimeState.Degraded, status.State);
        Assert.Contains("Proposal", status.SummaryHe, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedBaselineStatusContributor_when_complete_then_idle()
    {
        var verify = new StubVerify(SeedBaselineVerifyResult.Evaluate(
            SeedBaselineCatalog.RequiredWorkflowDefinitionCodes.ToList(),
            SeedBaselineCatalog.RequiredUserGroupCodes.ToList(),
            SeedBaselineCatalog.RequiredProjectFileCatalogCodes.ToList(),
            jobTypePresent: true,
            correspondenceFolderPresent: true));

        var contributor = new SeedBaselineStatusContributor(verify);
        var status = await contributor.ContributeAsync();

        Assert.Equal(SubsystemRuntimeState.Idle, status.State);
    }

    [Fact]
    public void GuidanceCatalog_seed_baseline_returns_seed_guidance()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "seed-baseline",
            SubsystemRuntimeState.Degraded,
            "חסרים Codes: Workflow: Proposal");

        Assert.NotNull(guidance);
        Assert.Contains("Seed בסיסי", guidance, StringComparison.Ordinal);
    }

    private sealed class StubVerify(SeedBaselineVerifyResult result) : ISeedBaselineVerifyService
    {
        public Task<SeedBaselineVerifyResult> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
