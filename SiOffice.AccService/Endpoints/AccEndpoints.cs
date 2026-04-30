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

        // ── Templates ───────────────────────────────────────────────────────
        v1.MapGet("/templates", async (
            IAccProjectProvisioningService svc,
            CancellationToken ct) =>
        {
            var list = await svc.ListAvailableTemplatesAsync(ct);
            return Results.Ok(list.Select(t => new AccTemplateDto(t.Id, t.Name)));
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
}
