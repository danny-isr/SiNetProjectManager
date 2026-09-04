using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>Default no-op ACC membership probe (registered when no Autodesk membership client is wired).</summary>
public sealed class NullAccHumanMembershipProbe : IAccHumanMembershipProbe
{
    public Task<AccHumanMembershipProbeResult?> ProbeAsync(
        string? accProjectId,
        string expectedEmail,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AccHumanMembershipProbeResult?>(null);
}
