namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>Reads safe runtime diagnostics from the remote <c>SiOffice.AccService</c> endpoint.</summary>
public interface IAccServiceDiagnosticsProbe
{
    Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default);
}
