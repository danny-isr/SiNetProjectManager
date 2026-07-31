using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Catalog codes that should paint orange on the ProjectWork tree for the open task
/// (same gates as Complete). Browse / tasks without a file gate → empty.
/// </summary>
public static class ProjectWorkActiveRequiredCatalog
{
    // Keep in sync with ProjectWorkWindowViewModel completion-gate constants.
    public const string QuoteEstimate = "QuoteEstimate";
    public const string QuoteDocument = "QuoteDocument";
    public const string QuoteClientApproval = "QuoteClientApproval";

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> QuoteEstimateOnly =
        new HashSet<string>(StringComparer.Ordinal) { QuoteEstimate };

    private static readonly IReadOnlySet<string> QuoteDocumentOnly =
        new HashSet<string>(StringComparer.Ordinal) { QuoteDocument };

    private static readonly IReadOnlySet<string> QuoteClientApprovalOnly =
        new HashSet<string>(StringComparer.Ordinal) { QuoteClientApproval };

    /// <summary>Resolves active orange-gate catalog codes for <paramref name="context"/>.</summary>
    public static IReadOnlySet<string> Resolve(WorkSurfaceContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.TaskTypeCode))
            return Empty;

        if (string.Equals(context.TaskTypeCode, "PrepareQuoteCalculation", StringComparison.Ordinal))
            return QuoteEstimateOnly;

        if (string.Equals(context.TaskTypeCode, "PrepareQuoteDocument", StringComparison.Ordinal))
            return QuoteDocumentOnly;

        if (string.Equals(context.TaskTypeCode, "FollowQuoteApproval", StringComparison.Ordinal))
            return QuoteClientApprovalOnly;

        return Empty;
    }
}
