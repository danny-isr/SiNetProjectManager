namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Shared assertions every certification scenario reuses so evidence, integrity and gate semantics stay
/// consistent.
/// </summary>
internal static class SystemCertificationAssertions
{
    public static void AssertDeltaClean(
        SystemCertificationIntegrityValidator.Report report,
        SystemCertificationEvidence evidence,
        string step)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(evidence);

        if (report.IsDeltaClean)
        {
            evidence.Pass(step, report.DescribeDelta());
            return;
        }

        evidence.Fail(step, report.DescribeDelta());
    }

    public static void AssertAbsoluteClean(
        SystemCertificationIntegrityValidator.Report report,
        SystemCertificationEvidence evidence,
        string step)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(evidence);

        if (report.IsAbsolutelyClean)
        {
            evidence.Pass(step, report.DescribeAbsolute());
            return;
        }

        evidence.Fail(step, report.DescribeAbsolute());
    }

    public static void AssertRunnableScenariosLinked(
        SystemCertificationEvidence evidence,
        string step)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        try
        {
            SystemCertificationScenarioRegistry.AssertRunnableScenariosLinked();
            evidence.Pass(step, "every ScenarioRequired workflow is linked to a runnable scenario type");
        }
        catch (InvalidOperationException ex)
        {
            evidence.Fail(step, ex.Message);
        }
    }

    public static void AssertCoverageComplete(
        WorkflowCoverageInventory.Inventory inventory,
        SystemCertificationEvidence evidence,
        string step)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(evidence);

        try
        {
            SystemCertificationScenarioRegistry.AssertFullCoverage(inventory);
            evidence.Pass(step, WorkflowCoverageInventory.Describe(inventory));
        }
        catch (InvalidOperationException ex)
        {
            evidence.Fail(step, ex.Message);
        }
    }

    public static void RequirePass(SystemCertificationEvidence evidence, string step, string detail)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Pass(step, detail);
    }

    public static void RequireFail(SystemCertificationEvidence evidence, string step, string detail)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Fail(step, detail);
    }

    public static void RequireBlocked(SystemCertificationEvidence evidence, string step, string detail)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Blocked(step, detail);
    }
}
