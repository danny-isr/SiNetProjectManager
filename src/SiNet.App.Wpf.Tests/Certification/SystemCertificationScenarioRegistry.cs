using SiNet.Application.DevTools;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Maps every active workflow definition to a certification scenario or an explicit classification.
/// Adding a workflow to the seed without updating this registry must fail the coverage gate.
/// </summary>
internal static class SystemCertificationScenarioRegistry
{
    internal sealed record ScenarioEntry(
        string Code,
        WorkflowCoverageInventory.Classification Classification,
        string Reason,
        string ScenarioId);

    private static readonly IReadOnlyDictionary<string, ScenarioEntry> Entries =
        new Dictionary<string, ScenarioEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["Proposal"] = new(
                "Proposal",
                WorkflowCoverageInventory.Classification.Certified,
                "PRP full corridor including continuation into PLN",
                "cert.prp"),
            ["Opinion"] = new(
                "Opinion",
                WorkflowCoverageInventory.Classification.Certified,
                "OPN full scenario from email start",
                "cert.opn"),
            ["MaterialIntake"] = new(
                "MaterialIntake",
                WorkflowCoverageInventory.Classification.Certified,
                "MAT sub-workflow exercised via PLN/REV parent scenarios",
                "cert.mat"),
            ["PlanningWorkflow"] = new(
                "PlanningWorkflow",
                WorkflowCoverageInventory.Classification.Certified,
                "PLN full scenario including SubWorkflow wait/recovery proof",
                "cert.pln"),
            ["Review"] = new(
                "Review",
                WorkflowCoverageInventory.Classification.Blocked,
                "no email start mapping; no ProjectType continuation into REV",
                "cert.rev.blocked"),
            ["Outsourcing"] = new(
                "Outsourcing",
                WorkflowCoverageInventory.Classification.Blocked,
                "no TaskResult codes; transitions rely on AllTasksComplete only",
                "cert.out.blocked"),
        };

    public static IReadOnlyDictionary<string, (WorkflowCoverageInventory.Classification Classification, string Reason)>
        CoverageClassifications =>
        Entries.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.Classification, pair.Value.Reason),
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ScenarioEntry> All => Entries.Values.ToList();

    /// <summary>
    /// Fails when the live inventory contains a definition this registry does not classify.
    /// </summary>
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
    /// Ensures the registry still covers every code the seed baseline requires.
    /// </summary>
    public static void AssertRegistryMatchesSeedBaseline()
    {
        var missing = SeedBaselineCatalog.RequiredWorkflowDefinitionCodes
            .Where(code => !Entries.ContainsKey(code))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Certification scenario registry is missing seed baseline workflows: "
                + string.Join(", ", missing));
        }
    }
}
