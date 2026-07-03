namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>Probes the remote <c>SiOffice.AccService</c> health endpoint.</summary>
public interface IAccServiceHealthProbe
{
    Task<AccServiceHealthResult> CheckAsync(CancellationToken cancellationToken = default);
}
