namespace SiNet.Application.Configuration;

/// <summary>
/// Materializes Google OAuth client secrets from the vault to a secure per-user file for consumers that still need a path.
/// Vault (<see cref="SecretCatalog.GoogleClientSecrets"/>) is the single source of truth.
/// </summary>
public interface IGoogleClientSecretsMaterializer
{
    Task<GoogleClientSecretsMaterializationResult> MaterializeClientSecretsFileAsync(
        CancellationToken cancellationToken = default);
}

public sealed record GoogleClientSecretsMaterializationResult(
    string? FilePath,
    bool UsedVault,
    bool UsedFallback,
    string? FallbackWarning);

/// <summary>Resolves the effective Google client secrets file path (vault-first).</summary>
public interface IGoogleClientSecretsPathProvider
{
    Task<string?> ResolveClientSecretsPathAsync(CancellationToken cancellationToken = default);
}

public sealed record GoogleClientSecretsFallbackOptions
{
    public string? GmailClientSecretsPath { get; init; }

    public string? GoogleReportsClientSecretsPath { get; init; }
}
