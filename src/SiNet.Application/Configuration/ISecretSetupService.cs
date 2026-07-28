namespace SiNet.Application.Configuration;

/// <summary>Native Secret Setup port — vault read/write and post-save validation.</summary>
public interface ISecretSetupService
{
    Task<IReadOnlyList<SecretStatusDto>> GetStatusesAsync(CancellationToken cancellationToken = default);

    Task<SecretSetupSnapshotDto> GetEditableSnapshotAsync(CancellationToken cancellationToken = default);

    Task<SecretSaveResultDto> SaveAndValidateAsync(
        SecretSetupUpdateDto update,
        CancellationToken cancellationToken = default);

    Task<SecretExportResultDto> ExportAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default);

    Task<SecretImportPreviewDto> PreviewImportAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default);

    Task<SecretImportResultDto> ImportAsync(
        string filePath,
        string password,
        bool overwrite,
        CancellationToken cancellationToken = default);

    Task<string> GenerateAccServiceApiKeyAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateAccServiceCertificatePasswordAsync(CancellationToken cancellationToken = default);

    Task<AccServiceDiagnosticResultDto> TestAccServiceAsync(CancellationToken cancellationToken = default);
}
