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

    [Fact]
    public void WhenOnlyEmailMatchesThenCompanyIsNotAutoMatched()
    {
        var load = new MasterPlanMappingLoadResult(
            [
                new MasterPlanCompanyMappingDto(1, "חן-מור חב' לבניה", "shared@example.com", null, 1, 0, true, null, null, false),
            ],
            [],
            [
                new MpCompanyOptionDto(3503, "קסנטיני שמעון ובניו בע\"מ", "shared@example.com", null, "515682334", null, null),
            ],
            [],
            null);

        var result = MasterPlanAutoMatchEngine.Apply(load);

        Assert.Null(result.Companies[0].MasterPlanCompanyId);
        Assert.False(result.Companies[0].IsAutoMatch);
    }

    [Fact]
    public void WhenRegistrationInTitleMatchesThenCompanyIsAutoMatched()
    {
        var load = new MasterPlanMappingLoadResult(
            [
                new MasterPlanCompanyMappingDto(1, "חן-מור חב' לבניה ח.פ. 512931932", "shared@example.com", null, 1, 0, true, null, null, false),
            ],
            [],
            [
                new MpCompanyOptionDto(3503, "קסנטיני שמעון ובניו בע\"מ", "shared@example.com", null, "515682334", null, null),
                new MpCompanyOptionDto(2837, "חן-מור חברה לבניה ויזום בע\"מ", null, null, "512931932", null, null),
            ],
            [],
            null);

        var result = MasterPlanAutoMatchEngine.Apply(load);

        Assert.Equal(2837, result.Companies[0].MasterPlanCompanyId);
        Assert.True(result.Companies[0].IsAutoMatch);
    }

    [Fact]
    public void WhenOnlyEmailMatchesThenContactIsNotAutoMatched()
    {
        var load = new MasterPlanMappingLoadResult(
            [],
            [
                new MasterPlanContactMappingDto(1, "בני פלד", null, null, "shared@office.com", null, 0, true, null, null, false),
            ],
            [],
            [
                new MpContactOptionDto(99, "יוסי", "יוסי שמעון", null, null, "shared@office.com", null, null, null),
            ],
            null);

        var result = MasterPlanAutoMatchEngine.Apply(load);

        Assert.Null(result.Contacts[0].MasterPlanContactId);
        Assert.False(result.Contacts[0].IsAutoMatch);
    }

    [Fact]
    public void WhenContactNameAndEmailMatchThenContactIsAutoMatched()
    {
        var load = new MasterPlanMappingLoadResult(
            [],
            [
                new MasterPlanContactMappingDto(1, "יוסי שמעון", null, null, "yossi@example.com", null, 0, true, null, null, false),
            ],
            [],
            [
                new MpContactOptionDto(99, "יוסי", "יוסי שמעון", null, null, "yossi@example.com", null, null, null),
            ],
            null);

        var result = MasterPlanAutoMatchEngine.Apply(load);

        Assert.Equal(99, result.Contacts[0].MasterPlanContactId);
        Assert.True(result.Contacts[0].IsAutoMatch);
    }
}
