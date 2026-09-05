using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Autodesk;
using SiNetSQL.Data;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
using SiNetSQL.Models;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Provisions per-Place ACC projects ("SI-{Place}") with folder structure, user provisioning, and permissions.
/// 
/// FLOW:
/// 1. Check if ProjectAccMapping already exists (DB as source of truth)
/// 2. Load project + Place from DB
/// 3. Resolve AccHub (DB first, API fallback)
/// 4. Build ACC project name: "SI-" + Place.Title
/// 5. Find or create ACC project via API
/// 6. Wait for project activation + assign admin + probe Docs
/// 7. Provision users (SIUser table → ACC project members)
/// 8. Build project folder structure: [parent folder] → project folder
/// 9. Save ProjectAccMapping to DB
/// </summary>
public class AccProjectProvisioningService(
    ITokenProvider tokenProvider,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    IAccMetadataStatusReporter? metadataReporter = null,
    ISystemSettingsQueryService? settingsService = null) : IAccProjectProvisioningService
{
    private readonly ITokenProvider _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

    private readonly IAccMetadataStatusReporter? _metadataReporter = metadataReporter;

    /// <summary>
    /// Optional centralized settings service. When provided, used to look up
    /// <c>Acc.AccProjectTemplateName</c> so newly-created per-Place ACC projects
    /// inherit folder ACLs from a shared template.
    /// </summary>
    private readonly ISystemSettingsQueryService? _settingsService = settingsService;

    private const string AccProjectPrefix = "SI-";
    private const string AccessLevelMember = "member";
    private const string AccessLevelAdmin = "administrator";

    /// <summary>
    /// Per-process cache of ACC project IDs whose SiNet custom-attribute
    /// definitions we have already (best-effort) ensured this run. Prevents
    /// hammering the Docs API on every cached-mapping cache hit.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _attributeDefsEnsured = new();

    /// <inheritdoc/>
    public async Task<ProjectAccTargets> EnsureProjectMappingAsync(int projectId, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ═══════════════════════════════════════════════");
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] EnsureProjectMappingAsync START ProjectId={projectId}");

        await EnsureAccServiceAdminIdentityAllowsMutationAsync(correlationId, cancellationToken).ConfigureAwait(false);

        // Step 1: Check existing mapping (DB as source of truth)
        await using (var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var existing = await db.ProjectAccMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId, cancellationToken);

            if (existing != null
                && !string.IsNullOrEmpty(existing.AccProjectId)
                && !string.IsNullOrEmpty(existing.AccTargetFolderId)
                && existing.DocsStatus == DocsStatus.Ready)
            {
                AccBootstrapLog.Info($"[AccProvision:{correlationId}] Found existing valid mapping. AccProjectId={existing.AccProjectId}, TargetFolder={existing.AccTargetFolderId}");

                // Validate the cached ACC project is still ACTIVE before trusting the mapping.
                // If the project was archived/suspended/deleted in ACC (e.g. user renamed and
                // archived it), all subsequent Docs API calls would fail with
                // 403 BIM360DM_ERROR "Project is not active". In that case we must discard the
                // stale mapping and re-provision (Step 5 will look up by name and create new).
                var stillActive = await IsAccProjectActiveAsync(correlationId, existing.AccProjectId!, cancellationToken);
                if (!stillActive)
                {
                    AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Cached mapping points to an inactive/missing ACC project ({existing.AccProjectId}). Discarding mapping and re-provisioning.");
                }
                else
                {
                    // Best-effort: ensure SiNet custom-attribute definitions exist
                    // even on the cached-mapping path. This is a no-op once per
                    // process per AccProjectId thanks to _attributeDefsEnsured.
                    // Required because mappings created before the metadata feature
                    // existed never had their definitions registered.
                    if (!_attributeDefsEnsured.ContainsKey(existing.AccProjectId!))
                    {
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ★ ATTR-DEFS scheduling background ensure for AccProjectId={existing.AccProjectId}");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                AccBootstrapLog.Info($"[AccProvision:{correlationId}] ★ ATTR-DEFS background START AccProjectId={existing.AccProjectId}");
                                var ok = await EnsureCustomAttributeDefinitionsAsync(
                                    existing.AccProjectId!, existing.AccTargetFolderId!, projectId, CancellationToken.None)
                                    .ConfigureAwait(false);
                                AccBootstrapLog.Info($"[AccProvision:{correlationId}] ★ ATTR-DEFS background END success={ok} AccProjectId={existing.AccProjectId}");
                                if (ok)
                                    _attributeDefsEnsured.TryAdd(existing.AccProjectId!, 0);
                            }
                            catch (Exception ex)
                            {
                                AccBootstrapLog.Warn($"[AccProvision:{correlationId}] ★ ATTR-DEFS background FAILED AccProjectId={existing.AccProjectId}: {ex}");
                            }
                        }, CancellationToken.None);
                    }
                    else
                    {
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ★ ATTR-DEFS already ensured this process for AccProjectId={existing.AccProjectId} — skipping");
                    }

                    return ToTargets(existing);
                }
            }

            if (existing != null)
            {
                AccBootstrapLog.Info($"[AccProvision:{correlationId}] Existing mapping incomplete (DocsStatus={existing.DocsStatus}). Re-provisioning...");
            }
        }

        // Step 2: Load project with Place and parent project
        Project project;
        await using (var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            project = await db.Projects
                .AsNoTracking()
                .Include(p => p.Place)
                .Include(p => p.OnerProject)
                    .ThenInclude(op => op!.Place)
                .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new InvalidOperationException($"Project with ID {projectId} not found.");

            if (project.Place == null || string.IsNullOrWhiteSpace(project.Place.Title))
            {
                throw new InvalidOperationException($"Project {projectId} has no Place assigned. Cannot determine ACC project.");
            }
        }

        var placeName = project.Place!.Title!.Trim();
        var accProjectName = AccProjectPrefix + placeName;

        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Place='{placeName}', AccProjectName='{accProjectName}'");
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Project=({project.Number}){project.Title}");
        if (project.OnerProjectId.HasValue)
        {
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] ParentProject=({project.OnerProject?.Number}){project.OnerProject?.Title}");
        }

        var bim360 = new Bim360Service(_tokenProvider);
        Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
        Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
        Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

        // Step 4: Resolve AccHub
        var accHub = await ResolveAccHubAsync(correlationId, bim360, cancellationToken);
        var accountId = accHub.HubId.StartsWith("b.") ? accHub.HubId[2..] : accHub.HubId;

        // Step 5: Find or create ACC project
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Searching for ACC project '{accProjectName}'...");
        var accProjectId = await bim360.GetAccNativeProjectByNameAsync(accountId, accProjectName, cancellationToken);
        var projectWasCreated = false;
        var createdFromTemplate = false;

        if (string.IsNullOrEmpty(accProjectId))
        {
            // Resolve template (if configured). When a template is used, the new project
            // inherits industry-role folder ACLs (e.g. Engineer = Edit, Administrator = Manage)
            // and we can SKIP the explicit SetFolderPermissions API call entirely — which
            // is normally rejected with 403 because the integration caller does not hold
            // folder CONTROL on a freshly-created project's root folder.
            string? templateProjectId = null;
            if (_settingsService != null)
            {
                try
                {
                    var settings = await _settingsService.GetSystemSettingsAsync(cancellationToken);
                    var templateName = settings.Acc.AccProjectTemplateName;
                    if (!string.IsNullOrWhiteSpace(templateName))
                    {
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Resolving ACC template by name='{templateName}'...");
                        templateProjectId = await bim360.GetAccNativeTemplateByNameAsync(
                            accountId, templateName.Trim(), cancellationToken);
                        if (string.IsNullOrEmpty(templateProjectId))
                            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Template '{templateName}' not found in account — falling back to plain project creation.");
                        else
                            AccBootstrapLog.Info($"[AccProvision:{correlationId}] TemplateProjectId={templateProjectId}");
                    }
                    else
                    {
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] No ACC template configured (Acc.AccProjectTemplateName empty) — creating project without template.");
                    }
                }
                catch (Exception ex)
                {
                    AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Failed to resolve template name: {ex.Message} — falling back to plain project creation.");
                }
            }

            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Project not found. Creating '{accProjectName}'{(string.IsNullOrEmpty(templateProjectId) ? "" : $" from template {templateProjectId}")}...");
            accProjectId = await bim360.CreateAccNativeProjectAsync(
                accHub.HubId, accountId, accProjectName, cancellationToken, templateProjectId);
            projectWasCreated = true;
            createdFromTemplate = !string.IsNullOrEmpty(templateProjectId);

            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Project created. AccProjectId={accProjectId} fromTemplate={createdFromTemplate}");

            // Wait for project to become active
            var isActive = await bim360.WaitForAccProjectActiveAsync(accProjectId, ct: cancellationToken);
            if (!isActive)
            {
                AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Project may not be fully active. Continuing...");
            }

            // Assign admin to enable Docs
            var adminEmail = await ResolveAdminEmailAsync(correlationId, cancellationToken);
            if (!string.IsNullOrEmpty(adminEmail))
            {
                AccBootstrapLog.Info($"[AccProvision:{correlationId}] Assigning admin: {adminEmail}");
                await bim360.AssignProjectAdminAsync(accProjectId, adminEmail, cancellationToken);
            }
        }
        else
        {
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Found existing project. AccProjectId={accProjectId}");
        }

        // Step 6: Probe Docs readiness / get root folder ID
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Probing Docs for root folder...");
        var rootFolderId = await WaitForDocsReadyAsync(correlationId, bim360, accHub.HubId, accProjectId, cancellationToken);

        // Step 7: Provision users (if project was just created)
        if (projectWasCreated)
        {
            await ProvisionUsersToProjectAsync(correlationId, bim360, accHub.HubId, accProjectId, rootFolderId, createdFromTemplate, cancellationToken);
        }

        // Step 8: Build project folder structure
        var (targetFolderId, targetFolderPath) = await BuildProjectFolderStructureAsync(
            correlationId, bim360, accProjectId, rootFolderId, project, cancellationToken);

        // Step 9: Save ProjectAccMapping
        ProjectAccMapping mapping;
        await using (var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var now = DateTime.UtcNow;
            mapping = await db.ProjectAccMappings
                .FirstOrDefaultAsync(m => m.ProjectId == projectId, cancellationToken)
                ?? new ProjectAccMapping { ProjectId = projectId, CreatedAtUtc = now };

            mapping.AccHubId = accHub.Id;
            mapping.AccProjectId = accProjectId;
            mapping.AccProjectName = accProjectName;
            mapping.AccTargetFolderId = targetFolderId;
            mapping.AccTargetFolderPath = targetFolderPath;
            mapping.AccPlatform = AccPlatform.AccNative;
            mapping.DocsStatus = DocsStatus.Ready;
            mapping.DocsLastCheckedUtc = now;
            mapping.DocsLastError = null;
            mapping.LastVerifiedUtc = now;
            mapping.UpdatedAtUtc = now;
            mapping.Notes = projectWasCreated
                ? $"Auto-provisioned by AccProjectProvisioningService"
                : $"Mapped to existing ACC project";

            if (mapping.Id == 0)
            {
                db.ProjectAccMappings.Add(mapping);
            }
            else
            {
                db.ProjectAccMappings.Update(mapping);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ═══════════════════════════════════════════════");
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] SUCCESS AccProjectId={accProjectId} TargetFolder={targetFolderId}");
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ═══════════════════════════════════════════════");

        // Step 10: Best-effort — ensure SiNet custom-attribute definitions exist.
        // Failures (403 / missing Docs add-on) are reported via IAccMetadataStatusReporter
        // and do NOT fail provisioning: file storage still works without the metadata.
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ★ ATTR-DEFS fresh-provision ensure START AccProjectId={accProjectId}");
        var attrOk = await EnsureCustomAttributeDefinitionsAsync(accProjectId, targetFolderId, projectId, cancellationToken);
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ★ ATTR-DEFS fresh-provision ensure END success={attrOk} AccProjectId={accProjectId}");
        if (attrOk)
            _attributeDefsEnsured.TryAdd(accProjectId, 0);

        return ToTargets(mapping);
    }

    /// <inheritdoc/>
    public Task<string> ProbeFolderPermissionsAsync(CancellationToken cancellationToken)
        => ProbeFolderPermissionsCoreAsync(templateName: null, cancellationToken);

    /// <inheritdoc/>
    public Task<string> ProbeFolderPermissionsFromTemplateAsync(string templateName, CancellationToken cancellationToken)
        => ProbeFolderPermissionsCoreAsync(templateName, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string Id, string Name)>> ListAvailableTemplatesAsync(CancellationToken cancellationToken)
    {
        var bim360 = new Bim360Service(_tokenProvider);
        Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
        Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
        Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var accHub = await ResolveAccHubAsync(correlationId, bim360, cancellationToken);
        var accountId = accHub.HubId.StartsWith("b.") ? accHub.HubId[2..] : accHub.HubId;
        return await bim360.ListAccNativeTemplatesAsync(accountId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AccProjectMemberInfo>> ListProjectMembersAsync(
        string accProjectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            throw new ArgumentException("accProjectId is required.", nameof(accProjectId));

        await EnsureAccServiceAdminIdentityAllowsMutationAsync("list-members", cancellationToken).ConfigureAwait(false);

        var bim360 = new Bim360Service(_tokenProvider);
        Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
        Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
        Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

        var users = await bim360.GetProjectUsersAsync(accProjectId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return users
            .Where(u => !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => new AccProjectMemberInfo(
                Email: u.Email.Trim(),
                Name: string.IsNullOrWhiteSpace(u.Name) ? null : u.Name.Trim(),
                AccessLevel: ResolveDocsAccess(u),
                Status: string.IsNullOrWhiteSpace(u.Status) ? null : u.Status.Trim()))
            .ToList();
    }

    private static string? ResolveDocsAccess(ProjectUserInfo user)
    {
        if (user.ProductAccess.TryGetValue("docs", out var docs) && !string.IsNullOrWhiteSpace(docs))
            return docs.Trim();
        return string.IsNullOrWhiteSpace(user.AccessLevel) ? null : user.AccessLevel.Trim();
    }

    private async Task<string> ProbeFolderPermissionsCoreAsync(string? templateName, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var nameSuffix = string.IsNullOrEmpty(templateName) ? "PermProbe" : "TplProbe";
        var probeName = $"SI-{nameSuffix}-{DateTime.Now:yyyyMMdd-HHmmss}";
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] ═══════════════════════════════════════════════");
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] START — minimal folder-perms probe. Project name='{probeName}' Template='{templateName ?? "(none)"}'");
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] Steps: {(string.IsNullOrEmpty(templateName) ? "create project" : "resolve template → create project FROM template")} → wait active → assign admin → docs probe → grant Engineer role on root folder. NO other provisioning.");

        var bim360 = new Bim360Service(_tokenProvider);
        Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
        Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
        Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

        // 2. Resolve hub.
        var accHub = await ResolveAccHubAsync(correlationId, bim360, cancellationToken);
        var accountId = accHub.HubId.StartsWith("b.") ? accHub.HubId[2..] : accHub.HubId;
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] Hub={accHub.HubId} Account={accountId}");

        // 2b. Resolve template id (if requested).
        string? templateProjectId = null;
        if (!string.IsNullOrEmpty(templateName))
        {
            AccBootstrapLog.Info($"[PermProbe:{correlationId}] Resolving template by name='{templateName}'...");
            templateProjectId = await bim360.GetAccNativeTemplateByNameAsync(accountId, templateName, cancellationToken);
            if (string.IsNullOrEmpty(templateProjectId))
                return $"FAILED: template '{templateName}' not found in account {accountId}.";
            AccBootstrapLog.Info($"[PermProbe:{correlationId}] TemplateProjectId={templateProjectId}");
        }

        // 3. Create project (optionally from template).
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] Creating project '{probeName}'...");
        var accProjectId = await bim360.CreateAccNativeProjectAsync(accHub.HubId, accountId, probeName, cancellationToken, templateProjectId);
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] Project created. AccProjectId={accProjectId}");

        // 4. Wait for active.
        var isActive = await bim360.WaitForAccProjectActiveAsync(accProjectId, ct: cancellationToken);
        if (!isActive)
            AccBootstrapLog.Warn($"[PermProbe:{correlationId}] Project did not reach active state in time — continuing anyway.");

        // 5. Assign Project Admin (current user) — required so docs gets provisioned.
        var adminEmail = await ResolveAdminEmailAsync(correlationId, cancellationToken);
        if (string.IsNullOrEmpty(adminEmail))
            return $"FAILED: could not resolve admin email. AccProjectId={accProjectId}";
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] Assigning admin: {adminEmail}");
        await bim360.AssignProjectAdminAsync(accProjectId, adminEmail, cancellationToken);

        // 6. Probe Docs to retrieve the root folder id.
        var rootFolderId = await WaitForDocsReadyAsync(correlationId, bim360, accHub.HubId, accProjectId, cancellationToken);
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] RootFolderId={rootFolderId}");

        // 7. Resolve Engineer industry role.
        string? engineerRoleId = null;
        try
        {
            var roles = await bim360.ListProjectIndustryRolesAsync(accHub.HubId, accProjectId, cancellationToken);
            engineerRoleId = roles.FirstOrDefault(r => r.Name.Equals("Engineer", StringComparison.OrdinalIgnoreCase))?.Id;
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Warn($"[PermProbe:{correlationId}] List industry roles failed: {ex.Message}");
        }

        if (string.IsNullOrEmpty(engineerRoleId))
            return $"FAILED: Engineer industry role not found on project. ProjectName='{probeName}' AccProjectId={accProjectId}";
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] EngineerRoleId={engineerRoleId}");

        // 8. THE actual test: grant Engineer role Edit permissions on the root folder.
        var roleGrants = new List<FolderPermissionGrant>
        {
            new FolderPermissionGrant
            {
                SubjectType = "ROLE",
                SubjectId = engineerRoleId!,
                Actions = FolderPermissionPresets.EngineerEdit.ToList()
            }
        };

        bool grantOk;
        string grantError = "";
        try
        {
            grantOk = await bim360.SetFolderPermissionsAsync(accProjectId, rootFolderId, roleGrants, cancellationToken);
            AccBootstrapLog.Info($"[PermProbe:{correlationId}] SetFolderPermissionsAsync result: ok={grantOk}");
        }
        catch (Exception ex)
        {
            grantOk = false;
            grantError = ex.Message;
            AccBootstrapLog.Error(ex, $"[PermProbe:{correlationId}] SetFolderPermissionsAsync threw");
        }

        var templateInfo = string.IsNullOrEmpty(templateName) ? "" : $" Template='{templateName}' TemplateProjectId={templateProjectId}";
        var summary = grantOk
            ? $"SUCCESS — folder grant on Engineer role worked. ProjectName='{probeName}' AccProjectId={accProjectId} RootFolder={rootFolderId}{templateInfo}"
            : $"FAILED — folder grant did NOT work. ProjectName='{probeName}' AccProjectId={accProjectId} RootFolder={rootFolderId}{templateInfo} Error='{grantError}'";

        AccBootstrapLog.Info($"[PermProbe:{correlationId}] {summary}");
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] NOTE: probe project is left in ACC for inspection. Archive/delete manually.");
        AccBootstrapLog.Info($"[PermProbe:{correlationId}] ═══════════════════════════════════════════════");
        return summary;
    }

    /// <summary>
    /// Verifies that an ACC project is still <c>active</c> in the Admin API. Used to invalidate
    /// stale <see cref="ProjectAccMapping"/> rows whose <c>AccProjectId</c> points at a project
    /// that has since been archived, suspended, or deleted (e.g. after a manual rename + archive
    /// in the ACC UI). Returns <c>false</c> on any non-active status, on HTTP 404, or on any
    /// transient/auth failure (best-effort: prefer treating an inactive cache as untrustworthy
    /// rather than failing the whole flow).
    /// </summary>
    private async Task<bool> IsAccProjectActiveAsync(string correlationId, string accProjectId, CancellationToken ct)
    {
        try
        {
            var bim360 = new Bim360Service(_tokenProvider);
            Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
            Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
            Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

            var status = await bim360.GetAccNativeProjectStatusAsync(accProjectId, ct);
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] IsAccProjectActiveAsync: AccProjectId={accProjectId} status='{status ?? "<null>"}'");
            return string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Network/auth glitch — don't punish the user by tearing down a valid mapping.
            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] IsAccProjectActiveAsync transient failure for {accProjectId}: {ex.Message} — assuming mapping is valid.");
            return true;
        }
    }

    /// <summary>
    /// Resolves the AccHub from DB (first) or API (fallback). Creates DB record if discovered from API.
    /// </summary>
    private async Task<AccHub> ResolveAccHubAsync(
        string correlationId, Bim360Service bim360, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var hub = await db.AccHubs.FirstOrDefaultAsync(h => h.IsDefault, ct)
               ?? await db.AccHubs.FirstOrDefaultAsync(ct);

        if (hub != null)
        {
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Using AccHub from DB: {hub.HubId} ({hub.DisplayName})");
            return hub;
        }

        // Discover from API
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] No AccHub in DB. Discovering from API...");
        var hubs = await bim360.ListHubsAsync(ct);

        if (hubs.Count == 0)
            throw new InvalidOperationException("No hubs found in Autodesk account.");
        if (hubs.Count > 1)
            throw new InvalidOperationException($"Multiple hubs ({hubs.Count}) found. Only single-hub is supported.");

        var apiHub = hubs[0];
        var now = DateTime.UtcNow;
        hub = new AccHub
        {
            HubId = apiHub.Id,
            DisplayName = apiHub.Name,
            IsDefault = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.AccHubs.Add(hub);
        await db.SaveChangesAsync(ct);
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Created AccHub: {hub.HubId} ({hub.DisplayName})");
        return hub;
    }

    /// <summary>
    /// Resolves the admin email for project admin assignment.
    /// Priority:
    /// <list type="number">
    ///   <item>Centralized setting <c>Acc.AccBootstrapAdminEmail</c>
    ///         (the dedicated service account — preferred).</item>
    ///   <item>Legacy: SIUser with <c>AccUserType=Admin</c>.</item>
    ///   <item>Legacy: current domain user's email from SIUser.</item>
    /// </list>
    /// </summary>
    private async Task<string?> ResolveAdminEmailAsync(string correlationId, CancellationToken ct)
    {
        // Priority 1: dedicated bootstrap admin from system settings (not a SIUser).
        if (_settingsService != null)
        {
            try
            {
                var settings = await _settingsService.GetSystemSettingsAsync(ct);
                var configured = settings.Acc.AccBootstrapAdminEmail;
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    var trimmed = configured.Trim();
                    AccBootstrapLog.Info($"[AccProvision:{correlationId}] Admin resolved from Acc.AccBootstrapAdminEmail: {trimmed}");
                    return trimmed;
                }
            }
            catch (Exception ex)
            {
                AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Failed to read Acc.AccBootstrapAdminEmail: {ex.Message}");
            }
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Priority 2: Explicit Admin user (legacy)
        var admin = await db.Siusers
            .Where(u => u.IsActive && u.AccUserType == AccUserType.Admin && !string.IsNullOrEmpty(u.Email))
            .Select(u => u.Email!.Trim())
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(admin))
        {
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Admin resolved from AccUserType.Admin: {admin}");
            return admin;
        }

        // Priority 3: Current domain user's email from SIUser table (legacy fallback)
        var currentLogin = Environment.UserName;
        admin = await db.Siusers
            .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Email)
                        && u.LoginName != null && u.LoginName.Contains(currentLogin))
            .Select(u => u.Email!.Trim())
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(admin))
        {
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Admin resolved from current domain user ({currentLogin}): {admin}");
            return admin;
        }

        AccBootstrapLog.Warn($"[AccProvision:{correlationId}] No admin email configured (Acc.AccBootstrapAdminEmail empty, no AccUserType.Admin SIUser, no domain user match for '{currentLogin}').");
        return null;
    }

    /// <summary>
    /// Polls Docs readiness until root folder ID is available.
    /// </summary>
    private async Task<string> WaitForDocsReadyAsync(
        string correlationId, Bim360Service bim360, string hubId,
        string accProjectId, CancellationToken ct)
    {
        const int maxAttempts = 30;
        const int delaySeconds = 5;

        for (int i = 1; i <= maxAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();

            var probe = await bim360.ProbeDocsAsync(hubId, accProjectId, ct);
            if (probe.IsReady && !string.IsNullOrEmpty(probe.RootFolderId))
            {
                AccBootstrapLog.Info($"[AccProvision:{correlationId}] Docs ready. RootFolderId={probe.RootFolderId}");
                return probe.RootFolderId;
            }

            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Docs not ready (attempt {i}/{maxAttempts}). Waiting {delaySeconds}s...");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
        }

        throw new InvalidOperationException(
            $"Docs not ready for ACC project {accProjectId} after {maxAttempts * delaySeconds}s. " +
            "The project may not be fully provisioned.");
    }

    /// <inheritdoc/>
    public async Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            throw new ArgumentException("accProjectId is required.", nameof(accProjectId));

        var correlationId = Guid.NewGuid().ToString("N")[..8];
        AccBootstrapLog.Info($"[AccReconcile:{correlationId}] ReconcileProjectMembersAsync START AccProjectId={accProjectId}");

        await EnsureAccServiceAdminIdentityAllowsMutationAsync(correlationId, cancellationToken).ConfigureAwait(false);

        var bim360 = new Bim360Service(_tokenProvider);
        Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
        Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
        Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

        // Resolve the project's HubId so we can probe Docs for the root folder
        // (root-folder permissions are best-effort; if probing fails we still
        // reconcile members + roles).
        string? rootFolderId = null;
        string? hubId = null;
        try
        {
            await using (var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                hubId = await db.ProjectAccMappings
                    .AsNoTracking()
                    .Where(m => m.AccProjectId == accProjectId)
                    .Select(m => m.AccHub != null ? m.AccHub.HubId : null)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (!string.IsNullOrEmpty(hubId))
            {
                var probe = await bim360.ProbeDocsAsync(hubId!, accProjectId, cancellationToken);
                if (probe.IsReady && !string.IsNullOrEmpty(probe.RootFolderId))
                {
                    rootFolderId = probe.RootFolderId;
                    AccBootstrapLog.Info($"[AccReconcile:{correlationId}] Root folder probed: {rootFolderId}");
                }
            }
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Warn($"[AccReconcile:{correlationId}] Could not resolve root folder: {ex.Message}");
        }

        await ProvisionUsersToProjectAsync(correlationId, bim360, hubId, accProjectId, rootFolderId, fromTemplate: false, cancellationToken);

        AccBootstrapLog.Info($"[AccReconcile:{correlationId}] ReconcileProjectMembersAsync DONE AccProjectId={accProjectId}");
    }

    /// <inheritdoc/>
    public async Task<string> ReconcileAllProjectsAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        AccBootstrapLog.Info($"[AccReconcileAll:{correlationId}] ═══════════════════════════════════════════════");
        AccBootstrapLog.Info($"[AccReconcileAll:{correlationId}] ReconcileAllProjectsAsync START");

        await EnsureAccServiceAdminIdentityAllowsMutationAsync(correlationId, cancellationToken).ConfigureAwait(false);

        // Load AccProjectId + best-available display name (AccProjectName, falling back
        // to the SI Project.Name) so the final summary can list the actual projects that
        // were processed (the user has more than 3 ACC mappings).
        List<(string AccProjectId, string DisplayName)> accProjects;
        await using (var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var rows = await db.ProjectAccMappings
                .AsNoTracking()
                .Where(m => m.AccProjectId != null && m.AccProjectId != "")
                .Select(m => new
                {
                    m.AccProjectId,
                    m.AccProjectName,
                    ProjectNameAndNumber = m.Project != null ? m.Project.NameAndNumber : null,
                    ProjectTitle = m.Project != null ? m.Project.Title : null
                })
                .ToListAsync(cancellationToken);

            accProjects = rows
                .GroupBy(x => x.AccProjectId!)
                .Select(g =>
                {
                    var first = g.First();
                    var display =
                        !string.IsNullOrWhiteSpace(first.AccProjectName) ? first.AccProjectName! :
                        !string.IsNullOrWhiteSpace(first.ProjectNameAndNumber) ? first.ProjectNameAndNumber! :
                        !string.IsNullOrWhiteSpace(first.ProjectTitle) ? first.ProjectTitle! :
                        g.Key;
                    return (AccProjectId: g.Key, DisplayName: display);
                })
                .ToList();
        }

        AccBootstrapLog.Info($"[AccReconcileAll:{correlationId}] Found {accProjects.Count} ACC project mapping(s).");

        int ok = 0, failed = 0;
        var succeeded = new List<string>(accProjects.Count);
        var failures = new List<string>();
        foreach (var (accProjectId, displayName) in accProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ReconcileProjectMembersAsync(accProjectId, cancellationToken);
                ok++;
                succeeded.Add(displayName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"{displayName} ({ex.Message})");
                AccBootstrapLog.Error(ex, $"[AccReconcileAll:{correlationId}] FAILED AccProjectId={accProjectId} ({displayName})");
            }
        }

        var summary = $"Total={accProjects.Count}, Succeeded={ok}, Failed={failed}";
        if (succeeded.Count > 0)
            summary += Environment.NewLine + "Succeeded projects:" + Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", succeeded);
        if (failures.Count > 0)
            summary += Environment.NewLine + "Failed projects:" + Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", failures);

        AccBootstrapLog.Info($"[AccReconcileAll:{correlationId}] ReconcileAllProjectsAsync DONE. Total={accProjects.Count}, Succeeded={ok}, Failed={failed}");
        if (succeeded.Count > 0)
            AccBootstrapLog.Info($"[AccReconcileAll:{correlationId}] Succeeded: {string.Join(", ", succeeded)}");
        if (failures.Count > 0)
            AccBootstrapLog.Info($"[AccReconcileAll:{correlationId}] Failed: {string.Join(", ", failures)}");
        return summary;
    }

    /// <inheritdoc/>
    public async Task<bool> EnsureCustomAttributeDefinitionsAsync(
        string accProjectId, string accFolderId, int? siProjectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            return false;
        if (string.IsNullOrWhiteSpace(accFolderId))
        {
            _metadataReporter?.Report(new AccMetadataIssue(
                DateTime.UtcNow, AccMetadataOperation.DefineAttribute,
                siProjectId, accProjectId, null, null, null,
                "accFolderId is required (Docs custom-attribute definitions are folder-scoped)."));
            return false;
        }

        var correlationId = Guid.NewGuid().ToString("N")[..8];
        AccBootstrapLog.Info($"[AccAttrDef:{correlationId}] EnsureCustomAttributeDefinitionsAsync AccProjectId={accProjectId}");

        await EnsureAccServiceAdminIdentityAllowsMutationAsync(correlationId, cancellationToken).ConfigureAwait(false);

        var bim360 = new Bim360Service(_tokenProvider);
        Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
        Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
        Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

        // Map our well-known names to Docs custom-attribute definitions.
        var definitions = new[]
        {
            new CustomAttributeDefinition(
                Name: SidecarMetadata.AccAttributeNames.LastFileName,
                DisplayName: "Si Last File Name",
                Type: CustomAttributeType.Text,
                Description: "Filename at the time of the last upload through SiNet."),
            new CustomAttributeDefinition(
                Name: SidecarMetadata.AccAttributeNames.LastSizeBytes,
                DisplayName: "Si Last Size (bytes)",
                Type: CustomAttributeType.Text,
                Description: "Size in bytes at the time of the last upload."),
            new CustomAttributeDefinition(
                Name: SidecarMetadata.AccAttributeNames.LastSavedUtc,
                DisplayName: "Si Last Saved (UTC)",
                Type: CustomAttributeType.Text,
                Description: "LastWriteTime (UTC) at the time of the last upload (ISO-8601)."),
            new CustomAttributeDefinition(
                Name: SidecarMetadata.AccAttributeNames.SourceFileNames,
                DisplayName: "Si Source File Names",
                Type: CustomAttributeType.Text,
                Description: "Newline-separated history of original filenames (external files)."),
            new CustomAttributeDefinition(
                Name: SidecarMetadata.AccAttributeNames.Notes,
                DisplayName: "Si Notes",
                Type: CustomAttributeType.Text,
                Description: "Free-form notes attached by the user."),
        };

        AccMetadataResult result;
        try
        {
            result = await bim360.EnsureCustomAttributeDefinitionsAsync(
                accProjectId, accFolderId, definitions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metadataReporter?.Report(new AccMetadataIssue(
                DateTime.UtcNow, AccMetadataOperation.DefineAttribute,
                siProjectId, accProjectId, null, null, null,
                $"Unexpected: {ex.Message}"));
            return false;
        }

        if (!result.Success)
        {
            _metadataReporter?.Report(new AccMetadataIssue(
                DateTime.UtcNow, AccMetadataOperation.DefineAttribute,
                siProjectId, accProjectId, null, null,
                result.HttpStatus, result.ErrorMessage ?? "Unknown error."));
            return false;
        }

        AccBootstrapLog.Info($"[AccAttrDef:{correlationId}] Definitions ensured.");
        return true;
    }

    /// <summary>
    /// Provisions active SI users into the given ACC project, including
    /// industry-role assignment and (best-effort) root-folder permissions.
    /// <para>
    /// Single-policy: every user with <see cref="AccUserType"/> != <c>NoAccUser</c>
    /// gets docs=member, role=Engineer, folder=Engineer edit. <c>NoAccUser</c> is skipped.
    /// (Administrator role is intentionally NOT assigned — it does not exist on every BIM 360 account,
    /// and SI admins already have full access via their account-level admin permissions.)
    /// </para>
    /// Each step is independent and best-effort; partial failures are logged and do not abort.
    /// </summary>
    private async Task ProvisionUsersToProjectAsync(
        string correlationId, Bim360Service bim360, string? hubId, string accProjectId, string? rootFolderId, bool fromTemplate, CancellationToken ct)
    {
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Provisioning users... hubId={hubId ?? "<none>"} rootFolderId={(string.IsNullOrEmpty(rootFolderId) ? "<none>" : rootFolderId)} fromTemplate={fromTemplate}");

        List<(string Email, AccUserType UserType)> users;
        await using (var db = await _dbContextFactory.CreateDbContextAsync(ct))
        {
            users = await db.Siusers
                .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Email) && u.AccUserType != AccUserType.NoAccUser)
                .Select(u => new { u.Email, u.AccUserType })
                .ToListAsync(ct)
                .ContinueWith(t => t.Result.Select(u => (u.Email!.Trim().ToLowerInvariant(), u.AccUserType)).ToList(), ct);
        }

        if (users.Count == 0)
        {
            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] No active ACC users found in SIUser table.");
            return;
        }

        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Found {users.Count} active ACC user(s) to provision.");

        // The dedicated bootstrap-admin (system-settings AccBootstrapAdminEmail) is a
        // service account whose ACC permissions must NEVER be touched by this routine
        // (no docs=administrator → member downgrade, no role reassignment, no folder-
        // permission tweaks). Resolve it once here so we can skip it everywhere below.
        string? bootstrapAdminEmail = null;
        if (_settingsService != null)
        {
            try
            {
                var settings = await _settingsService.GetSystemSettingsAsync(ct);
                var configured = settings.Acc.AccBootstrapAdminEmail;
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    bootstrapAdminEmail = configured.Trim().ToLowerInvariant();
                    AccBootstrapLog.Info($"[AccProvision:{correlationId}] Bootstrap-admin lockout email='{bootstrapAdminEmail}' — its ACC permissions will NOT be modified.");
                }
            }
            catch (Exception ex)
            {
                AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Failed to read Acc.AccBootstrapAdminEmail: {ex.Message}");
            }
        }

        // 1. Resolve the Engineer industry-role id — best-effort.
        // The BIM 360 HQ industry-roles endpoint requires the accountId (= hubId without "b." prefix).
        string? engineerRoleId = null;
        if (!string.IsNullOrEmpty(hubId))
        {
            try
            {
                var roles = await bim360.ListProjectIndustryRolesAsync(hubId!, accProjectId, ct);
                engineerRoleId = roles.FirstOrDefault(r => r.Name.Equals("Engineer", StringComparison.OrdinalIgnoreCase))?.Id;

                if (engineerRoleId == null)
                    AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Industry role 'Engineer' not found on project — role assignment will be skipped.");
            }
            catch (Exception ex)
            {
                AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Could not list industry roles: {ex.Message}");
            }
        }
        else
        {
            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] hubId not available — skipping industry-role assignment.");
        }

        // 2. Snapshot existing project users so we can match user IDs for updates / folder perms.
        List<ProjectUserInfo> existingUsers;
        try
        {
            existingUsers = await bim360.GetProjectUsersAsync(accProjectId, ct);
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Could not fetch existing project users: {ex.Message}");
            existingUsers = new List<ProjectUserInfo>();
        }
        var existingByEmail = existingUsers
            .Where(u => !string.IsNullOrEmpty(u.Email))
            .GroupBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 2b. Apply folder permissions on the ROOT folder via the Engineer ROLE BEFORE the
        // members-downgrade loop below. On a freshly created project the signed-in admin still
        // holds folder CONTROL (granted automatically when ProjectAdmin was assigned). The
        // members loop further down will downgrade users (including the creator) from
        // docs=administrator → docs=member, which strips that CONTROL grant. If folder
        // permissions are applied AFTER the loop, the batch-create call returns HTTP 403
        // ("don't have control permission on folder ..."). Running it first avoids that.
        //
        // SKIP when the project was created from an ACC project TEMPLATE: templates carry
        // their own industry-role folder ACLs (e.g. Engineer = Edit, Administrator = Manage)
        // which are copied to the new project automatically. In that case the API call is
        // both unnecessary AND would fail with 403 (the integration caller never holds
        // folder CONTROL on a template-derived project's root either).
        if (fromTemplate)
        {
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Skipping ApplyRootFolderPermissionsAsync — folder ACL inherited from ACC project template.");
        }
        else
        {
            await ApplyRootFolderPermissionsAsync(correlationId, bim360, accProjectId, rootFolderId, engineerRoleId, existingByEmail, ct);
        }

        int added = 0, updated = 0, skipped = 0, failed = 0;
        foreach (var (email, userType) in users)
        {
            ct.ThrowIfCancellationRequested();

            // Bootstrap-admin lockout: never modify the dedicated service account.
            if (bootstrapAdminEmail != null
                && string.Equals(email, bootstrapAdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                AccBootstrapLog.Info($"[AccProvision:{correlationId}] SKIP (bootstrap-admin lockout) {email} — leaving existing permissions untouched.");
                continue;
            }

            // Single policy: everyone (Admin and Engineer alike) gets docs=member,
            // role=Engineer. NoAccUser is filtered out earlier.
            _ = userType; // kept to silence unused-variable hints if any
            var accessLevel = AccessLevelMember;
            string? roleId = engineerRoleId;

            var roleIds = roleId != null ? new[] { roleId } : null;

            try
            {
                string? userId = null;
                if (existingByEmail.TryGetValue(email, out var existing))
                {
                    userId = existing.UserId;
                    // User exists — update access level if drifted; update role.
                    var currentDocs = existing.ProductAccess.TryGetValue("docs", out var d) ? d : "";
                    if (!string.Equals(currentDocs, accessLevel, StringComparison.OrdinalIgnoreCase))
                    {
                        var upd = await bim360.UpdateProjectMemberAccessAsync(accProjectId, userId, accessLevel, ct);
                        if (upd.Success) updated++;
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] UPDATE access {email} {currentDocs}→{accessLevel} : {upd.Action}");
                    }
                    else
                    {
                        skipped++;
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] SKIP access {email} (already {accessLevel})");
                    }

                    if (roleIds != null)
                    {
                        try
                        {
                            var rr = await bim360.UpdateProjectMemberRolesAsync(accProjectId, userId, roleIds, ct);
                            AccBootstrapLog.Info($"[AccProvision:{correlationId}] UPDATE role {email} → {rr.Action}");
                        }
                        catch (Exception exr)
                        {
                            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] role-update failed {email}: {exr.Message}");
                        }
                    }
                }
                else
                {
                    var result = await bim360.AddProjectMemberWithRoleAsync(accProjectId, email, accessLevel, roleIds, ct);
                    if (result.Success)
                    {
                        added++;
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] ADD {email} ({accessLevel}, role={(roleId ?? "<none>")}) → {result.Action}");
                    }
                    else
                    {
                        failed++;
                        AccBootstrapLog.Warn($"[AccProvision:{correlationId}] FAIL ADD {email} → {result.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                failed++;
                AccBootstrapLog.Error(ex, $"[AccProvision:{correlationId}] FAIL {email}");
            }
        }

        AccBootstrapLog.Info($"[AccProvision:{correlationId}] User provisioning done. Total={users.Count}, Added={added}, Updated={updated}, Skipped={skipped}, Failed={failed}");
    }

    /// <summary>
    /// Apply folder permissions on the project root folder via the Engineer industry ROLE.
    /// Per Autodesk's "Update a User's Folder Permissions" tutorial (Option 3), grant permissions
    /// to the role on the folder once; users assigned to that role inherit them automatically.
    ///
    /// IMPORTANT — call ordering: this MUST run BEFORE the members-access downgrade loop in
    /// <see cref="ProvisionUsersToProjectAsync"/>. On a fresh project, the signed-in admin holds
    /// folder CONTROL (granted automatically when ProjectAdmin was assigned). Downgrading them
    /// to docs=member strips that CONTROL grant and the batch-create call then returns HTTP 403.
    ///
    /// For pre-existing projects (e.g. bulk reconcile path) where the caller never had CONTROL,
    /// this method falls back to a temporary docs=administrator elevation + delay + revert.
    /// The revert runs in a finally so it ALWAYS happens.
    /// </summary>
    private async Task ApplyRootFolderPermissionsAsync(
        string correlationId,
        Bim360Service bim360,
        string accProjectId,
        string? rootFolderId,
        string? engineerRoleId,
        Dictionary<string, ProjectUserInfo> existingByEmail,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rootFolderId) || string.IsNullOrEmpty(engineerRoleId))
        {
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Skipping folder permissions (rootFolderId or engineerRoleId missing).");
            return;
        }

        // Identify the current/signed-in user (token owner) via the same resolver used for
        // project-admin assignment.
        string? currentUserEmail = await ResolveAdminEmailAsync(correlationId, ct);
        string? currentUserId = null;
        string? originalAccess = null;
        bool elevated = false;

        // Construction Admin v1 enum is "administrator" (NOT "admin") for elevated docs access.
        const string AdminAccessLevel = "administrator";

        if (!string.IsNullOrEmpty(currentUserEmail) &&
            existingByEmail.TryGetValue(currentUserEmail, out var meInProject))
        {
            currentUserId = meInProject.UserId;
            originalAccess = meInProject.ProductAccess.TryGetValue("docs", out var d) ? d : AccessLevelMember;

            if (string.Equals(originalAccess, AdminAccessLevel, StringComparison.OrdinalIgnoreCase))
            {
                AccBootstrapLog.Info($"[AccProvision:{correlationId}] Current user {currentUserEmail} already docs=administrator; no elevation needed.");
            }
            else
            {
                try
                {
                    var elev = await bim360.UpdateProjectMemberAccessAsync(accProjectId, currentUserId!, AdminAccessLevel, ct);
                    elevated = elev.Success;
                    if (elevated)
                    {
                        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Elevated current user {currentUserEmail} docs={originalAccess}→{AdminAccessLevel} (for folder-perms call): {elev.Action}");
                        // Give the BIM 360 Docs subsystem a moment to propagate the elevated
                        // access level to the folder-permission cache.
                        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { /* swallow */ }
                    }
                    else
                        AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Elevation of {currentUserEmail} did not succeed: {elev.Action} {elev.Message} — proceeding without elevation.");
                }
                catch (Exception ex)
                {
                    AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Could not elevate current user {currentUserEmail}: {ex.Message}");
                }
            }
        }
        else
        {
            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Could not identify current user in project membership — proceeding without elevation (folder-perms call may 403).");
        }

        try
        {
            var roleGrants = new List<FolderPermissionGrant>
            {
                new FolderPermissionGrant
                {
                    SubjectType = "ROLE",
                    SubjectId = engineerRoleId!,
                    Actions = FolderPermissionPresets.EngineerEdit.ToList()
                }
            };
            var ok = await bim360.SetFolderPermissionsAsync(accProjectId, rootFolderId!, roleGrants, ct);
            AccBootstrapLog.Info($"[AccProvision:{correlationId}] Folder permissions on root via Engineer role: ok={ok}");
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Warn($"[AccProvision:{correlationId}] SetFolderPermissionsAsync (role) failed: {ex.Message}");
        }
        finally
        {
            if (elevated && !string.IsNullOrEmpty(currentUserId) && !string.IsNullOrEmpty(originalAccess))
            {
                try
                {
                    // Use CancellationToken.None so the revert runs even if the outer call was cancelled.
                    var rev = await bim360.UpdateProjectMemberAccessAsync(accProjectId, currentUserId!, originalAccess!, CancellationToken.None);
                    AccBootstrapLog.Info($"[AccProvision:{correlationId}] Reverted current user {currentUserEmail} docs=administrator→{originalAccess}: {rev.Action}");
                }
                catch (Exception ex)
                {
                    AccBootstrapLog.Error(ex, $"[AccProvision:{correlationId}] FAILED to revert current user {currentUserEmail} back to docs={originalAccess} — MANUAL CLEANUP MAY BE REQUIRED!");
                }
            }
        }
    }

    /// <summary>
    /// Builds the project folder hierarchy in ACC:
    /// [Parent Project Folder] → [Project Folder]
    /// Uses NameAndNumber.FixDirectoryName() for folder naming (same as local filesystem).
    /// </summary>
    private async Task<(string folderId, string folderPath)> BuildProjectFolderStructureAsync(
        string correlationId, Bim360Service bim360, string accProjectId,
        string rootFolderId, Project project, CancellationToken ct)
    {
        var segments = new List<string>();

        // If parent project exists, add parent folder first
        if (project.OnerProjectId.HasValue && project.OnerProject != null)
        {
            var parentFolderName = BuildProjectFolderName(project.OnerProject);
            if (!string.IsNullOrEmpty(parentFolderName))
            {
                segments.Add(parentFolderName);
                AccBootstrapLog.Info($"[AccProvision:{correlationId}] Parent folder: '{parentFolderName}'");
            }
        }

        // Add project folder
        var projectFolderName = BuildProjectFolderName(project);
        if (string.IsNullOrEmpty(projectFolderName))
        {
            throw new InvalidOperationException($"Cannot build folder name for project {project.Id}. NameAndNumber is empty.");
        }
        segments.Add(projectFolderName);
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Project folder: '{projectFolderName}'");

        // Create folder hierarchy in ACC
        var deepestFolderId = await bim360.EnsureFolderPathAsync(
            accProjectId, rootFolderId, segments, ct);

        var folderPath = "/" + string.Join("/", segments);
        AccBootstrapLog.Info($"[AccProvision:{correlationId}] Folder structure created. Path={folderPath}, FolderId={deepestFolderId}");

        return (deepestFolderId, folderPath);
    }

    /// <summary>
    /// Builds the ACC folder name for a project using the same convention as the local filesystem:
    /// (Number)Title normalized with FixDirectoryName.
    /// </summary>
    private static string BuildProjectFolderName(Project project)
    {
        var nameAndNumber = project.NameAndNumber;
        if (string.IsNullOrWhiteSpace(nameAndNumber))
        {
            // Fallback: build from Number + Title
            var number = project.Number?.ToString("0") ?? "0";
            var title = project.Title ?? "Untitled";
            nameAndNumber = $"({number}){title}";
        }

        return nameAndNumber.FixDirectoryName() ?? nameAndNumber;
    }

    /// <summary>
    /// Fail-closed before ACC Admin mutations when AccService token identity ≠ AccBootstrapAdminEmail.
    /// </summary>
    private async Task EnsureAccServiceAdminIdentityAllowsMutationAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        string expected = SystemSettingsDefaults.AccBootstrapAdminEmail;
        if (_settingsService is not null)
        {
            try
            {
                var settings = await _settingsService.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(settings.Acc.AccBootstrapAdminEmail))
                {
                    expected = settings.Acc.AccBootstrapAdminEmail.Trim();
                }
            }
            catch (Exception ex)
            {
                AccBootstrapLog.Warn(
                    $"[AccProvision:{correlationId}] AccBootstrapAdminEmail read failed: {ex.Message}; using default.");
            }
        }

        var profile = await AccServiceAdminTokenProfile
            .ResolveAsync(_tokenProvider, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (_tokenProvider.TokenStorePurpose != AutodeskTokenStorePurpose.AccServiceAdmin
            || !AccServiceTokenPackageMeta.IsDedicatedAccServiceTokenPath(
                _tokenProvider.ThreeLeggedRefreshTokenStoragePath))
        {
            AccBootstrapLog.Warn(
                $"[AccProvision:{correlationId}] Admin mutation blocked: wrong token store " +
                $"purpose={_tokenProvider.TokenStorePurpose}, path={_tokenProvider.ThreeLeggedRefreshTokenStoragePath}");
            throw new InvalidOperationException(
                "ACC Admin mutation blocked: AccService must use the dedicated AccService Autodesk token store.");
        }

        var check = AccServiceAdminIdentity.Evaluate(
            expected,
            profile.Email,
            profile.TokenAvailable,
            profile.ProfileResolved,
            profile.AutodeskUserId,
            profile.DisplayName);

        AccBootstrapLog.Info(
            $"[AccProvision:{correlationId}] AdminIdentity expected={check.ExpectedAdminEmail}, " +
            $"actual={check.ActualAdminEmail ?? "(unavailable)"}, status={check.Status}");

        if (!AccServiceAdminIdentity.ShouldBlockAdminMutation(check))
        {
            return;
        }

        var message = check.OperatorMessageHe
            ?? AccServiceAdminIdentity.FormatMismatchMessageHe(
                check.ExpectedAdminEmail,
                check.ActualAdminEmail ?? "(unavailable)");

        AccBootstrapLog.Warn($"[AccProvision:{correlationId}] Admin mutation blocked: {check.Status}");
        throw new InvalidOperationException(message);
    }

    private static ProjectAccTargets ToTargets(ProjectAccMapping m) => new()
    {
        AccHubDbId = m.AccHubId,
        HubId = m.AccHub?.HubId ?? string.Empty,
        AccProjectId = m.AccProjectId ?? string.Empty,
        AccProjectName = m.AccProjectName ?? string.Empty,
        AccRootFolderId = string.Empty,
        AccTargetFolderId = m.AccTargetFolderId ?? string.Empty,
        AccTargetFolderPath = m.AccTargetFolderPath ?? string.Empty
    };
}
