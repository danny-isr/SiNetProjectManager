using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNetSQL.Data;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
using SiNetSQL.Models;
using System.Diagnostics;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Bootstraps ACC (Autodesk Construction Cloud) resources for the Office Inbox workflow.
/// 
/// RESPONSIBILITIES:
/// - Auto-detect hub from API (must have exactly 1 hub)
/// - Ensure AccHub record exists in database
/// - Find or create the Office Inbox project in ACC
/// - For ACC-native: Assign Project Admin + Enable Docs
/// - Find or create the "_Inbox" folder in that project
/// - Persist all resolved IDs to the database (AccSystemResource)
/// 
/// DATABASE IS THE SOURCE OF TRUTH:
/// - After first run, all IDs are read from DB
/// - No configuration files needed for ACC identifiers
/// 
/// CONFIGURATION (via constructor):
/// - InboxProjectName: Name of the Office Inbox project
/// - InboxFolderName: Name of the inbox folder
/// - ForceCreate: Whether to auto-create project if not found
/// - Platform: Which API to use (AccNative or LegacyBim360)
/// - BootstrapAdminEmail: Email for Project Admin assignment (required for ACC-native)
/// - DryRun: When true, no mutating API calls are made
/// 
/// AUTHENTICATION:
/// - Requires a valid Bim360Service instance with authentication already handled
/// - Does NOT handle token acquisition (caller's responsibility)
/// </summary>
public class AccBootstrapService : IAccBootstrapService
{
    // === TEMP DEV: Docs provisioning poll settings (30 attempts × 5s = 2.5 min max) ===
    private const int DocsProvisioningMaxAttempts = 30;
    private const int DocsProvisioningDelaySeconds = 5;
    // === END TEMP DEV ===

    private readonly SiNetSQLDbContext _dbContext;
    private readonly Bim360Service _bim360Service;

    // === Configuration (set via constructor) ===
    private readonly string _inboxProjectName;
    private readonly string _inboxFolderName;
    private readonly bool _forceCreateProject;
    private readonly CreateProjectPlatform _createPlatform;
    private readonly string _bootstrapAdminEmail;
    private readonly bool _dryRun;

    /// <summary>
    /// Optional ACC project template name. When non-empty AND the platform is AccNative,
    /// the Office Inbox project will be created FROM the named template, inheriting its
    /// folder ACLs and structure. Resolved to a template id at bootstrap time.
    /// </summary>
    private readonly string? _templateName;

    // Resolved hub info (populated during EnsureOfficeInboxAsync)
    private string? _resolvedHubId;
    private string? _resolvedHubName;

    // Track which platform was actually used for project creation
    private AccPlatform _detectedPlatform = AccPlatform.Unknown;

    // Bootstrap timeline tracking
    private readonly Stopwatch _bootstrapStopwatch = new();
    private bool _projectWasCreated;
    private bool _adminWasAssigned;
    private string? _finalProjectId;
    private string? _finalDataProjectId;
    private string? _finalRootFolderId;
    private string? _finalInboxFolderId;
    private DocsStatus _finalDocsStatus = DocsStatus.Unknown;
    private string? _docsLastError;

    /// <summary>
    /// Creates a new AccBootstrapService with default configuration.
    /// </summary>
    /// <param name="dbContext">EF Core database context.</param>
    /// <param name="bim360Service">BIM 360/ACC service with valid authentication.</param>
    public AccBootstrapService(
        SiNetSQLDbContext dbContext,
        Bim360Service bim360Service)
        : this(dbContext, bim360Service, 
               inboxProjectName: "מיילים למשרד - POC 4",
               inboxFolderName: "_Inbox",
               forceCreateProject: true,
               createPlatform: CreateProjectPlatform.AccNative,
               bootstrapAdminEmail: "",
               dryRun: false)
    {
    }

    /// <summary>
    /// Creates a new AccBootstrapService with custom configuration.
    /// </summary>
    /// <param name="dbContext">EF Core database context.</param>
    /// <param name="bim360Service">BIM 360/ACC service with valid authentication.</param>
    /// <param name="inboxProjectName">Name of the Office Inbox project.</param>
    /// <param name="inboxFolderName">Name of the inbox folder.</param>
    /// <param name="forceCreateProject">Whether to auto-create project if not found.</param>
    /// <param name="createPlatform">Which platform API to use for project creation.</param>
    /// <param name="bootstrapAdminEmail">Email of user to assign as Project Admin (required for ACC-native).</param>
    /// <param name="dryRun">When true, no mutating API calls are made.</param>
    public AccBootstrapService(
        SiNetSQLDbContext dbContext,
        Bim360Service bim360Service,
        string inboxProjectName,
        string inboxFolderName,
        bool forceCreateProject,
        CreateProjectPlatform createPlatform,
        string bootstrapAdminEmail = "",
        bool dryRun = false,
        string? templateName = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _bim360Service = bim360Service ?? throw new ArgumentNullException(nameof(bim360Service));
        _inboxProjectName = inboxProjectName ?? throw new ArgumentNullException(nameof(inboxProjectName));
        _inboxFolderName = inboxFolderName ?? throw new ArgumentNullException(nameof(inboxFolderName));
        _forceCreateProject = forceCreateProject;
        _createPlatform = createPlatform;
        _bootstrapAdminEmail = bootstrapAdminEmail ?? "";
        _dryRun = dryRun;
        _templateName = string.IsNullOrWhiteSpace(templateName) ? null : templateName.Trim();

        // Pass DryRun flag to Bim360Service
        _bim360Service.DryRun = _dryRun;
    }

    /// <inheritdoc />
    public async Task<OfficeInboxTargets> EnsureOfficeInboxAsync(string currentLogin, CancellationToken cancellationToken)
    {
        _bootstrapStopwatch.Start();

        // ═══════════════════════════════════════════════════════════════════════════════
        // DECISION BLOCK - Log configuration once at start for observability
        // ═══════════════════════════════════════════════════════════════════════════════
        LogDecisionBlock(currentLogin);

        try
        {
            AccBootstrapLog.Info($"[AccBootstrap] EnsureOfficeInboxAsync started. User={currentLogin}");

            // Step 0: Resolve Hub ID (auto-detect from API, must be exactly 1 hub)
            await ResolveHubIdAsync(cancellationToken);

            // Step 1: Ensure AccHub row exists in DB
            var accHub = await EnsureAccHubAsync(cancellationToken);
            AccBootstrapLog.Info($"[AccBootstrap] AccHub ensured. DbId={accHub.Id}, HubId={accHub.HubId}, IsDefault={accHub.IsDefault}");

            // Step 2: Check if AccSystemResource already has valid IDs (DB is source of truth)
            var existingResource = await _dbContext.AccSystemResources
                .FirstOrDefaultAsync(r => r.Key == AccConstants.OfficeInboxResourceKey && r.AccHubId == accHub.Id, cancellationToken);

            if (existingResource != null &&
                !string.IsNullOrEmpty(existingResource.AccProjectId) &&
                !string.IsNullOrEmpty(existingResource.AccInboxFolderId))
            {
                AccBootstrapLog.Info($"[AccBootstrap] Found existing AccSystemResource. Re-validating...");
                AccBootstrapLog.Info($"[AccBootstrap]   Existing ProjectId={existingResource.AccProjectId}");
                AccBootstrapLog.Info($"[AccBootstrap]   Existing InboxFolderId={existingResource.AccInboxFolderId}");
            }

            // Step 3: Bootstrap ACC - find or create project and folders
            AccBootstrapLog.Info($"[AccBootstrap] Bootstrapping ACC resources...");
            var (projectId, rootFolderId, inboxFolderId) = await BootstrapAccResourcesAsync(cancellationToken);

            // Store final values for timeline log
            _finalProjectId = projectId;
            _finalDataProjectId = projectId.StartsWith("b.") ? projectId : "b." + projectId;
            _finalRootFolderId = rootFolderId;
            _finalInboxFolderId = inboxFolderId;
            _finalDocsStatus = DocsStatus.Ready;

            // Step 4: Ensure Office Inbox custom-attribute definitions exist before clients write metadata values.
            await EnsureInboxCustomAttributeDefinitionsAsync(projectId, inboxFolderId, cancellationToken);

            // Step 5: Persist to database with race-condition handling
            var result = await SaveOrUpdateSystemResourceAsync(
                accHub, existingResource, projectId, rootFolderId, inboxFolderId, currentLogin, cancellationToken);

            // Log Bootstrap Timeline at end
            LogBootstrapTimeline(success: true, accHub);

            return result;
        }
        catch (Exception ex)
        {
            // Log Bootstrap Timeline on failure
            _docsLastError = ex.Message;
            LogBootstrapTimeline(success: false, accHub: null);
            throw;
        }
        finally
        {
            _bootstrapStopwatch.Stop();
        }
    }

    /// <summary>
    /// Logs the decision block at the start of bootstrap for full observability.
    /// </summary>
    private void LogDecisionBlock(string currentLogin)
    {
        AccBootstrapLog.Info($"[AccBootstrap] ╔═══════════════════════════════════════════════════════════════════════════════════╗");
        AccBootstrapLog.Info($"[AccBootstrap] ║                          BOOTSTRAP DECISION BLOCK                                ║");
        AccBootstrapLog.Info($"[AccBootstrap] ╠═══════════════════════════════════════════════════════════════════════════════════╣");
        AccBootstrapLog.Info($"[AccBootstrap] ║ User:              {currentLogin,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ DryRun:            {_dryRun,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ Platform:          {_createPlatform,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ Project Name:      {_inboxProjectName,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ Folder Name:       {_inboxFolderName,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ ForceCreate:       {_forceCreateProject,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ Admin Email:       {(string.IsNullOrEmpty(_bootstrapAdminEmail) ? "(NOT SET)" : _bootstrapAdminEmail),-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ╚═══════════════════════════════════════════════════════════════════════════════════╝");

        if (_createPlatform == CreateProjectPlatform.AccNative && string.IsNullOrEmpty(_bootstrapAdminEmail))
        {
            AccBootstrapLog.Warn($"[AccBootstrap] ⚠ WARNING: BootstrapAdminEmail is NOT SET.");
            AccBootstrapLog.Warn($"[AccBootstrap]   ACC-native project creation REQUIRES admin assignment to enable Docs.");
            AccBootstrapLog.Warn($"[AccBootstrap]   Set 'Autodesk:BootstrapAdminEmail' in appsettings.json or appsettings.local.json");
        }
    }

    /// <summary>
    /// Logs the Bootstrap Timeline summary at the end (success or failure).
    /// </summary>
    private void LogBootstrapTimeline(bool success, AccHub? accHub)
    {
        var durationMs = _bootstrapStopwatch.ElapsedMilliseconds;
        var status = success ? "SUCCESS" : "FAILED";

        AccBootstrapLog.Info($"[AccBootstrap] ╔═══════════════════════════════════════════════════════════════════════════════════╗");
        AccBootstrapLog.Info($"[AccBootstrap] ║                          BOOTSTRAP TIMELINE: {status,-10}                        ║");
        AccBootstrapLog.Info($"[AccBootstrap] ╠═══════════════════════════════════════════════════════════════════════════════════╣");
        AccBootstrapLog.Info($"[AccBootstrap] ║ Duration:          {durationMs + "ms",-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ DryRun:            {_dryRun,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ Platform:          {_detectedPlatform,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ╠───────────────────────────────────────────────────────────────────────────────────╣");
        AccBootstrapLog.Info($"[AccBootstrap] ║ HubId:             {(_resolvedHubId ?? "(none)"),-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ HubName:           {(_resolvedHubName ?? "(none)"),-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ AccHubDbId:        {(accHub?.Id.ToString() ?? "(none)"),-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ╠───────────────────────────────────────────────────────────────────────────────────╣");
        AccBootstrapLog.Info($"[AccBootstrap] ║ ProjectName:       {_inboxProjectName,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ ProjectId:         {(_finalProjectId ?? "(none)"),-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ DataProjectId:     {(_finalDataProjectId ?? "(none)"),-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ ProjectCreated:    {_projectWasCreated,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ AdminAssigned:     {_adminWasAssigned,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ╠───────────────────────────────────────────────────────────────────────────────────╣");
        AccBootstrapLog.Info($"[AccBootstrap] ║ DocsStatus:        {_finalDocsStatus,-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ RootFolderId:      {(_finalRootFolderId ?? "(none)"),-60} ║");
        AccBootstrapLog.Info($"[AccBootstrap] ║ InboxFolderId:     {(_finalInboxFolderId ?? "(none)"),-60} ║");

        if (!success && !string.IsNullOrEmpty(_docsLastError))
        {
            var errorSnippet = _docsLastError.Length > 60 ? _docsLastError[..57] + "..." : _docsLastError;
            AccBootstrapLog.Info($"[AccBootstrap] ╠───────────────────────────────────────────────────────────────────────────────────╣");
            AccBootstrapLog.Info($"[AccBootstrap] ║ Error:             {errorSnippet,-60} ║");
        }

        AccBootstrapLog.Info($"[AccBootstrap] ╠───────────────────────────────────────────────────────────────────────────────────╣");
        if (_dryRun)
        {
            AccBootstrapLog.Info($"[AccBootstrap] ║ DB Persisted:      false (DryRun mode - no changes saved)                         ║");
        }
        else
        {
            AccBootstrapLog.Info($"[AccBootstrap] ║ DB Persisted:      {(success ? "YES" : "NO"),-60} ║");
        }
        AccBootstrapLog.Info($"[AccBootstrap] ╚═══════════════════════════════════════════════════════════════════════════════════╝");
    }

    /// <summary>
    /// Saves or updates the AccSystemResource with race-condition handling.
    /// If a concurrent insert causes a PK conflict, re-queries and returns the existing row.
    /// </summary>
    private async Task<OfficeInboxTargets> SaveOrUpdateSystemResourceAsync(
        AccHub accHub,
        AccSystemResource? existingResource,
        string projectId,
        string rootFolderId,
        string inboxFolderId,
        string currentLogin,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        try
        {
            if (existingResource == null)
            {
                // Create new resource
                existingResource = new AccSystemResource
                {
                    Key = AccConstants.OfficeInboxResourceKey,
                    AccHubId = accHub.Id,
                    AccProjectId = projectId,
                    AccRootFolderId = rootFolderId,
                    AccInboxFolderId = inboxFolderId,
                    Notes = $"Bootstrapped by {currentLogin}",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _dbContext.AccSystemResources.Add(existingResource);
                AccBootstrapLog.Info($"[AccBootstrap] Created new AccSystemResource row for OfficeInbox.");
            }
            else
            {
                // Update existing resource
                existingResource.AccProjectId = projectId;
                existingResource.AccRootFolderId = rootFolderId;
                existingResource.AccInboxFolderId = inboxFolderId;
                existingResource.Notes = $"Re-bootstrapped by {currentLogin}";
                existingResource.UpdatedAtUtc = now;
                AccBootstrapLog.Info($"[AccBootstrap] Updated existing AccSystemResource row for OfficeInbox.");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            AccBootstrapLog.Info($"[AccBootstrap] Database saved successfully.");

            return new OfficeInboxTargets
            {
                AccHubDbId = accHub.Id,
                HubId = accHub.HubId,
                AccProjectId = projectId,
                AccRootFolderId = rootFolderId,
                AccInboxFolderId = inboxFolderId
            };
        }
        catch (DbUpdateException ex) when (IsPrimaryKeyOrUniqueConstraintViolation(ex))
        {
            // Race condition: another process inserted the row first
            // Detach the conflicting entity and re-query
            AccBootstrapLog.Warn($"[AccBootstrap] Race condition detected during save. Re-querying existing resource...");

            if (existingResource != null)
            {
                _dbContext.Entry(existingResource).State = EntityState.Detached;
            }

            var raceWinner = await _dbContext.AccSystemResources
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == AccConstants.OfficeInboxResourceKey && r.AccHubId == accHub.Id, cancellationToken);

            if (raceWinner != null &&
                !string.IsNullOrEmpty(raceWinner.AccProjectId) &&
                !string.IsNullOrEmpty(raceWinner.AccInboxFolderId))
            {
                AccBootstrapLog.Info($"[AccBootstrap] Using race-winner resource. ProjectId={raceWinner.AccProjectId}");
                return new OfficeInboxTargets
                {
                    AccHubDbId = accHub.Id,
                    HubId = accHub.HubId,
                    AccProjectId = raceWinner.AccProjectId,
                    AccRootFolderId = raceWinner.AccRootFolderId ?? string.Empty,
                    AccInboxFolderId = raceWinner.AccInboxFolderId
                };
            }

            // If we can't recover, rethrow
            throw;
        }
    }

    /// <summary>
    /// Checks if the exception indicates a PK or unique constraint violation.
    /// </summary>
    private static bool IsPrimaryKeyOrUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server error codes: 2601 = unique index violation, 2627 = PK violation
        var sqlEx = ex.InnerException as Microsoft.Data.SqlClient.SqlException;
        return sqlEx != null && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }

    private async Task<bool> EnsureInboxCustomAttributeDefinitionsAsync(
        string accProjectId,
        string inboxFolderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
        {
            AccBootstrapLog.Error("[AccBootstrap] Inbox custom attribute definitions were not ensured: accProjectId is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(inboxFolderId))
        {
            AccBootstrapLog.Error("[AccBootstrap] Inbox custom attribute definitions were not ensured: inboxFolderId is required.");
            return false;
        }

        var definitions = BuildInboxCustomAttributeDefinitions();
        AccBootstrapLog.Info(
            $"[AccBootstrap] Ensuring {definitions.Length} Inbox custom attribute definitions in ACC Inbox project. ProjectId={accProjectId}, FolderId={inboxFolderId}");

        try
        {
            var result = await _bim360Service
                .EnsureCustomAttributeDefinitionsAsync(accProjectId, inboxFolderId, definitions, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                AccBootstrapLog.Info("[AccBootstrap] Inbox custom attribute definitions ensured.");
                return true;
            }

            AccBootstrapLog.Error(
                $"[AccBootstrap] Failed to ensure Inbox custom attribute definitions. http={result.HttpStatus}, error={result.ErrorMessage}");
            return false;
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Error(ex, "[AccBootstrap] Exception while ensuring Inbox custom attribute definitions.");
            return false;
        }
    }

    private static CustomAttributeDefinition[] BuildInboxCustomAttributeDefinitions()
    {
        static CustomAttributeDefinition TextDefinition(string name, string displayName, string description) =>
            new(name, displayName, CustomAttributeType.Text, description);

        return new[]
        {
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.TagProjectFileId,
                "Si Inbox Tag Project File Id",
                "Target ProjectFile id selected for an Office Inbox attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.TagProjectAlternativeId,
                "Si Inbox Tag Project Alternative Id",
                "Target ProjectAlternative id selected for an Office Inbox attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.TagTaggedBy,
                "Si Inbox Tag Tagged By",
                "User or process that last tagged the Office Inbox attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.TagTaggedAtUtc,
                "Si Inbox Tag Tagged At UTC",
                "UTC timestamp when the Office Inbox attachment was last tagged."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.TagStatus,
                "Si Inbox Tag Status",
                "Tagging status for the Office Inbox attachment."),

            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveMovedToProject,
                "Si Inbox Move Moved To Project",
                "Whether the Office Inbox attachment was moved/filed to a project."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveMovedAtUtc,
                "Si Inbox Move Moved At UTC",
                "UTC timestamp when the Office Inbox attachment was moved/filed."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveMovedBy,
                "Si Inbox Move Moved By",
                "User or process that moved/filed the Office Inbox attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetDestination,
                "Si Inbox Move Target Destination",
                "Target storage destination for the filed attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectId,
                "Si Inbox Move Target Project Id",
                "Target SiNet project id for the filed attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectFileId,
                "Si Inbox Move Target Project File Id",
                "Target ProjectFile id for the filed attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetProjectAlternativeId,
                "Si Inbox Move Target Project Alternative Id",
                "Target ProjectAlternative id for the filed attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetFileName,
                "Si Inbox Move Target File Name",
                "Target file name after the attachment was filed."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetAccItemId,
                "Si Inbox Move Target ACC Item Id",
                "Target ACC item id after the attachment was filed."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetAccFolderId,
                "Si Inbox Move Target ACC Folder Id",
                "Target ACC folder id after the attachment was filed."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.MoveTargetFilePath,
                "Si Inbox Move Target File Path",
                "Target FileServer path after the attachment was filed."),

            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.LockLockedForEditing,
                "Si Inbox Lock Locked For Editing",
                "Whether the Office Inbox attachment is locked from further tag/edit operations."),

            // Source-identity attributes (2026-05-24): written on the *target* ACC item
            // by MoveToProject so future moves can detect same-source identical files.
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.SourceGmailMessageId,
                "Si Inbox Source Gmail Message Id",
                "Gmail message id of the email the filed attachment came from."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.SourceMessageDateUtc,
                "Si Inbox Source Message Date UTC",
                "Canonical source date (email ReceivedUtc) of the filed attachment."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.SourceOriginalFileName,
                "Si Inbox Source Original File Name",
                "Original file name of the email attachment that produced this ACC item version."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.SourceFileSizeBytes,
                "Si Inbox Source File Size Bytes",
                "Original file size in bytes of the email attachment that produced this version."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.SourceContentSha256,
                "Si Inbox Source Content SHA-256",
                "Content SHA-256 of the email attachment that produced this version."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.SourceAttachmentId,
                "Si Inbox Source Attachment Id",
                "EmailInboxAttachment.Id that produced this ACC item version."),

            // Identity attributes (2026-05-28): canonical message/thread identity
            // written on EVERY ACC file/item produced by Inbox ingestion so any
            // item can be traced back to its source email/thread without relying
            // on folder structure. Folder-level metadata stays in manifest.json.
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.IdentityMessageUniqueId,
                "Si Inbox Identity Message Unique Id",
                "Canonical message unique id (EmailInboxMessage.MessageUniqueId)."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.IdentityThreadUniqueId,
                "Si Inbox Identity Thread Unique Id",
                "Canonical thread unique id (EmailInboxMessage.ThreadUniqueId)."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.IdentityMessageKey,
                "Si Inbox Identity Message Key",
                "Short message key used as MSG_ folder suffix."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.IdentityThreadKey,
                "Si Inbox Identity Thread Key",
                "Short thread key used as THREAD_ folder suffix."),
            TextDefinition(
                SidecarMetadata.InboxAccAttributeNames.IdentityInternetMessageId,
                "Si Inbox Identity Internet Message Id",
                "RFC 2822 Message-ID header value of the source email."),
        };
    }

    /// <summary>
    /// Resolves the Hub ID from database first, then API as fallback.
    /// RULE: DB is source of truth. Only call API if DB has no hubs.
    /// RULE: Must have exactly 1 hub. If multiple hubs exist, this is an error.
    /// </summary>
    /// <exception cref="InvalidOperationException">When no hubs or multiple hubs found.</exception>
    private async Task ResolveHubIdAsync(CancellationToken cancellationToken)
    {
        AccBootstrapLog.Info("[AccBootstrap] Resolving Hub ID...");

        // === DB-FIRST: Check if AccHub table already contains a hub ===
        var dbHubs = await _dbContext.AccHubs.ToListAsync(cancellationToken);

        if (dbHubs.Count > 0)
        {
            AccBootstrapLog.Info($"[AccBootstrap] Found {dbHubs.Count} hub(s) in database. Using DB as source of truth.");

            // Log all DB hubs
            foreach (var hub in dbHubs)
            {
                AccBootstrapLog.Info($"[AccBootstrap]   DB Hub: {hub.HubId} \"{hub.DisplayName}\" (IsDefault={hub.IsDefault}, DbId={hub.Id})");
            }

            // RULE: Must have exactly 1 hub (for now)
            if (dbHubs.Count > 1)
            {
                AccBootstrapLog.Error("[AccBootstrap] ERROR: Multiple hubs found in database. This is not supported in current implementation.");
                throw new InvalidOperationException(
                    $"Multiple hubs ({dbHubs.Count}) found in database. " +
                    "Current implementation requires exactly 1 hub. " +
                    "Contact support if you need multi-hub support.");
            }

            // Use the single hub from DB
            var singleHub = dbHubs[0];
            _resolvedHubId = singleHub.HubId;
            _resolvedHubName = singleHub.DisplayName;
            AccBootstrapLog.Info($"[AccBootstrap] Using hub from DB: {_resolvedHubId} ({_resolvedHubName})");
            AccBootstrapLog.Info($"[AccBootstrap] SKIPPING API call to GET /project/v1/hubs (DB already has hub).");
            return;
        }

        // === FALLBACK: No hubs in DB, fetch from API ===
        AccBootstrapLog.Info("[AccBootstrap] No hubs in database. Fetching from API...");
        var hubs = await _bim360Service.ListHubsAsync(cancellationToken);

        if (hubs.Count == 0)
        {
            throw new InvalidOperationException(
                "No hubs found in Autodesk account. " +
                "Check that the app has correct permissions (data:read, data:write, account:read, account:write).");
        }

        AccBootstrapLog.Info($"[AccBootstrap] Found {hubs.Count} hub(s) from API.");

        // Log all hubs for clarity
        foreach (var hub in hubs)
        {
            AccBootstrapLog.Info($"[AccBootstrap]   API Hub: {hub.Id} \"{hub.Name}\" (type={hub.Type}, region={hub.Region ?? "N/A"})");
        }

        // RULE: Must have exactly 1 hub (for now)
        if (hubs.Count > 1)
        {
            AccBootstrapLog.Error("[AccBootstrap] ERROR: Multiple hubs found. This is not supported in current implementation.");
            throw new InvalidOperationException(
                $"Multiple hubs ({hubs.Count}) found in Autodesk account. " +
                "Current implementation requires exactly 1 hub. " +
                "Contact support if you need multi-hub support.");
        }

        // Use the single hub from API
        var singleApiHub = hubs[0];
        _resolvedHubId = singleApiHub.Id;
        _resolvedHubName = singleApiHub.Name;
        AccBootstrapLog.Info($"[AccBootstrap] Using hub from API: {_resolvedHubId} ({_resolvedHubName})");
    }

    /// <summary>
    /// Ensures AccHub row exists for the resolved Hub ID.
    /// Creates it if missing, with IsDefault=true only if no other default exists.
    /// Handles race conditions where another process might insert concurrently.
    /// </summary>
    private async Task<AccHub> EnsureAccHubAsync(CancellationToken cancellationToken)
    {
        // _resolvedHubId must be set by ResolveHubIdAsync before calling this
        if (string.IsNullOrEmpty(_resolvedHubId))
        {
            throw new InvalidOperationException("Hub ID not resolved. Call ResolveHubIdAsync first.");
        }

        // First, try to find existing hub
        var existingHub = await _dbContext.AccHubs
            .FirstOrDefaultAsync(h => h.HubId == _resolvedHubId, cancellationToken);

        if (existingHub != null)
        {
            // Update DisplayName if we have a better one from API
            if (!string.IsNullOrEmpty(_resolvedHubName) && existingHub.DisplayName != _resolvedHubName)
            {
                existingHub.DisplayName = _resolvedHubName;
                existingHub.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                AccBootstrapLog.Info($"[AccBootstrap] Updated AccHub DisplayName to '{_resolvedHubName}'.");
            }
            return existingHub;
        }

        // Hub doesn't exist - create it
        // Determine if this should be the default (only if no other default exists)
        var hasAnyDefault = await _dbContext.AccHubs.AnyAsync(h => h.IsDefault, cancellationToken);
        var shouldBeDefault = !hasAnyDefault;

        var now = DateTime.UtcNow;
        var newHub = new AccHub
        {
            HubId = _resolvedHubId,
            DisplayName = _resolvedHubName, // Now we have the name from API
            IsDefault = shouldBeDefault,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            _dbContext.AccHubs.Add(newHub);
            await _dbContext.SaveChangesAsync(cancellationToken);
            AccBootstrapLog.Info($"[AccBootstrap] Created new AccHub. HubId={_resolvedHubId}, DisplayName={_resolvedHubName}, IsDefault={shouldBeDefault}");
            return newHub;
        }
        catch (DbUpdateException ex) when (IsPrimaryKeyOrUniqueConstraintViolation(ex))
        {
            // Race condition: another process inserted this hub first
            AccBootstrapLog.Warn($"[AccBootstrap] Race condition on AccHub insert. Re-querying...");
            _dbContext.Entry(newHub).State = EntityState.Detached;

            var raceWinner = await _dbContext.AccHubs
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.HubId == _resolvedHubId, cancellationToken);

            if (raceWinner != null)
            {
                // Re-attach for tracking if needed by caller
                _dbContext.AccHubs.Attach(raceWinner);
                return raceWinner;
            }

            throw; // Should not happen, but rethrow if we can't find it
        }
    }

    /// <summary>
    /// Bootstraps ACC resources: finds or creates project and inbox folder.
    /// 
    /// For ACC-native projects, the flow is:
    /// 1. Find or create project
    /// 2. If created: Wait for project to become active
    /// 3. If created: Assign Project Admin + Enable Docs
    /// 4. Poll for Docs readiness
    /// 5. Find or create inbox folder
    /// </summary>
    /// <returns>Tuple of (projectId, rootFolderId, inboxFolderId)</returns>
    private async Task<(string projectId, string rootFolderId, string inboxFolderId)> BootstrapAccResourcesAsync(
        CancellationToken cancellationToken)
    {
        // _resolvedHubId must be set by ResolveHubIdAsync before calling this
        if (string.IsNullOrEmpty(_resolvedHubId))
        {
            throw new InvalidOperationException("Hub ID not resolved. Call ResolveHubIdAsync first.");
        }

        // Extract account ID from hub ID (remove "b." prefix if present)
        var accountId = _resolvedHubId.StartsWith("b.") ? _resolvedHubId[2..] : _resolvedHubId;

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step A: Find or create project
        // ═══════════════════════════════════════════════════════════════════════════════
        // IMPORTANT: Use appropriate API for project search based on platform:
        // - AccNative: Use ACC Admin API (construction/admin/v1) with 3-legged token
        // - LegacyBim360: Use HQ API (hq/v1) with 2-legged token
        // ═══════════════════════════════════════════════════════════════════════════════
        AccBootstrapLog.Info($"[AccBootstrap] Looking for project '{_inboxProjectName}' in hub {_resolvedHubId}...");
        AccBootstrapLog.Info($"[AccBootstrap] Search platform: {_createPlatform}");

        string? projectId;
        if (_createPlatform == CreateProjectPlatform.AccNative)
        {
            // ACC-native: Use ACC Admin API (requires 3-legged token)
            AccBootstrapLog.Info($"[AccBootstrap] Using ACC Admin API for project search (construction/admin/v1)...");
            projectId = await _bim360Service.GetAccNativeProjectByNameAsync(accountId, _inboxProjectName, cancellationToken);
        }
        else
        {
            // Legacy BIM360: Use HQ API (2-legged token)
            AccBootstrapLog.Info($"[AccBootstrap] Using Legacy HQ API for project search (hq/v1)...");
            projectId = await _bim360Service.GetProjectByNameAsync(accountId, _inboxProjectName);
        }
        _projectWasCreated = false;

        if (string.IsNullOrEmpty(projectId))
        {
            // Project not found - check if we should create it
            if (!_forceCreateProject)
            {
                _finalDocsStatus = DocsStatus.Error;
                _docsLastError = $"Project '{_inboxProjectName}' not found and ForceCreate=false";
                throw new InvalidOperationException(
                    $"Office Inbox project '{_inboxProjectName}' not found in ACC.\n\n" +
                    $"To fix:\n" +
                    $"1. Create the project manually in ACC Admin Console, OR\n" +
                    $"2. Set 'ForceCreateOfficeInboxProject': true in appsettings.json");
            }

            AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");
            AccBootstrapLog.Info($"[AccBootstrap] Project '{_inboxProjectName}' not found. Creating via API...");
            AccBootstrapLog.Info($"[AccBootstrap] Platform: {_createPlatform}");
            AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");

            try
            {
                if (_createPlatform == CreateProjectPlatform.AccNative)
                {
                    // === ACC-NATIVE PATH ===
                    // Step A1: Resolve optional template (if configured)
                    string? templateProjectId = null;
                    if (!string.IsNullOrEmpty(_templateName))
                    {
                        AccBootstrapLog.Info($"[AccBootstrap] Resolving ACC template by name='{_templateName}'...");
                        try
                        {
                            templateProjectId = await _bim360Service.GetAccNativeTemplateByNameAsync(
                                accountId, _templateName, cancellationToken);
                            if (string.IsNullOrEmpty(templateProjectId))
                                AccBootstrapLog.Warn($"[AccBootstrap] Template '{_templateName}' not found — creating without template.");
                            else
                                AccBootstrapLog.Info($"[AccBootstrap] TemplateProjectId={templateProjectId}");
                        }
                        catch (Exception tex)
                        {
                            AccBootstrapLog.Warn($"[AccBootstrap] Template lookup failed: {tex.Message} — creating without template.");
                        }
                    }

                    // Step A1b: Create project via ACC Admin API
                    AccBootstrapLog.Info($"[AccBootstrap] Using ACC-Native API (POST /construction/admin/v1/...){(string.IsNullOrEmpty(templateProjectId) ? "" : $" from template {templateProjectId}")}");
                    projectId = await _bim360Service.CreateAccNativeProjectAsync(
                        _resolvedHubId,    // with b. prefix
                        accountId,         // without b. prefix
                        _inboxProjectName,
                        cancellationToken,
                        templateProjectId);
                    _detectedPlatform = AccPlatform.AccNative;
                    _projectWasCreated = true;

                    // Step A2: Wait for project to become active (ACC-native only)
                    AccBootstrapLog.Info($"[AccBootstrap] Waiting for project to become active...");
                    var isActive = await _bim360Service.WaitForAccProjectActiveAsync(projectId, ct: cancellationToken);
                    if (!isActive)
                    {
                        AccBootstrapLog.Warn($"[AccBootstrap] Project may not be fully active yet. Continuing anyway...");
                    }

                    // Step A3: Assign Project Admin + Enable Docs (ACC-native only)
                    if (!string.IsNullOrEmpty(_bootstrapAdminEmail))
                    {
                        AccBootstrapLog.Info($"[AccBootstrap] Assigning Project Admin: {_bootstrapAdminEmail}");
                        _adminWasAssigned = await _bim360Service.AssignProjectAdminAsync(
                            projectId, 
                            _bootstrapAdminEmail, 
                            cancellationToken);

                        if (!_adminWasAssigned)
                        {
                            AccBootstrapLog.Warn($"[AccBootstrap] Admin assignment failed. Docs may not be enabled.");
                            AccBootstrapLog.Warn($"[AccBootstrap] You may need to enable Docs manually in ACC Admin Console.");
                        }
                    }
                    else
                    {
                        AccBootstrapLog.Warn($"[AccBootstrap] BootstrapAdminEmail not set. Skipping admin assignment.");
                        AccBootstrapLog.Warn($"[AccBootstrap] Docs may not be enabled automatically. Manual setup may be required.");
                        _adminWasAssigned = false;
                    }

                    AccBootstrapLog.Info($"[AccBootstrap] ✓ Project created via ACC-Native API!");
                }
                else
                {
                    // === LEGACY BIM360 PATH ===
                    AccBootstrapLog.Warn($"[AccBootstrap] Using Legacy BIM360 API (POST /hq/v1/...)");
                    AccBootstrapLog.Warn($"[AccBootstrap] NOTE: This creates a legacy BIM 360 project, not ACC-native.");
                    projectId = await _bim360Service.CreateProjectAsync(accountId, _inboxProjectName, "Office");
                    _detectedPlatform = AccPlatform.LegacyBim360;
                    _projectWasCreated = true;
                    _adminWasAssigned = false; // Not applicable for legacy
                    AccBootstrapLog.Info($"[AccBootstrap] ✓ Project created via Legacy BIM360 API!");
                }

                // Log project creation result
                AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");
                AccBootstrapLog.Info($"[AccBootstrap] PROJECT CREATION SUCCESS");
                AccBootstrapLog.Info($"[AccBootstrap]   ProjectId: {projectId}");
                AccBootstrapLog.Info($"[AccBootstrap]   Platform: {_detectedPlatform}");
                AccBootstrapLog.Info($"[AccBootstrap]   AdminAssigned: {_adminWasAssigned}");
                AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                _finalDocsStatus = DocsStatus.Error;
                _docsLastError = ex.Message;
                AccBootstrapLog.Error(ex, $"[AccBootstrap] Failed to create project '{_inboxProjectName}' using {_createPlatform}.");

                // Provide platform-specific guidance
                var guidance = _createPlatform == CreateProjectPlatform.AccNative
                    ? "Try setting 'CreateOfficeInboxPlatform': 'LegacyBim360' in appsettings.json as a fallback."
                    : "Check that the app is approved in ACC Admin > Settings > Custom Integrations.";

                throw new InvalidOperationException(
                    $"Failed to create Office Inbox project '{_inboxProjectName}' via {_createPlatform} API.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    $"POSSIBLE CAUSES:\n" +
                    $"1. Missing 'account:write' scope in your APS app\n" +
                    $"2. App not approved in ACC Admin > Settings > Custom Integrations\n" +
                    $"3. 2-legged auth may not have admin rights (ACC-Native may require 3-legged)\n" +
                    $"4. Account permissions issue\n\n" +
                    $"SUGGESTION: {guidance}\n\n" +
                    $"MANUAL FALLBACK: Create the project manually in ACC Admin Console, then restart.", ex);
            }
        }
        else
        {
            // Project already exists
            AccBootstrapLog.Info($"[AccBootstrap] Found existing project. ProjectId={projectId}");
            // When project already exists, we don't know for sure which platform created it
            _detectedPlatform = AccPlatform.Unknown;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step A2: ALWAYS assign ACC permissions to Dani (resolved from database)
        // This ensures the current user has permissions even if project already existed.
        // ═══════════════════════════════════════════════════════════════════════════════
        await EnsureDaniHasAccPermissionsAsync(projectId, cancellationToken);

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step B: Get root folder ID (with provisioning polling if project was just created)
        // ═══════════════════════════════════════════════════════════════════════════════
        AccBootstrapLog.Info($"[AccBootstrap] Getting root folder ('Project Files') for project...");
        var rootFolderId = await WaitForProjectProvisioningAsync(projectId, cancellationToken);

        if (string.IsNullOrEmpty(rootFolderId))
        {
            _finalDocsStatus = DocsStatus.NotProvisionedYet;
            _docsLastError = "Could not retrieve root folder. Project may not be fully provisioned.";
            throw new InvalidOperationException(
                $"Could not retrieve root folder for project '{_inboxProjectName}' (ID: {projectId}). " +
                "The project may not be fully provisioned. Please wait a few minutes and retry.");
        }

        _finalDocsStatus = DocsStatus.Ready;

        // Log Docs readiness confirmation
        AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");
        AccBootstrapLog.Info($"[AccBootstrap] DOCS READY - 'Project Files' folder exists!");
        AccBootstrapLog.Info($"[AccBootstrap]   RootFolderId: {rootFolderId}");
        AccBootstrapLog.Info($"[AccBootstrap]   ProjectWasCreated: {_projectWasCreated}");
        AccBootstrapLog.Info($"[AccBootstrap]   DetectedPlatform: {_detectedPlatform}");
        AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step C: Find or create _Inbox folder
        // ═══════════════════════════════════════════════════════════════════════════════
        AccBootstrapLog.Info($"[AccBootstrap] Looking for folder '{_inboxFolderName}' in root folder...");
        var inboxFolderId = await _bim360Service.GetFolderByNameAsync(projectId, rootFolderId, _inboxFolderName);

        if (string.IsNullOrEmpty(inboxFolderId))
        {
            // Folder doesn't exist - create it
            AccBootstrapLog.Info($"[AccBootstrap] Folder not found. Creating '{_inboxFolderName}'...");
            inboxFolderId = await _bim360Service.CreateFolderAsync(projectId, rootFolderId, _inboxFolderName);
            AccBootstrapLog.Info($"[AccBootstrap] Created new folder. InboxFolderId={inboxFolderId}");
        }
        else
        {
            AccBootstrapLog.Info($"[AccBootstrap] Found existing folder. InboxFolderId={inboxFolderId}");
        }

        return (projectId, rootFolderId, inboxFolderId);
    }

    // === TEMP DEV: Docs Provisioning Poll using ProbeDocsAsync ===
    /// <summary>
    /// Waits for Docs (Document Management) to be ready by polling with ProbeDocsAsync.
    /// New projects in ACC take time to fully provision their folder structure.
    /// 
    /// Uses the canonical ProbeDocsAsync method which works for both:
    /// - Legacy BIM 360 projects
    /// - ACC-native projects
    /// 
    /// Poll settings: 30 attempts × 5 seconds = 2.5 minutes max wait.
    /// </summary>
    /// <param name="projectId">The project ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The root folder ID once Docs is ready, or null if timeout.</returns>
    private async Task<string?> WaitForProjectProvisioningAsync(string projectId, CancellationToken cancellationToken)
    {
        AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");
        AccBootstrapLog.Info($"[AccBootstrap] Starting Docs readiness poll for project {projectId}");
        AccBootstrapLog.Info($"[AccBootstrap] Max attempts: {DocsProvisioningMaxAttempts}, Delay: {DocsProvisioningDelaySeconds}s");
        AccBootstrapLog.Info($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");

        MyOffice.AutodeskConnector.DocsProbeResult? lastProbeResult = null;

        for (int attempt = 1; attempt <= DocsProvisioningMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AccBootstrapLog.Info($"[AccBootstrap] Docs probe attempt {attempt}/{DocsProvisioningMaxAttempts}...");

            try
            {
                // Use the canonical ProbeDocsAsync method
                lastProbeResult = await _bim360Service.ProbeDocsAsync(_resolvedHubId!, projectId, cancellationToken);

                if (lastProbeResult.IsReady)
                {
                    AccBootstrapLog.Info($"[AccBootstrap] ✓ Docs READY! RootFolderId={lastProbeResult.RootFolderId}");
                    return lastProbeResult.RootFolderId;
                }

                // Log probe result
                AccBootstrapLog.Info($"[AccBootstrap] Docs not ready: {lastProbeResult.FailureReason} (HTTP {(int?)lastProbeResult.StatusCode})");

                // Check if this is a permanent failure (not worth retrying)
                if (lastProbeResult.FailureReason == "DocsDisabledOrNoAccess")
                {
                    AccBootstrapLog.Error($"[AccBootstrap] Docs is disabled or no access. This is a permanent failure - stopping poll.");
                    AccBootstrapLog.Error($"[AccBootstrap] To fix: Enable 'Document Management' for this project in ACC Admin Console.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                AccBootstrapLog.Warn($"[AccBootstrap] Attempt {attempt}: Error during docs probe: {ex.Message}");
            }

            if (attempt < DocsProvisioningMaxAttempts)
            {
                AccBootstrapLog.Info($"[AccBootstrap] Waiting {DocsProvisioningDelaySeconds}s before next attempt...");
                await Task.Delay(TimeSpan.FromSeconds(DocsProvisioningDelaySeconds), cancellationToken);
            }
        }

        // Timeout - log final state
        AccBootstrapLog.Error($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");
        AccBootstrapLog.Error($"[AccBootstrap] TIMEOUT waiting for Docs provisioning after {DocsProvisioningMaxAttempts} attempts.");
        if (lastProbeResult != null)
        {
            AccBootstrapLog.Error($"[AccBootstrap] Last probe result: {lastProbeResult.FailureReason}");
            if (!string.IsNullOrEmpty(lastProbeResult.RawResponseSnippet))
            {
                AccBootstrapLog.Error($"[AccBootstrap] Last response: {lastProbeResult.RawResponseSnippet}");
            }
        }
        AccBootstrapLog.Error($"[AccBootstrap] ═══════════════════════════════════════════════════════════════════");

        return null;
    }
    // === END TEMP DEV ===

    // ═══════════════════════════════════════════════════════════════════════════════
    // ACC PERMISSION ASSIGNMENT
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ensures Dani Israel has ACC project permissions.
    /// Uses the explicit admin email (danny@si-eng.co.il) for ACC permission assignment.
    /// 
    /// This is called ALWAYS after project is found/created to ensure permissions exist.
    /// The assignment API is idempotent - if already assigned, it won't fail.
    /// 
    /// Also ensures additional inbox members (like Lilach@si-eng.co.il) have view-only access.
    /// </summary>
    private async Task EnsureDaniHasAccPermissionsAsync(string projectId, CancellationToken cancellationToken)
    {
        AccBootstrapLog.Info($"[Bootstrap] ═══════════════════════════════════════════════════════════════════");
        AccBootstrapLog.Info($"[Bootstrap] AssignProjectPermissionsAsync START projectId={projectId}");
        AccBootstrapLog.Info($"[Bootstrap] ═══════════════════════════════════════════════════════════════════");

        try
        {
            // Step 1: Get the explicit admin email (always returns danny@si-eng.co.il)
            var adminEmail = await ResolveDaniEmailAsync(cancellationToken);
            AccBootstrapLog.Info($"[Bootstrap] Admin email resolved: {adminEmail}");

            // Step 2: Assign Project Admin permissions
            AccBootstrapLog.Info($"[Bootstrap] Calling AssignProjectAdminAsync for project {projectId}...");

            var success = await _bim360Service.AssignProjectAdminAsync(projectId, adminEmail, cancellationToken);

            if (success)
            {
                AccBootstrapLog.Info($"[Bootstrap] ✓ Permission assignment SUCCESS for {adminEmail}");
                _adminWasAssigned = true;
            }
            else
            {
                AccBootstrapLog.Warn($"[Bootstrap] Permission assignment returned false. User may already be assigned or API issue.");
                AccBootstrapLog.Warn($"[Bootstrap] This is not necessarily a failure - check ACC Admin Console to verify.");
                _adminWasAssigned = false;
            }

            // Step 3: Ensure additional inbox members have access
            var memberEmails = AccConstants.DefaultInboxMembers?.ToList() ?? new List<string>();
            AccBootstrapLog.Info($"[Bootstrap] Calling EnsureInboxMembersAsync projectId={projectId} members={string.Join(", ", memberEmails)}");
            await EnsureInboxMembersAsync(projectId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log detailed error but don't fail the entire bootstrap
            AccBootstrapLog.Error(ex, $"[Bootstrap] Permission assignment FAILED");
            AccBootstrapLog.Error($"[Bootstrap] Error: {ex.Message}");
            AccBootstrapLog.Warn($"[Bootstrap] Bootstrap will continue, but admin may not have access to create folders.");
            AccBootstrapLog.Warn($"[Bootstrap] Manual fix: Add danny@si-eng.co.il to the project in ACC Admin Console > Project Members");
            _adminWasAssigned = false;
        }

        AccBootstrapLog.Info($"[Bootstrap] AssignProjectPermissionsAsync END");
        AccBootstrapLog.Info($"[Bootstrap] ═══════════════════════════════════════════════════════════════════");
    }

    /// <summary>
    /// Ensures additional users have access to the Office Inbox project.
    /// These users get "viewer" access to Docs. The Office Inbox project is a
    /// system-controlled workspace: regular users can view files, while writes
    /// are reserved for the system/admin account assigned above.
    /// 
    /// This method:
    /// 1. Logs the BEFORE member state
    /// 2. Adds missing members
    /// 3. Logs the AFTER member state
    /// 4. Verifies each requested email is now present
    /// 
    /// Configurable via AccConstants.DefaultInboxMembers.
    /// </summary>
    private async Task EnsureInboxMembersAsync(string projectId, CancellationToken cancellationToken)
    {
        var memberEmails = AccConstants.DefaultInboxMembers?.ToList() ?? new List<string>();

        AccBootstrapLog.Info($"[Members] ════════════════════════════════════════════════════════════════════");
            AccBootstrapLog.Info($"[Members] START EnsureInboxMembers projectId={projectId} docsAccess=viewer requestedEmails={string.Join(", ", memberEmails)}");
        AccBootstrapLog.Info($"[Members] ════════════════════════════════════════════════════════════════════");

        if (memberEmails.Count == 0)
        {
            AccBootstrapLog.Info($"[Members] No additional inbox members configured. Skipping.");
            AccBootstrapLog.Info($"[Members] END EnsureInboxMembers added=0 already=0 failed=0");
            return;
        }

        try
        {
            // ══════════════════════════════════════════════════════════════════════════
            // STEP 1: Get BEFORE member list
            // ══════════════════════════════════════════════════════════════════════════
            AccBootstrapLog.Info($"[Members] Fetching BEFORE member list...");
            var beforeMembers = await _bim360Service.GetProjectUsersAsync(projectId, cancellationToken);
            var beforeEmails = beforeMembers.Select(u => u.Email).Where(e => !string.IsNullOrEmpty(e)).ToList();

            AccBootstrapLog.Info($"[Members] Before members count={beforeMembers.Count}");
            AccBootstrapLog.Info($"[Members] Before emails: {(beforeEmails.Count > 0 ? string.Join(", ", beforeEmails.Take(30)) : "(none)")}");
            if (beforeEmails.Count > 30)
            {
                AccBootstrapLog.Info($"[Members]   ...and {beforeEmails.Count - 30} more");
            }

            // Check which requested emails already exist
            var beforeEmailsSet = beforeEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var email in memberEmails)
            {
                var existsBefore = beforeEmailsSet.Contains(email);
                AccBootstrapLog.Info($"[Members]   {email} exists BEFORE = {existsBefore}");
            }

            // ══════════════════════════════════════════════════════════════════════════
            // STEP 2: Add missing members
            // ══════════════════════════════════════════════════════════════════════════
            AccBootstrapLog.Info($"[Members] Adding missing members...");
            var result = await _bim360Service.EnsureProjectMembersAsync(
                projectId,
                memberEmails,
                docsAccessLevel: "viewer",
                cancellationToken);

            // ══════════════════════════════════════════════════════════════════════════
            // STEP 3: Get AFTER member list to verify
            // ══════════════════════════════════════════════════════════════════════════
            AccBootstrapLog.Info($"[Members] Fetching AFTER member list to verify...");
            var afterMembers = await _bim360Service.GetProjectUsersAsync(projectId, cancellationToken);
            var afterEmails = afterMembers.Select(u => u.Email).Where(e => !string.IsNullOrEmpty(e)).ToList();

            AccBootstrapLog.Info($"[Members] After members count={afterMembers.Count}");

            // Verify each requested email is now present
            var afterEmailsSet = afterEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var email in memberEmails)
            {
                var existsAfter = afterEmailsSet.Contains(email);
                var symbol = existsAfter ? "✓" : "✗";
                AccBootstrapLog.Info($"[Members]   After contains {email} = {existsAfter} {symbol}");
            }

            // ══════════════════════════════════════════════════════════════════════════
            // SUMMARY
            // ══════════════════════════════════════════════════════════════════════════
            AccBootstrapLog.Info($"[Members] ════════════════════════════════════════════════════════════════════");
            AccBootstrapLog.Info($"[Members] END EnsureInboxMembers added={result.Added.Count} already={result.AlreadyExisted.Count} failed={result.Failed.Count}");

            if (result.Added.Count > 0)
            {
                AccBootstrapLog.Info($"[Members]   Added: {string.Join(", ", result.Added)}");
            }
            if (result.Failed.Count > 0)
            {
                AccBootstrapLog.Error($"[Members]   FAILURES:");
                foreach (var (email, error) in result.Failed)
                {
                    AccBootstrapLog.Error($"[Members]     ✗ {email}: {error}");
                }
            }
            AccBootstrapLog.Info($"[Members] ════════════════════════════════════════════════════════════════════");
        }
        catch (Exception ex)
        {
            AccBootstrapLog.Error(ex, $"[Members] EXCEPTION during EnsureInboxMembers");
            AccBootstrapLog.Error($"[Members] Exception type: {ex.GetType().Name}");
            AccBootstrapLog.Error($"[Members] Message: {ex.Message}");
            AccBootstrapLog.Warn($"[Members] Manual fix: Add members in ACC Admin Console > Project Members");
            AccBootstrapLog.Info($"[Members] END EnsureInboxMembers added=0 already=0 failed=EXCEPTION");
        }
    }

    /// <summary>
    /// Resolves the admin email for ACC permission assignment.
    /// 
    /// EXPLICIT OVERRIDE: Always returns "danny@si-eng.co.il" regardless of DB content.
    /// The DB lookup is for validation/logging only - it does NOT change the target email.
    /// 
    /// This is the admin account that must have ACC project permissions for folder creation.
    /// </summary>
    private async Task<string> ResolveDaniEmailAsync(CancellationToken cancellationToken)
    {
        // ═══════════════════════════════════════════════════════════════════════════════
        // CRITICAL: This exact email is required for ACC permission assignment.
        // Do NOT change this value or derive it from database lookups.
        // ═══════════════════════════════════════════════════════════════════════════════
        const string TargetEmail = "danny@si-eng.co.il";

        AccBootstrapLog.Info($"[AccBootstrap] Resolving admin email for ACC permissions...");
        AccBootstrapLog.Info($"[AccBootstrap] Target email (explicit): {TargetEmail}");

        // Validation: Check if user exists in Siusers table (for logging purposes only)
        var userInDb = await _dbContext.Siusers
            .FirstOrDefaultAsync(u =>
                u.Email != null &&
                u.Email.ToLower() == TargetEmail.ToLower(),
                cancellationToken);

        if (userInDb != null)
        {
            AccBootstrapLog.Info($"[AccBootstrap] ✓ User validated in Siusers: '{userInDb.Name}' (Id={userInDb.Id}, LoginName={userInDb.LoginName})");
        }
        else
        {
            // User not found in DB, but we still use the explicit email
            AccBootstrapLog.Warn($"[AccBootstrap] ⚠ Email '{TargetEmail}' not found in Siusers table.");
            AccBootstrapLog.Warn($"[AccBootstrap] ⚠ Proceeding with explicit override - this is the required admin email.");
        }

        AccBootstrapLog.Info($"[AccBootstrap] Assigning ACC permissions to: {TargetEmail}");
        return TargetEmail;
    }
}
