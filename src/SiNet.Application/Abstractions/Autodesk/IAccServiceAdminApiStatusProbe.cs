namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Read-only Autodesk Account Admin API probe using the AccService Admin 3-legged token.
/// Returns a compact status string for identity health (never tokens).
/// </summary>
public interface IAccServiceAdminApiStatusProbe
{
    /// <summary>
    /// Probes <c>GET construction/admin/v1/accounts/{accountId}/projects?limit=1</c>.
    /// Returns <c>200</c> / <c>403</c> / <c>unavailable:…</c>.
    /// </summary>
    Task<string> ProbeAsync(CancellationToken cancellationToken = default);
}
