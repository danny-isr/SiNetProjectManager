using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf;

/// <summary>
/// Fail-closed Release guard: the production desktop host must not register DEV/test harness
/// types. See <c>docs/RELEASE_AUTOMATION_LOCKDOWN.md</c>.
/// </summary>
public static class ReleaseDevAutomationGuard
{
    /// <summary>
    /// Type names that must never appear as DI implementation types in a Release standalone host.
    /// </summary>
    internal static readonly string[] ProhibitedImplementationTypeNames =
    [
        "InspectionShellViewModel",
        "InspectionShellView",
        "InspectionTreeViewModel",
        "InspectionNotesViewModel",
        "InspectionDrawingsViewModel",
        "InspectionReviewedPlanViewModel",
        "InspectionReportViewModel",
        "SqlWorkflowSeedService",
        "SqlTaskDemoSeedService",
        "SqlStaticSeedService",
        "SqlDevDataResetService",
        "DevToolsCoordinator",
    ];

    /// <summary>
    /// Throws if the service collection contains a prohibited DEV/test implementation type.
    /// Call only from Release composition (<c>#if !DEBUG</c>).
    /// </summary>
    public static void AssertStandaloneHostDescriptorsClean(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var descriptor in services)
        {
            var implName = descriptor.ImplementationType?.Name;
            if (string.IsNullOrEmpty(implName))
                continue;

            foreach (var prohibited in ProhibitedImplementationTypeNames)
            {
                if (!string.Equals(implName, prohibited, StringComparison.Ordinal))
                    continue;

                throw new InvalidOperationException(
                    $"Release composition rejected DEV/test registration '{implName}'. " +
                    "See docs/RELEASE_AUTOMATION_LOCKDOWN.md.");
            }
        }
    }
}
