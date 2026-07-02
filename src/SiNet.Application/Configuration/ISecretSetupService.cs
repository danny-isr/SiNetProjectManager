namespace SiNet.Application.Configuration;

/// <summary>Native Secret Setup port — vault read/write and post-save validation.</summary>
public interface ISecretSetupService
{
    Task<IReadOnlyList<SecretStatusDto>> GetStatusesAsync(CancellationToken cancellationToken = default);

    Task<SecretSetupSnapshotDto> GetEditableSnapshotAsync(CancellationToken cancellationToken = default);

    Task<SecretSaveResultDto> SaveAndValidateAsync(
        SecretSetupUpdateDto update,
        CancellationToken cancellationToken = default);
}
