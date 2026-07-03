namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Read-only ACC hub/account entry for live project discovery.
/// </summary>
public sealed record AccHubCatalogEntry(
    string HubId,
    string DisplayName,
    string? Region)
{
    public string DisplayText =>
        string.IsNullOrWhiteSpace(Region)
            ? $"{DisplayName.Trim()} ({HubId.Trim()})"
            : $"{DisplayName.Trim()} [{Region.Trim()}] ({HubId.Trim()})";
}
