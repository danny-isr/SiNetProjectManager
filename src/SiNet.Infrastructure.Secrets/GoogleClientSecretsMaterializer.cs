using System.Diagnostics;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// Writes vault Google JSON to <c>%LocalAppData%/SiNet/Secrets/google-client-secrets.json</c>.
/// </summary>
public sealed class GoogleClientSecretsMaterializer(ISecretVaultStore vault) : IGoogleClientSecretsMaterializer
{
    private readonly ISecretVaultStore _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    private string? _cachedPath;

    public Task<GoogleClientSecretsMaterializationResult> MaterializeClientSecretsFileAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_vault.HasSecret(SecretCatalog.GoogleClientSecrets))
        {
            return Task.FromResult(new GoogleClientSecretsMaterializationResult(
                null, UsedVault: false, UsedFallback: false, null));
        }

        var json = _vault.GetSecret(SecretCatalog.GoogleClientSecrets)!;
        var (success, detail) = GoogleClientSecretsValidator.ValidateJsonContent(json);
        if (!success)
        {
            throw new InvalidOperationException($"Google client secrets in vault are invalid: {detail}");
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiNet",
            "Secrets");
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, "google-client-secrets.json");
        File.WriteAllText(filePath, json);
        _cachedPath = filePath;

        return Task.FromResult(new GoogleClientSecretsMaterializationResult(
            filePath,
            UsedVault: true,
            UsedFallback: false,
            null));
    }
}

/// <summary>Vault-first Google client secrets path with optional config fallback (logged as warning).</summary>
public sealed class VaultGoogleClientSecretsPathProvider(
    ISecretVaultStore vault,
    IGoogleClientSecretsMaterializer materializer,
    GoogleClientSecretsFallbackOptions? fallbackOptions = null) : IGoogleClientSecretsPathProvider
{
    private readonly ISecretVaultStore _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    private readonly IGoogleClientSecretsMaterializer _materializer =
        materializer ?? throw new ArgumentNullException(nameof(materializer));
    private readonly GoogleClientSecretsFallbackOptions _fallback = fallbackOptions ?? new GoogleClientSecretsFallbackOptions();

    public async Task<string?> ResolveClientSecretsPathAsync(CancellationToken cancellationToken = default)
    {
        if (_vault.HasSecret(SecretCatalog.GoogleClientSecrets))
        {
            var result = await _materializer.MaterializeClientSecretsFileAsync(cancellationToken)
                .ConfigureAwait(false);
            return result.FilePath;
        }

        foreach (var candidate in new[] { _fallback.GmailClientSecretsPath, _fallback.GoogleReportsClientSecretsPath })
        {
            if (TryResolveExistingFile(candidate, out var path))
            {
                Debug.WriteLine(
                    "[GoogleClientSecrets] WARNING: Using config fallback for client secrets. " +
                    "Configure Google OAuth in Secret Setup (Vault) to remove this fallback.");
                return path;
            }
        }

        return null;
    }

    internal static bool TryResolveExistingFile(string? configuredPath, out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

        return File.Exists(fullPath);
    }
}
