using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>Fallback when AccBootstrap membership probe is not registered.</summary>
public sealed class NullAccHumanMembershipProbe : IAccHumanMembershipProbe
{
    public Task<AccHumanMembershipProbeResult?> ProbeAsync(
        string? accProjectId,
        string expectedEmail,
        bool allowReconcile = true,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AccHumanMembershipProbeResult?>(null);

    public Task<AccHumanMembershipProbeResult?> ProbeForSiProjectAsync(
        int siProjectId,
        string expectedEmail,
        bool allowReconcile = true,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AccHumanMembershipProbeResult?>(null);
}
