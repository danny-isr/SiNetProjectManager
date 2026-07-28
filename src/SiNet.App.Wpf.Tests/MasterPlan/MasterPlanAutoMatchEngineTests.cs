using SiNet.Application.MasterPlan;
using Xunit;

namespace SiNet.App.Wpf.Tests.MasterPlan;

public sealed class MasterPlanAutoMatchEngineTests
{
    [Fact]
    public void Exact_company_name_matches_above_threshold()
    {
        var load = new MasterPlanMappingLoadResult(
            [
                new MasterPlanCompanyMappingDto(1, "Acme Ltd", null, null, 1, 0, true, null, null, false),
            ],
            [],
            [
                new MpCompanyOptionDto(10, "Acme Ltd", null, null, null, null, null),
            ],
            [],
            null);

        var result = MasterPlanAutoMatchEngine.Apply(load);

        Assert.Equal(10, result.Companies[0].MasterPlanCompanyId);
        Assert.True(result.Companies[0].IsAutoMatch);
    }

    [Fact]
    public void Does_not_reuse_already_mapped_mp_company()
    {
        var load = new MasterPlanMappingLoadResult(
            [
                new MasterPlanCompanyMappingDto(1, "Acme", null, null, 0, 0, true, 10, "קיים", false),
                new MasterPlanCompanyMappingDto(2, "Acme", null, null, 0, 0, true, null, null, false),
            ],
            [],
            [
                new MpCompanyOptionDto(10, "Acme", null, null, null, null, null),
            ],
            [],
            null);

        var result = MasterPlanAutoMatchEngine.Apply(load);
        var second = result.Companies.Single(c => c.SiNetId == 2);
        Assert.Null(second.MasterPlanCompanyId);
    }
}
