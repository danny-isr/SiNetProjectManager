using SiNet.Application.DevTools;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Static plan for how each active workflow definition is accounted for in certification.
/// <para>
/// <see cref="ScenarioDisposition"/> is registry disposition only — it must not be confused with a runtime
/// certification result. A workflow marked <see cref="ScenarioDisposition.ScenarioRequired"/> must have a
/// concrete <see cref="ISystemCertificationScenario"/> implementation linked here, not merely a scenario id
/// string. Whether the scenario ultimately Passes, Fails, or Blocks is decided only after
/// <see cref="ISystemCertificationScenario.RunAsync"/>.
/// </para>
/// </summary>
internal static class SystemCertificationScenarioRegistry
{
    /// <summary>How the registry accounts for a workflow before any scenario runs.</summary>
    internal enum ScenarioDisposition
    {
        /// <summary>A runnable scenario implementation must exist and will be executed.</summary>
        ScenarioRequired,

        /// <summary>Cannot be certified end-to-end because of a documented product or seed gap.</summary>
        ScenarioBlocked,

        /// <summary>Out of scope for this tier, with a written reason.</summary>
        NotApplicable,
    }

    internal sealed record ScenarioPlan(
        string WorkflowCode,
        ScenarioDisposition Disposition,
        string Reason,
        string ScenarioId,
        Type? RunnableScenarioType);

    private static readonly IReadOnlyList<ScenarioPlan> Plans =
    [
        new(
            "Proposal",
            ScenarioDisposition.ScenarioRequired,
            "PRP full corridor including continuation into PLN",
            Scenarios.SystemCertificationPrpScenario.Id,
            typeof(Scenarios.SystemCertificationPrpScenario)),
        new(
            "Opinion",
            ScenarioDisposition.ScenarioRequired,
            "OPN full scenario from email start",
            Scenarios.SystemCertificationOpnScenario.Id,
            typeof(Scenarios.SystemCertificationOpnScenario)),
        new(
            "MaterialIntake",
            ScenarioDisposition.ScenarioRequired,
            "MAT sub-workflow exercised via PLN/REV parent scenarios",
            Scenarios.SystemCertificationMatSubWorkflowScenario.Id,
            typeof(Scenarios.SystemCertificationMatSubWorkflowScenario)),
        new(
            "PlanningWorkflow",
            ScenarioDisposition.ScenarioRequired,
            "PLN full scenario including SubWorkflow wait/recovery proof",
            Scenarios.SystemCertificationPlnScenario.Id,
            typeof(Scenarios.SystemCertificationPlnScenario)),
        new(
            "Review",
            ScenarioDisposition.ScenarioBlocked,
            "no email start mapping; no ProjectType continuation into REV",
            "cert.rev.blocked",
            null),
        new(
            "Outsourcing",
            ScenarioDisposition.ScenarioBlocked,
            "no TaskResult codes; transitions rely on AllTasksComplete only",
            "cert.out.blocked",
            null),
    ];

    private static readonly IReadOnlyDictionary<string, ScenarioPlan> PlanByWorkflowCode =
        Plans.ToDictionary(p => p.WorkflowCode, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, ScenarioPlan> PlanByScenarioId =
        Plans
            .Where(p => p.RunnableScenarioType is not null)
            .ToDictionary(p => p.ScenarioId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ScenarioPlan> All => Plans;

    public static IReadOnlyDictionary<string, (WorkflowCoverageInventory.ScenarioDisposition Disposition, string Reason)>
        CoverageDispositions =>
        Plans.ToDictionary(
            p => p.WorkflowCode,
            p => (MapDisposition(p.Disposition), p.Reason),
            StringComparer.OrdinalIgnoreCase);

    public static ISystemCertificationScenario CreateScenario(ScenarioPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.RunnableScenarioType is null)
        {
            throw new InvalidOperationException(
                $"Workflow '{plan.WorkflowCode}' has no runnable scenario type.");
        }

        return (ISystemCertificationScenario)Activator.CreateInstance(plan.RunnableScenarioType)!;
    }

    public static ISystemCertificationScenario CreateScenario(string scenarioId) =>
        CreateScenario(PlanByScenarioId[scenarioId]);

    public static IReadOnlyList<ISystemCertificationScenario> CreateAllRunnableScenarios() =>
        Plans
            .Where(p => p.Disposition == ScenarioDisposition.ScenarioRequired)
            .Select(CreateScenario)
            .ToList();

    /// <summary>Fails when the live inventory contains a workflow this registry does not classify.</summary>
    public static void AssertFullCoverage(WorkflowCoverageInventory.Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        if (inventory.Unclassified.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Active workflow definitions are not fully classified for certification: "
            + string.Join(", ", inventory.Unclassified));
    }

    /// <summary>
    /// Every <see cref="ScenarioDisposition.ScenarioRequired"/> workflow must be linked to a concrete
    /// runnable scenario type whose <see cref="ISystemCertificationScenario.WorkflowDefinitionCodes"/>
    /// includes that workflow.
    /// </summary>
    public static void AssertRunnableScenariosLinked()
    {
        foreach (var plan in Plans.Where(p => p.Disposition == ScenarioDisposition.ScenarioRequired))
        {
            if (plan.RunnableScenarioType is null)
            {
                throw new InvalidOperationException(
                    $"Workflow '{plan.WorkflowCode}' is ScenarioRequired but has no runnable scenario type.");
            }

            if (!typeof(ISystemCertificationScenario).IsAssignableFrom(plan.RunnableScenarioType))
            {
                throw new InvalidOperationException(
                    $"Scenario type '{plan.RunnableScenarioType.Name}' for '{plan.WorkflowCode}' "
                    + "does not implement ISystemCertificationScenario.");
            }

            var scenario = CreateScenario(plan);
            if (!scenario.WorkflowDefinitionCodes.Contains(
                    plan.WorkflowCode,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Scenario '{plan.ScenarioId}' does not declare workflow '{plan.WorkflowCode}'.");
            }

            if (!string.Equals(scenario.ScenarioId, plan.ScenarioId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Scenario type '{plan.RunnableScenarioType.Name}' reports id '{scenario.ScenarioId}' "
                    + $"but registry expects '{plan.ScenarioId}'.");
            }
        }
    }

    public static void AssertRegistryMatchesSeedBaseline()
    {
        var missing = SeedBaselineCatalog.RequiredWorkflowDefinitionCodes
            .Where(code => !PlanByWorkflowCode.ContainsKey(code))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Certification scenario registry is missing seed baseline workflows: "
                + string.Join(", ", missing));
        }
    }

    private static WorkflowCoverageInventory.ScenarioDisposition MapDisposition(ScenarioDisposition disposition) =>
        disposition switch
        {
            ScenarioDisposition.ScenarioRequired => WorkflowCoverageInventory.ScenarioDisposition.ScenarioRequired,
            ScenarioDisposition.ScenarioBlocked => WorkflowCoverageInventory.ScenarioDisposition.ScenarioBlocked,
            ScenarioDisposition.NotApplicable => WorkflowCoverageInventory.ScenarioDisposition.NotApplicable,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null),
        };
}
