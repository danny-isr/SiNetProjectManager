namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Resolves whether ACC privileged operations should use the local path or the remote
/// <c>SiOffice.AccService</c> path.
/// </summary>
public interface IAccServiceModeProvider
{
    AccServiceMode Mode { get; }

    string? BaseUrl { get; }
}
