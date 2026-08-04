using SiNet.Domain.Files;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// Settings-backed ProjectWork scan exclusion rules (extensions + name prefixes).
/// Sidecar companions (<c>*.si.json</c>) remain hard-coded outside this policy.
/// </summary>
public interface IProjectWorkScanExclusionPolicy
{
    /// <summary>Currently cached parsed rules (defaults until first refresh/replace).</summary>
    ParsedProjectWorkScanExclusions CurrentRules { get; }

    /// <summary>True when <paramref name="fullPathOrName"/> matches the current exclusion rules.</summary>
    bool ShouldExclude(string? fullPathOrName);

    /// <summary>Replaces the cache from a CSV (e.g. after System Settings save).</summary>
    void ReplaceRules(string? rulesCsv);

    /// <summary>Reloads rules from <c>SystemSettings</c> (fallback = domain default CSV).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
