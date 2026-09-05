namespace SiNet.Application.Identity;

/// <summary>
/// Target project / session context for an identity-gated operation.
/// Mapping SiNet ProjectId → AccProjectId may be resolved inside the identity/ACC layer.
/// </summary>
public sealed record IdentityOperationContext(
    int? SiProjectId = null,
    string? AccProjectId = null,
    string? AutodeskThreeLeggedEmail = null,
    AutodeskCredentialPurpose AutodeskCredentialPurpose = AutodeskCredentialPurpose.UserContext)
{
    public static IdentityOperationContext ForSiProject(int projectId) =>
        new(SiProjectId: projectId);

    public static IdentityOperationContext ForAccProject(string accProjectId) =>
        new(AccProjectId: accProjectId);

    /// <summary>User-context Autodesk 3-legged write — email must equal SIUser.Email.</summary>
    public static IdentityOperationContext ForUserThreeLegged(string autodeskEmail) =>
        new(
            AutodeskThreeLeggedEmail: autodeskEmail,
            AutodeskCredentialPurpose: AutodeskCredentialPurpose.UserContext);

    public static IdentityOperationContext Empty { get; } = new();
}
