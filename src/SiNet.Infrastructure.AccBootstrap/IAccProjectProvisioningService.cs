namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Service for provisioning per-Place ACC projects and creating the project folder structure.
/// 
/// Ensures that for a given SI project, the corresponding ACC project "SI-{Place}" exists,
/// users are provisioned, the project folder hierarchy is created, and a ProjectAccMapping
/// record is persisted in the database.
/// </summary>
public interface IAccProjectProvisioningService
{
    /// <summary>
    /// Ensures the given project has a valid ACC mapping.
    /// If one already exists and is ready, returns it immediately (DB as source of truth).
    /// Otherwise, orchestrates: find/create ACC project → provision users → create folder structure → save mapping.
    /// </summary>
    /// <param name="projectId">The dbo.Projects.ID to provision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved ACC targets for this project.</returns>
    Task<ProjectAccTargets> EnsureProjectMappingAsync(int projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles the members of an existing ACC project against the current
    /// set of active SI users (<see cref="SiNetSQL.Models.Siuser"/> with
    /// <see cref="SiNetSQL.Models.Siuser.IsActive"/> = <c>true</c> and
    /// <see cref="SiNetSQL.Models.Siuser.AccUserType"/> != <c>NoAccUser</c>).
    /// <para>
    /// Idempotent: existing members are SKIPped, missing ones are ADDed,
    /// and access levels are UPGRADEd as needed. This is the same logic used
    /// during initial project creation, exposed so external triggers (e.g.
    /// user-management save) can keep members in sync without re-provisioning.
    /// </para>
    /// </summary>
    /// <param name="accProjectId">ACC project GUID (without the "b." prefix).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the well-known SiNet custom-attribute definitions (see
    /// <see cref="SiNet.Infrastructure.Sql.Services.Email.Acc.SidecarMetadata.AccAttributeNames"/>)
    /// exist on the given ACC project. Idempotent: missing definitions are
    /// created, existing ones left untouched.
    /// <para>
    /// Failures (403 — user lacks Docs admin; 404 — add-on not enabled;
    /// network errors) are reported through
    /// <see cref="IAccMetadataStatusReporter"/>; this method
    /// never throws for expected ACC API errors, so callers can invoke it as a
    /// best-effort step during project bootstrap.
    /// </para>
    /// </summary>
    /// <param name="accProjectId">ACC project GUID (with or without the "b." prefix).</param>
    /// <param name="accFolderId">ACC target folder URN (Docs custom-attribute definitions are folder-scoped).</param>
    /// <param name="siProjectId">SI project id for diagnostics (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if all definitions are ensured; <c>false</c> otherwise.</returns>
    Task<bool> EnsureCustomAttributeDefinitionsAsync(
        string accProjectId,
        string accFolderId,
        int? siProjectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles members, ACC industry roles, and root-folder permissions across
    /// ALL projects that have an existing <c>ProjectAccMapping</c>.
    /// <para>
    /// Idempotent: SKIPs correctly-configured users, ADDs missing ones, UPGRADEs
    /// drifted access levels and roles, and applies folder permissions per the
    /// current <see cref="SiNetSQL.Models.AccUserType"/> policy
    /// (Engineer = Docs edit; Administrator = full control).
    /// </para>
    /// <para>
    /// Each project is processed in isolation: a failure on one project is logged
    /// and does not stop processing of the remaining projects.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary message describing how many projects succeeded / failed.</returns>
    Task<string> ReconcileAllProjectsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Diagnostic: creates a throwaway ACC project, assigns the signed-in user as Project Admin,
    /// and grants the Engineer industry role <c>Edit</c> permissions on the project's root folder.
    /// Skips ALL other provisioning steps (no member adds, no folder tree creation, no DB mapping,
    /// no attribute definitions). Used only to isolate whether the role-based folder-permission
    /// grant works on a freshly created project where the signed-in admin still holds CONTROL.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A human-readable summary line describing the project name, id, and grant outcome.</returns>
    Task<string> ProbeFolderPermissionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Same as <see cref="ProbeFolderPermissionsAsync(CancellationToken)"/> but creates the
    /// throwaway project FROM an existing ACC project template (resolved by exact name in the
    /// account). Used to test the hypothesis that template-derived projects inherit folder
    /// ACLs that grant the integration caller CONTROL on the root folder.
    /// </summary>
    /// <param name="templateName">Exact name of the ACC project template (classification == "template").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> ProbeFolderPermissionsFromTemplateAsync(string templateName, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all ACC project TEMPLATES in the default account, returning <c>(id, name)</c>
    /// pairs sorted by name. Intended for admin UI pickers (e.g. "select default template").
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<(string Id, string Name)>> ListAvailableTemplatesAsync(CancellationToken cancellationToken);
}
