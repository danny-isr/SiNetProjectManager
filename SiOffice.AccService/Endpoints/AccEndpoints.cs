using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNetSQL.Data;
using SiNetSQL.Services;
using SiNetSQL.Services.AccBootstrap;
using SiNetSQL.Services.AccBootstrap.Contracts;
using System.Text.Json;

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

        // ── Diagnostics (requires X-AccService-Key — see ApiKeyMiddleware) ───
        // Safe metadata for cross-machine API key and integration checks.
        // NEVER returns the actual key value, hash, or length.
        v1.MapGet("/diag", async (IConfiguration configuration, IDbContextFactory<SiNetSQLDbContext> dbContextFactory) =>
        {
            var apiKeyFromVault = CredentialVaultService.GetSecret(SecretKeys.AccServiceApiKey);
            var apiKeyFromConfig = configuration["AccService:ApiKey"];
            var effectiveKey = apiKeyFromVault ?? apiKeyFromConfig;
            var keySource = apiKeyFromVault != null ? "CredentialManager" : (apiKeyFromConfig != null ? "appsettings" : "none");

            // Active Autodesk Check
            var autodeskOk = false;
            string? autodeskDetail = null;
            var clientId = CredentialProvider.AutodeskClientId;
            var clientSecret = CredentialProvider.AutodeskClientSecret;

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                autodeskDetail = "Autodesk credentials not provisioned.";
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
                        autodeskDetail = "OK";
                    }
                    else
                    {
                        autodeskDetail = $"HTTP {(int)resp.StatusCode}";
                    }
                }
                catch (Exception ex)
                {
                    autodeskDetail = ex.GetType().Name;
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
                dbDetail = "OK";
            }
            catch (Exception ex)
            {
                dbDetail = ex.GetType().Name;
            }

            return Results.Ok(new
            {
                status = "ok",
                hasApiKey = effectiveKey != null,
                keySource,
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

        v1.MapGet("/projects/catalog", async (
            IDbContextFactory<SiNetSQLDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var mappedProjects = await db.ProjectAccMappings
                .AsNoTracking()
                .Where(mapping => mapping.AccProjectId != null && mapping.AccProjectId != string.Empty)
                .Select(mapping => new
                {
                    ProjectId = mapping.AccProjectId!,
                    DisplayName = mapping.AccProjectName,
                    SourceLabel = "ProjectAccMapping",
                    Priority = 0
                })
                .ToListAsync(ct);

            var systemProjects = await db.AccSystemResources
                .AsNoTracking()
                .Where(resource => resource.AccProjectId != null && resource.AccProjectId != string.Empty)
                .Select(resource => new
                {
                    ProjectId = resource.AccProjectId!,
                    DisplayName = (string?)resource.Key,
                    SourceLabel = "AccSystemResource",
                    Priority = 1
                })
                .ToListAsync(ct);

            var projects = mappedProjects
                .Concat(systemProjects)
                .Select(record => NormalizeProjectCatalogRecord(
                    record.ProjectId,
                    record.DisplayName,
                    record.SourceLabel,
                    record.Priority))
                .Where(static record => record is not null)
                .Cast<ProjectCatalogRecord>()
                .GroupBy(static record => record.ProjectId, StringComparer.OrdinalIgnoreCase)
                .Select(static group =>
                {
                    var best = group
                        .OrderBy(static record => record.Priority)
                        .ThenBy(static record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .First();

                    return new
                    {
                        best.ProjectId,
                        best.DisplayName,
                        best.SourceLabel
                    };
                })
                .OrderBy(static record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static record => record.ProjectId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Results.Ok(new { Projects = projects });
        });

        v1.MapGet("/live/hubs", async (
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            var hubs = await new Bim360Service(tokenProvider)
                .ListHubsAsync(ct);

            return Results.Ok(new
            {
                Hubs = hubs
                    .Where(static hub => !string.IsNullOrWhiteSpace(hub.Id))
                    .Select(static hub => new
                    {
                        HubId = hub.Id.Trim(),
                        DisplayName = string.IsNullOrWhiteSpace(hub.Name) ? hub.Id.Trim() : hub.Name.Trim(),
                        hub.Region
                    })
                    .OrderBy(static hub => hub.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static hub => hub.HubId, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
        });

        v1.MapGet("/live/hubs/{hubId}/projects", async (
            string hubId,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(hubId))
            {
                return Results.BadRequest(new { error = "hubId is required." });
            }

            var projects = await new Bim360Service(tokenProvider)
                .ListAccNativeProjectsAsync(hubId.Trim(), ct);

            return Results.Ok(new
            {
                Projects = projects
                    .Where(static project => !string.IsNullOrWhiteSpace(project.Id))
                    .Select(static project => new
                    {
                        ProjectId = project.Id.Trim(),
                        DisplayName = string.IsNullOrWhiteSpace(project.Name) ? project.Id.Trim() : project.Name.Trim()
                    })
                    .OrderBy(static project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
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

        // ── ACC file transfer ────────────────────────────────────────────────
        v1.MapPost("/projects/{projectId}/files/upload", async (
            string projectId,
            HttpRequest httpRequest,
            IAccFileUploadService uploadService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (!httpRequest.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data is required." });

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length <= 0)
                return Results.BadRequest(new { error = "file is required." });

            var requestJson = form["request"].ToString();
            if (string.IsNullOrWhiteSpace(requestJson))
                return Results.BadRequest(new { error = "request payload is required." });

            AccFileUploadEndpointRequest? body;
            try
            {
                body = JsonSerializer.Deserialize<AccFileUploadEndpointRequest>(requestJson, UploadRequestJsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"request payload is invalid JSON: {ex.Message}" });
            }

            if (body is null || string.IsNullOrWhiteSpace(body.DisplayName))
                return Results.BadRequest(new { error = "displayName is required." });
            if (string.IsNullOrWhiteSpace(body.TargetFolderId) && string.IsNullOrWhiteSpace(body.RootFolderId))
                return Results.BadRequest(new { error = "targetFolderId or rootFolderId is required." });

            var tempDirectory = Path.Combine(Path.GetTempPath(), "SiOffice.AccService", "uploads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var tempPath = Path.Combine(tempDirectory, SanitizeUploadFileName(file.FileName));

            try
            {
                await using (var targetStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true))
                {
                    await file.CopyToAsync(targetStream, ct);
                }

                var result = await uploadService.UploadAsync(
                    new AccFileUploadRequest(projectId.Trim(), tempPath, body.DisplayName.Trim())
                    {
                        TargetFolderId = string.IsNullOrWhiteSpace(body.TargetFolderId) ? null : body.TargetFolderId.Trim(),
                        RootFolderId = string.IsNullOrWhiteSpace(body.RootFolderId) ? null : body.RootFolderId.Trim(),
                        PathSegments = body.PathSegments ?? Array.Empty<string>(),
                        ExistingItemId = string.IsNullOrWhiteSpace(body.ExistingItemId) ? null : body.ExistingItemId.Trim(),
                        SourceIdentity = body.SourceIdentity,
                        Snapshot = body.Snapshot,
                        CompanionDocument = body.CompanionDocument,
                    },
                    ct);

                return Results.Ok(new
                {
                    result.FolderId,
                    result.ItemId,
                    result.VersionId,
                    result.FileName,
                    result.AlreadySameSource
                });
            }
            finally
            {
                TryDeleteTempDirectory(tempDirectory);
            }
        });

        v1.MapGet("/projects/{projectId}/items/{itemId}/download", async (
            string projectId,
            string itemId,
            HttpContext httpContext,
            IAccFileDownloadService downloadService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(itemId))
                return Results.BadRequest(new { error = "itemId is required." });

            var result = await downloadService.DownloadToTempAsync(projectId.Trim(), itemId.Trim(), ct);
            if (result is null || !File.Exists(result.TempFilePath))
            {
                return Results.NotFound();
            }

            var stream = new FileStream(
                result.TempFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

            // HTTP headers must be ASCII; percent-encode Hebrew/non-ASCII names.
            // RemoteAccFileDownloadService decodes with Uri.UnescapeDataString.
            var encodedFileName = Uri.EscapeDataString(result.DownloadedFileName ?? string.Empty);
            httpContext.Response.Headers["X-Acc-Downloaded-FileName"] = encodedFileName;
            httpContext.Response.OnCompleted(async () =>
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                TryDeleteTempFile(result.TempFilePath);
            });

            return Results.File(stream, "application/octet-stream", result.DownloadedFileName);
        });

        v1.MapGet("/projects/{projectId}/items/{itemId}/display-name", async (
            string projectId,
            string itemId,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(itemId))
                return Results.BadRequest(new { error = "itemId is required." });

            var bim360 = new Bim360Service(tokenProvider);
            var displayName = await bim360.GetItemDisplayNameAsync(NormalizeProjectId(projectId), itemId.Trim(), ct);
            return Results.Ok(new { DisplayName = displayName });
        });

        v1.MapGet("/projects/{projectId}/items/{itemId}/version-count", async (
            string projectId,
            string itemId,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(itemId))
                return Results.BadRequest(new { error = "itemId is required." });

            var bim360 = new Bim360Service(tokenProvider);
            var versionCount = await bim360.GetItemVersionCountAsync(NormalizeProjectId(projectId), itemId.Trim(), ct);
            return Results.Ok(new { VersionCount = versionCount });
        });

        v1.MapPost("/projects/{projectId}/items/{itemId}/hide", async (
            string projectId,
            string itemId,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(itemId))
                return Results.BadRequest(new { error = "itemId is required." });

            var bim360 = new Bim360Service(tokenProvider);
            var ok = await bim360.HideItemAsync(NormalizeProjectId(projectId), itemId.Trim(), ct);
            return Results.Ok(new { Ok = ok });
        });

        // ── ACC item custom attributes (metadata read) ──────────────────────
        // Privileged BIM 360 Docs custom-attribute read. Runs server-side so the
        // WPF client never needs Autodesk credentials — it forwards here via
        // RemoteAccItemMetadataService. Metadata-only: failures are reported in the
        // envelope, never surfaced as a missing-file signal.
        v1.MapGet("/projects/{projectId}/items/{itemId}/custom-attributes", async (
            string projectId,
            string itemId,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(itemId))
                return Results.BadRequest(new { error = "itemId is required." });

            var bim360 = new Bim360Service(tokenProvider);
            var result = await bim360.GetItemCustomAttributesAsync(NormalizeProjectId(projectId), itemId.Trim(), ct);
            return Results.Ok(new
            {
                result.Success,
                result.HttpStatus,
                result.ErrorMessage,
                Attributes = result.Value ?? new Dictionary<string, string?>(StringComparer.Ordinal)
            });
        });

        // ── ACC item custom attributes (metadata write) ─────────────────────
        // Version-scoped custom-attribute batch update. Definitions are per-folder,
        // so the caller supplies accFolderId + versionId. Same server-side privilege
        // boundary as the read endpoint above.
        v1.MapPost("/projects/{projectId}/items/{itemId}/custom-attributes", async (
            string projectId,
            string itemId,
            AccItemCustomAttributesWriteRequest body,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(itemId))
                return Results.BadRequest(new { error = "itemId is required." });
            if (body is null || string.IsNullOrWhiteSpace(body.AccFolderId))
                return Results.BadRequest(new { error = "accFolderId is required." });
            if (string.IsNullOrWhiteSpace(body.VersionId))
                return Results.BadRequest(new { error = "versionId is required." });

            var attributes = body.Attributes ?? new Dictionary<string, string?>(StringComparer.Ordinal);
            var bim360 = new Bim360Service(tokenProvider);
            var result = await bim360.SetItemCustomAttributesAsync(
                NormalizeProjectId(projectId),
                body.AccFolderId.Trim(),
                body.VersionId.Trim(),
                attributes,
                ct);
            return Results.Ok(new
            {
                result.Success,
                result.HttpStatus,
                result.ErrorMessage
            });
        });

        v1.MapPost("/projects/{projectId}/folders/resolve-path", async (
            string projectId,
            AccFolderPathEndpointRequest body,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (body is null || string.IsNullOrWhiteSpace(body.RootFolderId))
                return Results.BadRequest(new { error = "rootFolderId is required." });

            var normalizedProjectId = NormalizeProjectId(projectId);
            var bim360 = new Bim360Service(tokenProvider);
            var folderId = await TryResolveFolderPathAsync(
                bim360,
                normalizedProjectId,
                body.RootFolderId.Trim(),
                body.PathSegments ?? [],
                ct);

            return string.IsNullOrWhiteSpace(folderId)
                ? Results.NotFound()
                : Results.Ok(new { FolderId = folderId });
        });

        v1.MapPost("/projects/{projectId}/folders/ensure-path", async (
            string projectId,
            AccFolderPathEndpointRequest body,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (body is null || string.IsNullOrWhiteSpace(body.RootFolderId))
                return Results.BadRequest(new { error = "rootFolderId is required." });

            var normalizedProjectId = NormalizeProjectId(projectId);
            var bim360 = new Bim360Service(tokenProvider);
            var folderId = await bim360.EnsureFolderPathAsync(
                normalizedProjectId,
                body.RootFolderId.Trim(),
                NormalizePathSegments(body.PathSegments),
                ct);

            return Results.Ok(new { FolderId = folderId });
        });

        // ── Read-only ACC folder browse ──────────────────────────────────────
        v1.MapGet("/projects/{projectId}/folders/browse", async (
            string projectId,
            string? folderId,
            IDbContextFactory<SiNetSQLDbContext> dbFactory,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });

            var normalizedProjectId = NormalizeProjectId(projectId);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var resolvedFolderId = string.IsNullOrWhiteSpace(folderId)
                ? await ResolveProjectFilesRootFolderIdAsync(db, tokenProvider, normalizedProjectId, ct)
                : folderId.Trim();
            if (string.IsNullOrWhiteSpace(resolvedFolderId))
            {
                return Results.NotFound();
            }

            var bim360 = new Bim360Service(tokenProvider);
            var entries = await bim360.GetFolderContentsAsync(normalizedProjectId, resolvedFolderId, ct);

            return Results.Ok(new
            {
                ProjectId = normalizedProjectId,
                FolderId = resolvedFolderId,
                Entries = entries.Select(entry => new
                {
                    entry.Id,
                    entry.DisplayName,
                    Kind = entry.IsFolder ? 0 : 1,
                    entry.FileSize,
                    entry.LastModifiedTime,
                    entry.CreateTime
                }).ToArray()
            });
        });

        v1.MapGet("/projects/{projectId}/folders/search", async (
            string projectId,
            string fileName,
            string? folderId,
            IDbContextFactory<SiNetSQLDbContext> dbFactory,
            ITokenProvider tokenProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Results.BadRequest(new { error = "projectId is required." });
            if (string.IsNullOrWhiteSpace(fileName))
                return Results.BadRequest(new { error = "fileName is required." });

            var normalizedProjectId = NormalizeProjectId(projectId);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var resolvedFolderId = string.IsNullOrWhiteSpace(folderId)
                ? await ResolveProjectFilesRootFolderIdAsync(db, tokenProvider, normalizedProjectId, ct)
                : folderId.Trim();
            if (string.IsNullOrWhiteSpace(resolvedFolderId))
            {
                return Results.Ok(new
                {
                    Matches = Array.Empty<AccProjectTreeSearchMatchResponse>(),
                    VisitedFolderCount = 0,
                    HitFolderLimit = false,
                    HitResultLimit = false
                });
            }

            var bim360 = new Bim360Service(tokenProvider);
            var result = await SearchProjectTreeAsync(
                bim360,
                normalizedProjectId,
                resolvedFolderId,
                fileName.Trim(),
                string.IsNullOrWhiteSpace(folderId) ? "Project Files" : resolvedFolderId,
                ct);

            return Results.Ok(new
            {
                result.Matches,
                result.VisitedFolderCount,
                result.HitFolderLimit,
                result.HitResultLimit
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

    private static async Task<string?> ResolveProjectFilesRootFolderIdAsync(
        SiNetSQLDbContext db,
        ITokenProvider tokenProvider,
        string normalizedProjectId,
        CancellationToken ct)
    {
        var hubId = await ResolveHubIdAsync(db, normalizedProjectId, ct);
        if (string.IsNullOrWhiteSpace(hubId))
        {
            return null;
        }

        return await new Bim360Service(tokenProvider)
            .GetProjectRootFolderIdAsync(hubId, normalizedProjectId);
    }

    private static async Task<string?> ResolveHubIdAsync(
        SiNetSQLDbContext db,
        string normalizedProjectId,
        CancellationToken ct)
    {
        var mappedHubId = await db.ProjectAccMappings
            .AsNoTracking()
            .Where(mapping => mapping.AccProjectId != null && mapping.AccProjectId.Trim() == normalizedProjectId)
            .Join(
                db.AccHubs.AsNoTracking(),
                mapping => mapping.AccHubId,
                hub => hub.Id,
                (_, hub) => hub.HubId)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(mappedHubId))
        {
            return mappedHubId.Trim();
        }

        var systemHubId = await db.AccSystemResources
            .AsNoTracking()
            .Where(resource => resource.AccProjectId != null && resource.AccProjectId.Trim() == normalizedProjectId)
            .Join(
                db.AccHubs.AsNoTracking(),
                resource => resource.AccHubId,
                hub => hub.Id,
                (_, hub) => hub.HubId)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(systemHubId) ? null : systemHubId.Trim();
    }

    private static async Task<AccProjectTreeSearchResponse> SearchProjectTreeAsync(
        Bim360Service bim360,
        string projectId,
        string rootFolderId,
        string fileName,
        string rootFolderPath,
        CancellationToken ct)
    {
        const int maxTreeSearchFolders = 250;
        const int maxTreeSearchResults = 50;

        var pendingFolders = new Queue<AccTreeSearchLocation>();
        var visitedFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<AccProjectTreeSearchMatchResponse>();
        var visitedFolderCount = 0;
        pendingFolders.Enqueue(new AccTreeSearchLocation(rootFolderId, rootFolderPath));

        while (pendingFolders.Count > 0 && visitedFolderCount < maxTreeSearchFolders && matches.Count < maxTreeSearchResults)
        {
            ct.ThrowIfCancellationRequested();

            var currentLocation = pendingFolders.Dequeue();
            if (!visitedFolderIds.Add(currentLocation.FolderId))
            {
                continue;
            }

            visitedFolderCount++;
            var entries = await bim360.GetFolderContentsAsync(projectId, currentLocation.FolderId, ct);

            foreach (var entry in entries.Where(static entry => !entry.IsFolder))
            {
                if (!entry.DisplayName.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(new AccProjectTreeSearchMatchResponse(
                    projectId,
                    currentLocation.FolderId,
                    currentLocation.FolderPath,
                    entry.DisplayName));

                if (matches.Count >= maxTreeSearchResults)
                {
                    break;
                }
            }

            if (matches.Count >= maxTreeSearchResults || visitedFolderCount >= maxTreeSearchFolders)
            {
                break;
            }

            foreach (var entry in entries.Where(static entry => entry.IsFolder))
            {
                if (!visitedFolderIds.Contains(entry.Id))
                {
                    pendingFolders.Enqueue(new AccTreeSearchLocation(
                        entry.Id,
                        BuildChildPath(currentLocation.FolderPath, entry.DisplayName)));
                }
            }
        }

        return new AccProjectTreeSearchResponse(
            matches,
            visitedFolderCount,
            pendingFolders.Count > 0 && visitedFolderCount >= maxTreeSearchFolders,
            pendingFolders.Count > 0 && matches.Count >= maxTreeSearchResults);
    }

    private static string BuildChildPath(string parentPath, string folderName)
    {
        var normalizedFolderName = folderName.Trim();
        return string.IsNullOrWhiteSpace(parentPath)
            ? normalizedFolderName
            : $"{parentPath} / {normalizedFolderName}";
    }

    private static string NormalizeProjectId(string projectId)
    {
        var trimmed = projectId.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"b.{trimmed}";
    }

    private static IReadOnlyList<string> NormalizePathSegments(IReadOnlyList<string>? pathSegments) =>
        (pathSegments ?? [])
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .Select(static segment => segment.Trim())
            .ToArray();

    private static async Task<string?> TryResolveFolderPathAsync(
        Bim360Service bim360,
        string projectId,
        string rootFolderId,
        IReadOnlyList<string>? pathSegments,
        CancellationToken cancellationToken)
    {
        var currentFolderId = rootFolderId;
        foreach (var segment in NormalizePathSegments(pathSegments))
        {
            currentFolderId = await bim360.GetFolderByNameAsync(projectId, currentFolderId, segment).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(currentFolderId))
            {
                return null;
            }
        }

        return currentFolderId;
    }

    private static string SanitizeUploadFileName(string fileName)
    {
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "upload.bin" : fileName.Trim();
        return string.Join("_", safeFileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record AccItemCustomAttributesWriteRequest(
        string AccFolderId,
        string VersionId,
        Dictionary<string, string?>? Attributes);

    private sealed record AccTreeSearchLocation(string FolderId, string FolderPath);

    private sealed record AccProjectTreeSearchResponse(
        IReadOnlyList<AccProjectTreeSearchMatchResponse> Matches,
        int VisitedFolderCount,
        bool HitFolderLimit,
        bool HitResultLimit);

    private sealed record AccProjectTreeSearchMatchResponse(
        string ProjectId,
        string FolderId,
        string FolderPath,
        string FileName);

    private static ProjectCatalogRecord? NormalizeProjectCatalogRecord(
        string projectId,
        string? displayName,
        string sourceLabel,
        int priority)
    {
        var trimmedProjectId = projectId.Trim();
        if (trimmedProjectId.Length == 0)
        {
            return null;
        }

        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? trimmedProjectId
            : priority == 1
                ? $"System: {displayName.Trim()}"
                : displayName.Trim();

        return new ProjectCatalogRecord(trimmedProjectId, normalizedDisplayName, sourceLabel, priority);
    }

    private sealed record ProjectCatalogRecord(
        string ProjectId,
        string DisplayName,
        string SourceLabel,
        int Priority);

    private sealed record AccFolderPathEndpointRequest(
        string RootFolderId,
        IReadOnlyList<string>? PathSegments);

    private sealed record AccFileUploadEndpointRequest(
        string? TargetFolderId,
        string? RootFolderId,
        IReadOnlyList<string>? PathSegments,
        string DisplayName,
        string? ExistingItemId,
        AccFileSourceIdentity? SourceIdentity,
        AccFileUploadSnapshot? Snapshot,
        AccFileUploadCompanionDocument? CompanionDocument);

    private static readonly JsonSerializerOptions UploadRequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
