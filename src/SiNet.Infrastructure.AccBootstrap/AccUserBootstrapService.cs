using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Bootstraps SI users into the ACC Emails Project based on their AccUserType.
/// Idempotent: SKIP if already correct, ADD if missing, UPGRADE if access level needs increase.
/// If AccSystemResource doesn't exist, searches for the project by name and creates the record.
/// </summary>
public class AccUserBootstrapService(
    ITokenProvider tokenProvider,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory) : IAccUserBootstrapService
{
    private readonly ITokenProvider _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

    // ACC access levels for docs product
    private const string AccessLevelMember = "member";
    private const string AccessLevelAdmin = "administrator";

    // Project name to search for (same as AccBootstrapService default)
    private const string EmailsProjectName = "מיילים למשרד - POC 4";

    // In-memory cache: once discovered, the project ID never changes during app lifetime.
    // Eliminates DB round-trip on every ProvisionUsersAsync invocation.
    private static volatile string? _cachedEmailsProjectId;

    /// <inheritdoc/>
    public async Task ProvisionUsersAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];

        AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] ═══════════════════════════════════════════════");
        AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Starting ACC user provisioning");

        try
        {
            // Create Bim360Service early - we may need it for project discovery
            var bim360 = new Bim360Service(_tokenProvider);

            // Wire up logging from Bim360Service to AccBootstrapLog
            Bim360Service.LogInfo = msg => AccBootstrapLog.Info(msg);
            Bim360Service.LogWarn = msg => AccBootstrapLog.Warn(msg);
            Bim360Service.LogError = msg => AccBootstrapLog.Error(msg);

            // Step 1: Get EmailsProjectId from AccSystemResource table (or discover it)
            string? emailsProjectId = await GetOrDiscoverEmailsProjectIdAsync(correlationId, bim360, cancellationToken);

            if (string.IsNullOrEmpty(emailsProjectId))
            {
                AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] Could not find or discover EmailsProjectId. Skipping provisioning.");
                return;
            }

            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] EmailsProjectId={emailsProjectId}");

            // Step 2: Get all SI users with ACC access (Engineer or Admin)
            List<SiUserAccInfo> siUsers;
            await using (var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                siUsers = await context.Siusers
                    .Where(u => u.IsActive && u.AccUserType != AccUserType.NoAccUser && !string.IsNullOrEmpty(u.Email))
                    .Select(u => new SiUserAccInfo
                    {
                        Id = u.Id,
                        Email = u.Email!.Trim().ToLowerInvariant(),
                        Name = u.Name ?? u.LoginName ?? "Unknown",
                        AccUserType = u.AccUserType
                    })
                    .ToListAsync(cancellationToken);
            }

            if (siUsers.Count == 0)
            {
                AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] No SI users with ACC access (AccUserType != NoAccUser) found. Nothing to provision.");
                return;
            }

            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Found {siUsers.Count} SI users with ACC access:");
            foreach (var u in siUsers)
            {
                AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}]   - {u.Email} (AccUserType={u.AccUserType})");
            }

            // Step 3: Fetch existing ACC project members (once)
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Fetching existing ACC project members...");
            var existingMembers = await bim360.GetProjectUsersAsync(emailsProjectId, cancellationToken);

            // Build email→member cache (lowercase+trim for comparison)
            var memberCache = existingMembers
                .Where(m => !string.IsNullOrEmpty(m.Email))
                .ToDictionary(
                    m => m.Email.Trim().ToLowerInvariant(),
                    m => m,
                    StringComparer.OrdinalIgnoreCase);

            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Fetched {memberCache.Count} existing ACC project members");

            // Step 4: Process each SI user - SKIP / ADD / UPGRADE
            int skipped = 0, added = 0, upgraded = 0, failed = 0;

            foreach (var user in siUsers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetAccessLevel = user.AccUserType switch
                {
                    AccUserType.Engineer => AccessLevelMember,
                    AccUserType.Admin => AccessLevelAdmin,
                    _ => null // Should not happen due to filter above
                };

                if (targetAccessLevel == null)
                {
                    AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] SKIP user={user.Email} reason=UnexpectedAccUserType({user.AccUserType})");
                    skipped++;
                    continue;
                }

                // Check if user already exists in project
                if (memberCache.TryGetValue(user.Email, out var existingMember))
                {
                    // User exists - check current access level
                    var currentDocsAccess = existingMember.ProductAccess.TryGetValue("docs", out var da)
                        ? da?.ToLowerInvariant()
                        : null;

                    // Compare: need upgrade only if target > current
                    if (IsAccessLevelSufficient(currentDocsAccess, targetAccessLevel))
                    {
                        AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] SKIP user={user.Email} reason=AlreadyHasAccess currentDocs={currentDocsAccess ?? "N/A"}");
                        skipped++;
                        continue;
                    }

                    // Need upgrade: member → administrator
                    AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] UPGRADE user={user.Email} from={currentDocsAccess ?? "N/A"} to={targetAccessLevel}");

                    try
                    {
                        var upgradeResult = await bim360.UpdateProjectMemberAccessAsync(
                            emailsProjectId,
                            existingMember.UserId,
                            targetAccessLevel,
                            cancellationToken);

                        if (upgradeResult.Success)
                        {
                            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] UPGRADED user={user.Email} action={upgradeResult.Action}");
                            upgraded++;
                        }
                        else
                        {
                            AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] FAILED user={user.Email} reason={upgradeResult.Message}");
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AccBootstrapLog.Error(ex, $"[AccUserBootstrap:{correlationId}] FAILED user={user.Email} reason=Exception");
                        failed++;
                    }
                }
                else
                {
                    // User does not exist - ADD
                    AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] ADD user={user.Email} accessLevel={targetAccessLevel}");

                    try
                    {
                        var addResult = await bim360.AddProjectMemberAsync(
                            emailsProjectId,
                            user.Email,
                            targetAccessLevel,
                            cancellationToken);

                        if (addResult.Success)
                        {
                            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] ADDED user={user.Email} action={addResult.Action}");
                            added++;
                        }
                        else
                        {
                            AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] FAILED user={user.Email} reason={addResult.Message}");
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AccBootstrapLog.Error(ex, $"[AccUserBootstrap:{correlationId}] FAILED user={user.Email} reason=Exception");
                        failed++;
                    }
                }
            }

            // Summary log
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] ═══════════════════════════════════════════════");
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Provisioning complete: SKIP={skipped} ADD={added} UPGRADE={upgraded} FAILED={failed}");
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] ═══════════════════════════════════════════════");
        }
        catch (OperationCanceledException)
        {
            AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] Provisioning cancelled.");
            // Don't re-throw - this is fire-and-forget, just log
        }
        catch (TimeoutException tex)
        {
            AccBootstrapLog.Error($"[AccUserBootstrap:{correlationId}] Browser authorization timed out. " +
                "The user must complete Autodesk login in the browser within the timeout period. " +
                "Ensure the redirect URI (http://localhost:8080/) matches the APS Console configuration. " +
                $"Details: {tex.Message}");
            // Don't re-throw - this is fire-and-forget, just log
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Error(ex, $"[AccUserBootstrap:{correlationId}] Provisioning failed with exception: {ex.InnerException?.Message ?? ex.Message}");
            // Don't re-throw - this is fire-and-forget, just log
        }
    }

    /// <summary>
    /// Gets the EmailsProjectId from AccSystemResource, or discovers it by searching ACC by project name.
    /// If discovered, saves it to AccSystemResource for future use.
    /// Uses a static in-memory cache to avoid DB queries after first successful resolution.
    /// Uses a single DbContext for all DB operations to reduce EF Core overhead.
    /// </summary>
    private async Task<string?> GetOrDiscoverEmailsProjectIdAsync(
        string correlationId, 
        Bim360Service bim360, 
        CancellationToken cancellationToken)
    {
        // Fast path: return cached value (avoids DbContext creation entirely)
        if (_cachedEmailsProjectId is not null)
        {
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Using cached EmailsProjectId={_cachedEmailsProjectId}");
            return _cachedEmailsProjectId;
        }

        // Single DbContext for all DB operations in this method
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Check AccSystemResource table (read-only, no tracking needed)
        var resource = await context.AccSystemResources
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == AccConstants.OfficeInboxResourceKey, cancellationToken);

        if (resource != null && !string.IsNullOrEmpty(resource.AccProjectId))
        {
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Found AccSystemResource: Key={AccConstants.OfficeInboxResourceKey}, ProjectId={resource.AccProjectId}");
            _cachedEmailsProjectId = resource.AccProjectId;
            return resource.AccProjectId;
        }

        AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] AccSystemResource not found. Attempting to discover project by name...");

        // Get the hub from DB — single query with OrderByDescending to prefer default hub
        var accHub = await context.AccHubs
            .AsNoTracking()
            .OrderByDescending(h => h.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);

        if (accHub == null)
        {
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] No AccHub found in database. Discovering via API...");

            // Discover hub from API
            accHub = await DiscoverAndCreateHubAsync(correlationId, bim360, cancellationToken);

            if (accHub == null)
            {
                AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] Could not discover AccHub from API. Cannot search for project by name.");
                return null;
            }
        }

        // Extract account ID from hub ID (remove "b." prefix if present)
        var accountId = accHub.HubId.StartsWith("b.") ? accHub.HubId[2..] : accHub.HubId;
        AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Searching for project '{EmailsProjectName}' in account {accountId}...");

        // Search for project by name using ACC Admin API
        var projectId = await bim360.GetAccNativeProjectByNameAsync(accountId, EmailsProjectName, cancellationToken);

        if (string.IsNullOrEmpty(projectId))
        {
            AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] Project '{EmailsProjectName}' not found in ACC account.");
            return null;
        }

        AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Found project by name: '{EmailsProjectName}' -> ProjectId={projectId}");

        // Save to AccSystemResource for future use (reuse same context)
        var now = DateTime.UtcNow;
        var newResource = new AccSystemResource
        {
            Key = AccConstants.OfficeInboxResourceKey,
            AccHubId = accHub.Id,
            AccProjectId = projectId,
            Notes = $"Auto-discovered by AccUserBootstrapService at {now:u}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.AccSystemResources.Add(newResource);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Saved AccSystemResource: Key={AccConstants.OfficeInboxResourceKey}, ProjectId={projectId}");
        }
        catch (DbUpdateException ex)
        {
            // Race condition - another process may have inserted it
            AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] Failed to save AccSystemResource (race condition?): {ex.Message}");
            // Still return the projectId - we can use it even if we couldn't save
        }

        _cachedEmailsProjectId = projectId;
        return projectId;
    }

    /// <summary>
    /// Discovers the AccHub from the API and creates the database record.
    /// Returns null if no hub found or multiple hubs found (not supported).
    /// </summary>
    private async Task<AccHub?> DiscoverAndCreateHubAsync(
        string correlationId,
        Bim360Service bim360,
        CancellationToken cancellationToken)
    {
        try
        {
            // Call the API to get available hubs
            var hubs = await bim360.ListHubsAsync(cancellationToken);

            if (hubs.Count == 0)
            {
                AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] No hubs found in Autodesk account.");
                return null;
            }

            // Log discovered hubs
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Discovered {hubs.Count} hub(s) from API:");
            foreach (var hub in hubs)
            {
                AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}]   - {hub.Id} \"{hub.Name}\" (type={hub.Type})");
            }

            // Current implementation requires exactly 1 hub
            if (hubs.Count > 1)
            {
                AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] Multiple hubs found ({hubs.Count}). Current implementation requires exactly 1 hub.");
                return null;
            }

            var singleHub = hubs[0];
            AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Using hub: {singleHub.Id} ({singleHub.Name})");

            // Create the AccHub record in the database
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Check if any default hub already exists
            var hasAnyDefault = await context.AccHubs.AnyAsync(h => h.IsDefault, cancellationToken);
            var shouldBeDefault = !hasAnyDefault;

            var now = DateTime.UtcNow;
            var newHub = new AccHub
            {
                HubId = singleHub.Id,
                DisplayName = singleHub.Name,
                IsDefault = shouldBeDefault,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            try
            {
                context.AccHubs.Add(newHub);
                await context.SaveChangesAsync(cancellationToken);
                AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Created AccHub: HubId={singleHub.Id}, DisplayName={singleHub.Name}, IsDefault={shouldBeDefault}");
                return newHub;
            }
            catch (DbUpdateException ex)
            {
                // Race condition - another process may have inserted it
                AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] AccHub insert failed (race condition?): {ex.Message}");

                // Try to fetch the existing hub
                context.Entry(newHub).State = EntityState.Detached;
                var existingHub = await context.AccHubs.FirstOrDefaultAsync(h => h.HubId == singleHub.Id, cancellationToken);

                if (existingHub != null)
                {
                    AccBootstrapLog.Info($"[AccUserBootstrap:{correlationId}] Using existing AccHub from race winner: DbId={existingHub.Id}");
                    return existingHub;
                }

                AccBootstrapLog.Warn($"[AccUserBootstrap:{correlationId}] Could not find or create AccHub.");
                return null;
            }
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Error(ex, $"[AccUserBootstrap:{correlationId}] Failed to discover hub from API");
            return null;
        }
    }

    /// <summary>
    /// Checks if current access level is sufficient for the target.
    /// administrator >= member >= viewer
    /// </summary>
    private static bool IsAccessLevelSufficient(string? currentAccess, string targetAccess)
    {
        if (string.IsNullOrEmpty(currentAccess))
            return false;

        // If target is member, any access is sufficient (member, administrator)
        if (targetAccess == AccessLevelMember)
            return currentAccess is AccessLevelMember or AccessLevelAdmin;

        // If target is administrator, only administrator is sufficient
        if (targetAccess == AccessLevelAdmin)
            return currentAccess == AccessLevelAdmin;

        return false;
    }

    /// <summary>
    /// Internal DTO for SI user ACC info.
    /// </summary>
    private sealed class SiUserAccInfo
    {
        public int Id { get; init; }
        public required string Email { get; init; }
        public required string Name { get; init; }
        public AccUserType AccUserType { get; init; }
    }
}
