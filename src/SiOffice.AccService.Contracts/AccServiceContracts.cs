namespace SiOffice.AccService.Contracts;

/// <summary>
/// Wire DTOs for the SiOffice.AccService privileged-operations HTTP API.
/// Shared by the server (<c>SiOffice.AccService</c>), V2 remote clients, and
/// <c>SiNet.Infrastructure.Autodesk</c> remote adapters.
/// </summary>
/// <remarks>
/// IMPORTANT: any breaking change to these records must bump the API version
/// prefix (currently <c>/v1/</c>) on the server.
/// </remarks>
public static class AccServiceContracts
{
    public const string ApiVersionPrefix = "/v1";
    public const string ApiKeyHeader = "X-AccService-Key";
    public const string ApiVersionHeader = "X-AccService-Version";
}

/// <summary>One entry in the response of <c>GET /v1/acc/templates</c>.</summary>
public sealed record AccTemplateDto(string Id, string Name);

/// <summary>Request body for <c>POST /v1/acc/projects/ensure-mapping</c>.</summary>
public sealed record EnsureProjectMappingRequest(int SiProjectId);

/// <summary>Request body for <c>POST /v1/acc/projects/{accProjectId}/attribute-defs/ensure</c>.</summary>
public sealed record EnsureAttributeDefsRequest(string AccFolderId, int? SiProjectId);

/// <summary>Response wrapper for endpoints that return only a boolean outcome.</summary>
public sealed record BoolResultDto(bool Success);

/// <summary>Response wrapper for endpoints that return a free-text summary line.</summary>
public sealed record SummaryDto(string Summary);

/// <summary>Standard error body emitted by the service for non-2xx responses.</summary>
public sealed record ErrorDto(string Error, string? Detail = null);

/// <summary>
/// Optional overrides for <c>POST /v1/acc/inbox/ensure</c>. All fields are optional;
/// missing values fall back to <c>SystemSettings</c> rows or hard-coded defaults
/// (matching the in-process behavior of <c>EmailIngestionServiceFactory</c>).
/// </summary>
public sealed record EnsureInboxRequest(
    string? ProjectName = null,
    string? FolderName = null,
    string? TemplateName = null,
    string? AdminEmail = null,
    bool DryRun = false);

/// <summary>Resolved ACC identifiers for the Office Inbox after ensure.</summary>
public sealed record EnsureInboxResponse(
    int AccHubDbId,
    string HubId,
    string AccProjectId,
    string AccRootFolderId,
    string AccInboxFolderId);
