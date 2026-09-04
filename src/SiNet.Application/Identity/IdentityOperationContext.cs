namespace SiNet.Application.Identity;

/// <summary>
/// Target project / session context for an identity-gated operation.
/// Mapping SiNet ProjectId → AccProjectId may be resolved inside the identity/ACC layer.
/// </summary>
public sealed record IdentityOperationContext(
    int? SiProjectId = null,
    string? AccProjectId = null,
    string? AutodeskThreeLeggedEmail = null)
{
    public static IdentityOperationContext ForSiProject(int projectId) =>
        new(SiProjectId: projectId);

    public static IdentityOperationContext ForAccProject(string accProjectId) =>
        new(AccProjectId: accProjectId);

    public static IdentityOperationContext Empty { get; } = new();
}
