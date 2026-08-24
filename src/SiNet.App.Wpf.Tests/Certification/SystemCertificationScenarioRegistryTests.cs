using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationScenarioRegistryTests
{
    [Fact]
    public void Registry_covers_every_seed_baseline_workflow()
    {
        SystemCertificationScenarioRegistry.AssertRegistryMatchesSeedBaseline();
    }

    [Fact]
    public void Registry_classifies_every_required_seed_code()
    {
        foreach (var code in SiNet.Application.DevTools.SeedBaselineCatalog.RequiredWorkflowDefinitionCodes)
        {
            Assert.True(
                SystemCertificationScenarioRegistry.CoverageClassifications.ContainsKey(code),
                $"missing classification for {code}");
        }
    }
}
