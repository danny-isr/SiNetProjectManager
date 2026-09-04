namespace SiNet.Application.Identity;

/// <summary>Resolves AccProjectId from SiNet ProjectId via ProjectAccMapping (SQL helper only).</summary>
public interface IAccProjectIdResolver
{
    Task<string?> ResolveAccProjectIdAsync(int siProjectId, CancellationToken cancellationToken = default);
}
