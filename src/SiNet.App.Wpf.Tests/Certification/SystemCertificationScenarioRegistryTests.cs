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
                SystemCertificationScenarioRegistry.CoverageDispositions.ContainsKey(code),
                $"missing disposition for {code}");
        }
    }

    [Fact]
    public void Every_scenario_required_workflow_is_linked_to_a_runnable_implementation()
    {
        SystemCertificationScenarioRegistry.AssertRunnableScenariosLinked();
    }

    [Fact]
    public void Prp_scenario_is_registered_and_resolvable()
    {
        var scenario = SystemCertificationScenarioRegistry.CreateScenario(
            Scenarios.SystemCertificationPrpScenario.Id);

        Assert.IsType<Scenarios.SystemCertificationPrpScenario>(scenario);
        Assert.Contains("Proposal", scenario.WorkflowDefinitionCodes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scenario_required_plans_do_not_use_runtime_certified_status()
    {
        foreach (var plan in SystemCertificationScenarioRegistry.All.Where(
                     p => p.Disposition == SystemCertificationScenarioRegistry.ScenarioDisposition.ScenarioRequired))
        {
            Assert.NotNull(plan.RunnableScenarioType);
            Assert.DoesNotContain("Certified", plan.ScenarioId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
