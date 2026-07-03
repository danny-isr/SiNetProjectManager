using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNetSQL.Data;
using SiNetSQL.Services;
using SiNetSQL.Services.AccBootstrap;
using SiNetSQL.Services.AccBootstrap.Contracts;

namespace SiOffice.AccService.Endpoints;

/// <summary>
/// Maps every privileged-operations HTTP endpoint exposed by SiOffice.AccService.
/// Each endpoint is a thin adapter that deserializes the request, delegates to
/// <see cref="IAccProjectProvisioningService"/>, and serializes the result.
/// </summary>
internal static class AccEndpoints
{
    public static void MapAccEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup(AccServiceContracts.ApiVersionPrefix + "/acc");

        // ── Health (auth-exempt — see ApiKeyMiddleware) ─────────────────────
        // `apiVersion` is the API CONTRACT version (matches the route prefix).
        // `buildVersion` is the assembly version of the running service binary —
        // useful for verifying which build is actually deployed on the server.
        v1.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            apiVersion = "1.0",
            buildVersion = typeof(AccEndpoints).Assembly.GetName().Version?.ToString() ?? "?",
            utcNow = DateTime.UtcNow
        }));

        // ── Diagnostics (auth-exempt — see ApiKeyMiddleware) ─────────────────
        // Safe metadata for cross-machine API key mismatch debugging.
        // NEVER returns the actual key value. Returns: hasApiKey, keyLength, keySource, keyHashPrefix.
        // Clients can compare their keyHashPrefix with this to verify key match.
        // Also performs active database and Autodesk connectivity checks.
        v1.MapGet("/diag", async (IConfiguration configuration, IDbContextFactory<SiNetSQLDbContext> dbContextFactory) =>
        {
            var apiKeyFromVault = CredentialVaultService.GetSecret(SecretKeys.AccServiceApiKey);
            var apiKeyFromConfig = configuration["AccService:ApiKey"];
            var effectiveKey = apiKeyFromVault ?? apiKeyFromConfig;
            var keySource = apiKeyFromVault != null ? "CredentialManager" : (apiKeyFromConfig != null ? "appsettings" : "none");
            var keyLength = effectiveKey?.Length ?? 0;
            var keyHashPrefix = "(none)";
            if (!string.IsNullOrEmpty(effectiveKey))
            {
                var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(effectiveKey));
                keyHashPrefix = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
            }
            string windowsUser;
            try { windowsUser = Environment.UserDomainName + "\\" + Environment.UserName; }
            catch { windowsUser = "(unknown)"; }

            // Active Autodesk Check
            var autodeskOk = false;
            string? autodeskDetail = null;
            var clientId = CredentialProvider.AutodeskClientId;
            var clientSecret = CredentialProvider.AutodeskClientSecret;

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                autodeskDetail = "Autodesk credentials are not provisioned in the server vault.";
            }
            else
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var authBytes = System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}");
                    using var req = new HttpRequestMessage(HttpMethod.Post, "https://developer.api.autodesk.com/authentication/v2/token");
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                    req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["scope"] = "data:read"
                    });
                    var resp = await client.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        autodeskOk = true;
                        autodeskDetail = "Autodesk token retrieved successfully.";
                    }
                    else
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        autodeskDetail = $"HTTP {(int)resp.StatusCode}: {body}";
                    }
                }
                catch (Exception ex)
                {
                    autodeskDetail = ex.Message;
                }
            }

            // Active Database Check
            var dbOk = false;
            string? dbDetail = null;
            try
            {
                using var db = dbContextFactory.CreateDbContext();
                await db.Database.OpenConnectionAsync();
                await db.Database.CloseConnectionAsync();
                dbOk = true;
                dbDetail = "Database connection successful.";
            }
            catch (Exception ex)
            {
                dbDetail = ex.Message;
            }

            return Results.Ok(new
            {
                status = "ok",
                windowsUser,
                hasApiKey = effectiveKey != null,
                keySource,
                keyLength,
                keyHashPrefix,
                autodeskStatus = autodeskOk,
                autodeskDetail,
                dbStatus = dbOk,
                dbDetail,
                buildVersion = typeof(AccEndpoints).Assembly.GetName().Version?.ToString() ?? "?",
                utcNow = DateTime.UtcNow
            });
        });

        // ── Templates ───────────────────────────────────────────────────────
        v1.MapGet("/templates", async (
            IAccProjectProvisioningService svc,
            CancellationToken ct) =>
        {
            var list = await svc.ListAvailableTemplatesAsync(ct);
            return Results.Ok(list.Select(t => new AccTemplateDto(t.Id, t.Name)));
        });

        // ── Read-only ACC project discovery ─────────────────────────────────
        v1.MapGet("/projects/ids", async (
            IDbContextFactory<SiNetSQLDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var mappedProjectIds = await db.ProjectAccMappings
                .AsNoTracking()
                .Where(mapping => mapping.AccProjectId != null && mapping.AccProjectId != string.Empty)
                .Select(mapping => mapping.AccProjectId!)
                .ToListAsync(ct);

            var systemProjectIds = await db.AccSystemResources
                .AsNoTracking()
                .Where(resource => resource.AccProjectId != null && resource.AccProjectId != string.Empty)
                .Select(resource => resource.AccProjectId!)
                .ToListAsync(ct);

            var projectIds = mappedProjectIds
                .Concat(systemProjectIds)
                .Select(projectId => projectId.Trim())
                .Where(projectId => projectId.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(projectId => projectId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Results.Ok(new { ProjectIds = projectIds });
        });

        // ── Read-only ACC item lookup ───────────────────────────────────────
        v1.MapGet("/projects/{projectId}/folders/{folderId}/items/resolve", async (
            string projectId,
            string folderId,
            string fileName,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(folderId))
                return Results.BadRequest(new { error = "folderId is required." });
            if (string.IsNullOrWhiteSpace(fileName))
                return Results.BadRequest(new { error = "fileName is required." });

            var bim360 = new Bim360Service(tokenProvider);
            var items = await bim360.GetFolderItemsAsync(projectId, folderId, ct);
            var match = SelectFolderItem(items, fileName);
            if (match is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                ProjectId = projectId,
                ItemId = match.ItemId,
                VersionId = (string?)null,
                ViewerUrl = (string?)null
            });
        });

        // ── Project mapping (find-or-create + provision + folder tree) ──────
        v1.MapPost("/projects/ensure-mapping", async (
            EnsureProjectMappingRequest body,
            IAccProjectProvisioningService svc,
            CancellationToken ct) =>
        {
            if (body is null || body.SiProjectId <= 0)
                return Results.BadRequest(new ErrorDto("siProjectId is required and must be > 0."));

            var targets = await svc.EnsureProjectMappingAsync(body.SiProjectId, ct);
            return Results.Ok(targets);
        });

        // ── Reconcile members of a single ACC project ───────────────────────
        v1.MapPost("/projects/{accProjectId}/members/reconcile", async (
            string accProjectId,
            IAccProjectProvisioningService svc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(accProjectId))
                return Results.BadRequest(new ErrorDto("accProjectId is required."));

            await svc.ReconcileProjectMembersAsync(accProjectId, ct);
            return Results.NoContent();
        });

        // ── Reconcile across every mapped ACC project (returns text summary) ─
        v1.MapPost("/projects/reconcile-all", async (
            IAccProjectProvisioningService svc,
            CancellationToken ct) =>
        {
            var summary = await svc.ReconcileAllProjectsAsync(ct);
            return Results.Ok(new SummaryDto(summary));
        });

        // ── Ensure SiNet custom-attribute definitions on a project ──────────
        v1.MapPost("/projects/{accProjectId}/attribute-defs/ensure", async (
            string accProjectId,
            EnsureAttributeDefsRequest body,
            IAccProjectProvisioningService svc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(accProjectId))
                return Results.BadRequest(new ErrorDto("accProjectId is required."));
            if (body is null || string.IsNullOrWhiteSpace(body.AccFolderId))
                return Results.BadRequest(new ErrorDto("accFolderId is required."));

            var ok = await svc.EnsureCustomAttributeDefinitionsAsync(
                accProjectId, body.AccFolderId, body.SiProjectId, ct);
            return Results.Ok(new BoolResultDto(ok));
        });

        // ── Office Inbox: ensure project + _Inbox folder + member access ────
        v1.MapPost("/inbox/ensure", async (
            EnsureInboxRequest? body,
            IDbContextFactory<SiNetSQLDbContext> dbFactory,
            SystemSettingsService settings,
            HttpContext http,
            CancellationToken ct) =>
        {
            body ??= new EnsureInboxRequest();

            var clientId = CredentialProvider.AutodeskClientId;
            var clientSecret = CredentialProvider.AutodeskClientSecret;
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                return Results.BadRequest(new ErrorDto(
                    "Autodesk credentials are not provisioned in the service vault."));

            var projectName = body.ProjectName
                ?? await settings.GetOrDefaultAsync(SystemSettingKeys.InboxProjectName, "מיילים למשרד - POC 4");
            var folderName = body.FolderName
                ?? await settings.GetOrDefaultAsync(SystemSettingKeys.InboxFolderName, "_Inbox");
            var templateName = body.TemplateName
                ?? await settings.GetAsync(SystemSettingKeys.AccProjectTemplateName);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var tokenProvider = new TokenProvider(clientId, clientSecret);
            var bim360 = new Bim360Service(tokenProvider);

            var bootstrap = new AccBootstrapService(
                db, bim360,
                inboxProjectName: projectName,
                inboxFolderName: folderName,
                forceCreateProject: true,
                createPlatform: CreateProjectPlatform.AccNative,
                bootstrapAdminEmail: body.AdminEmail ?? string.Empty,
                dryRun: body.DryRun,
                templateName: templateName);

            var caller = http.User?.Identity?.Name ?? "AccService";
            try
            {
                var targets = await bootstrap.EnsureOfficeInboxAsync(caller, ct);
                return Results.Ok(new EnsureInboxResponse(
                    targets.AccHubDbId,
                    targets.HubId,
                    targets.AccProjectId,
                    targets.AccRootFolderId,
                    targets.AccInboxFolderId));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorDto("Inbox bootstrap failed.", ex.Message));
            }
        });
    }

    private static AccFolderItem? SelectFolderItem(IEnumerable<AccFolderItem> items, string fileName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(fileName);

        return items.FirstOrDefault(item =>
                   string.Equals(item.DisplayName, fileName, StringComparison.Ordinal))
            ?? items.FirstOrDefault(item =>
                   string.Equals(item.DisplayName, fileName, StringComparison.OrdinalIgnoreCase));
    }
}
