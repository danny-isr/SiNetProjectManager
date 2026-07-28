using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// Native Secret Setup service backed by Windows Credential Manager.
/// Google OAuth JSON is stored as vault content under <see cref="SecretCatalog.GoogleClientSecrets"/> — not as a file path.
/// </summary>
public sealed class CredentialVaultSecretSetupService(
    ISecretVaultStore vault,
    ISecretSetupHostConfiguration hostConfiguration) : ISecretSetupService
{
    private readonly ISecretVaultStore _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    private readonly ISecretSetupHostConfiguration _hostConfiguration =
        hostConfiguration ?? throw new ArgumentNullException(nameof(hostConfiguration));

    public async Task<IReadOnlyList<SecretStatusDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        var validationResults = await BuildValidationResultsAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<SecretStatusDto>(SecretCatalog.All.Count);

        foreach (var entry in SecretCatalog.All)
        {
            list.Add(ResolveEntryStatus(entry, validationResults));
        }

        return list;
    }

    public Task<SecretSetupSnapshotDto> GetEditableSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = _vault.GetVaultStatus();
        var prefill = new Dictionary<string, string?>();

        foreach (var entry in SecretCatalog.All)
        {
            if (!entry.CanPrefill || entry.Kind is SecretKind.Password or SecretKind.JsonFile)
            {
                continue;
            }

            prefill[entry.Key] = _vault.GetSecret(entry.Key);
        }

        var googleDisplay = status.GetValueOrDefault(SecretCatalog.GoogleClientSecrets)
            ? "(מוגדר ב-Vault)"
            : string.Empty;

        return Task.FromResult(new SecretSetupSnapshotDto(
            prefill,
            status,
            googleDisplay));
    }

    public async Task<SecretSaveResultDto> SaveAndValidateAsync(
        SecretSetupUpdateDto update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var saved = 0;

        foreach (var (key, value) in update.Updates)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!SecretCatalog.AllKeys.Contains(key))
            {
                continue;
            }

            var entry = SecretCatalog.All.First(e => e.Key == key);
            var normalized = entry.Kind == SecretKind.ConnectionString
                ? ConnectionStringNormalizer.Normalize(value)
                : value.Trim();

            _vault.SetSecret(key, normalized);
            saved++;
        }

        var validationResults = await BuildValidationResultsAsync(cancellationToken).ConfigureAwait(false);
        var passed = new List<string>();
        var failed = new List<string>();

        foreach (var result in validationResults)
        {
            if (!result.Exists)
            {
                if (result.Detail is not null)
                {
                    failed.Add($"{result.Label} — {result.Detail}");
                }

                continue;
            }

            if (result.Success)
            {
                passed.Add($"{result.Label}{(result.Detail != null ? $" — ({result.Detail})" : string.Empty)}");
            }
            else
            {
                failed.Add($"{result.Label} — {result.Detail}");
            }
        }

        return new SecretSaveResultDto(
            saved,
            validationResults,
            failed.Count == 0,
            passed,
            failed);
    }

    private async Task<IReadOnlyList<SecretValidationResultDto>> BuildValidationResultsAsync(
        CancellationToken cancellationToken)
    {
        var connSiNet = await Task.Run(
            () => SecretSetupValidators.TestDatabaseFromVault(_vault, SecretCatalog.SiNetDatabase),
            cancellationToken).ConfigureAwait(false);
        var connReplica = await Task.Run(
            () => SecretSetupValidators.TestDatabaseFromVault(_vault, SecretCatalog.ReplicaDatabase),
            cancellationToken).ConfigureAwait(false);
        var connMasterPlan = await Task.Run(
            () => SecretSetupValidators.TestDatabaseFromVault(_vault, SecretCatalog.MasterPlanDatabase),
            cancellationToken).ConfigureAwait(false);
        var gemini = await SecretSetupValidators.TestGeminiFromVaultAsync(_vault, cancellationToken).ConfigureAwait(false);
        var autodesk = await SecretSetupValidators.TestAutodeskFromVaultAsync(_vault, cancellationToken).ConfigureAwait(false);
        var google = SecretSetupValidators.TestGoogleFromVault(_vault);
        var ad = await Task.Run(
            () => SecretSetupValidators.TestAdFromVault(_vault, _hostConfiguration),
            cancellationToken).ConfigureAwait(false);
        var accServiceDiag = await AccServiceSecretDiagnostics.TestAsync(
                _vault,
                _hostConfiguration,
                _hostConfiguration.AccServicePinnedCertificateThumbprints,
                cancellationToken)
            .ConfigureAwait(false);
        var accCertPassword = SecretSetupValidators.TestPresenceOnly(
            _vault,
            SecretCatalog.AccServiceCertificatePassword);
        var masterPlanApi = SecretSetupValidators.TestPresenceOnly(_vault, SecretCatalog.MasterPlanApiKey);

        return
        [
            ToResult(SecretCatalog.SiNetDatabase, "SiNet DB", connSiNet.Exists, connSiNet.Success, connSiNet.Detail),
            ToResult(SecretCatalog.ReplicaDatabase, "Replica DB", connReplica.Exists, connReplica.Success, connReplica.Detail),
            ToResult(SecretCatalog.MasterPlanDatabase, "MasterPlan DB", connMasterPlan.Exists, connMasterPlan.Success, connMasterPlan.Detail),
            ToResult(SecretCatalog.GeminiApiKey, "Gemini API", gemini.Exists, gemini.Success, gemini.Detail),
            ToResult(SecretCatalog.GoogleClientSecrets, "Google OAuth", google.Exists, google.Success, google.Detail),
            ToPairResult("Autodesk APS", autodesk.BothExist, autodesk.Success, autodesk.Detail,
                SecretCatalog.AutodeskClientId, SecretCatalog.AutodeskClientSecret),
            ToPairResult("Active Directory", ad.BothExist, ad.Success, ad.Detail,
                SecretCatalog.AdUsername, SecretCatalog.AdPassword),
            ToResult(
                SecretCatalog.AccServiceApiKey,
                "AccService API Key",
                _vault.HasSecret(SecretCatalog.AccServiceApiKey),
                accServiceDiag.Success,
                accServiceDiag.Detail),
            ToResult(
                SecretCatalog.AccServiceCertificatePassword,
                "AccService Certificate Password",
                accCertPassword.Exists,
                accCertPassword.Success,
                accCertPassword.Detail),
            ToResult(SecretCatalog.MasterPlanApiKey, "MasterPlan API Key", masterPlanApi.Exists, masterPlanApi.Success, masterPlanApi.Detail),
        ];
    }

    public Task<SecretExportResultDto> ExportAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = SecretProvisioningFileService.ExportToFile(_vault, filePath, password);
        return Task.FromResult(new SecretExportResultDto(
            count,
            $"יוצאו {count} מפתחות לקובץ מוצפן (.secrets)."));
    }

    public Task<SecretImportPreviewDto> PreviewImportAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decrypted = SecretProvisioningFileService.DecryptSecrets(filePath, password);
        return Task.FromResult(BuildImportPreview(decrypted));
    }

    public Task<SecretImportResultDto> ImportAsync(
        string filePath,
        string password,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decrypted = SecretProvisioningFileService.DecryptSecrets(filePath, password);
        var catalogKeys = SecretCatalog.AllKeys.ToHashSet(StringComparer.Ordinal);
        var imported = 0;
        var skipped = 0;
        var skippedSummaries = new List<string>();

        foreach (var (key, value) in decrypted)
        {
            if (!catalogKeys.Contains(key))
            {
                skipped++;
                skippedSummaries.Add($"דולג key לא מוכר: {key}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                skipped++;
                skippedSummaries.Add($"דולג {key}: ערך ריק");
                continue;
            }

            if (_vault.HasSecret(key) && !overwrite)
            {
                skipped++;
                skippedSummaries.Add($"דולג {key}: כבר קיים ב-Vault (overwrite=false)");
                continue;
            }

            var entry = SecretCatalog.All.First(e => e.Key == key);
            var normalized = entry.Kind == SecretKind.ConnectionString
                ? ConnectionStringNormalizer.Normalize(value)
                : value.Trim();

            _vault.SetSecret(key, normalized);
            imported++;
        }

        return Task.FromResult(new SecretImportResultDto(
            imported,
            skipped,
            skippedSummaries,
            $"יובאו {imported} מפתחות, דולגו {skipped}."));
    }

    public Task<string> GenerateAccServiceApiKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = AccServiceSecretDiagnostics.GenerateApiKey();
        _vault.SetSecret(SecretCatalog.AccServiceApiKey, key);
        return Task.FromResult(key);
    }

    public Task<string> GenerateAccServiceCertificatePasswordAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var password = AccServiceSecretDiagnostics.GenerateCertificatePassword();
        _vault.SetSecret(SecretCatalog.AccServiceCertificatePassword, password);
        return Task.FromResult(password);
    }

    public Task<AccServiceDiagnosticResultDto> TestAccServiceAsync(CancellationToken cancellationToken = default)
        => AccServiceSecretDiagnostics.TestAsync(
            _vault,
            _hostConfiguration,
            _hostConfiguration.AccServicePinnedCertificateThumbprints,
            cancellationToken);

    private SecretImportPreviewDto BuildImportPreview(IReadOnlyDictionary<string, string> decrypted)
    {
        var catalogKeys = SecretCatalog.AllKeys.ToHashSet(StringComparer.Ordinal);
        var items = new List<SecretImportPreviewItemDto>();
        var unknown = new List<string>();

        foreach (var key in decrypted.Keys)
        {
            if (!catalogKeys.Contains(key))
            {
                unknown.Add(key);
                continue;
            }

            var entry = SecretCatalog.All.First(e => e.Key == key);
            items.Add(new SecretImportPreviewItemDto(
                key,
                entry.DisplayName,
                _vault.HasSecret(key),
                IsKnown: true));
        }

        return new SecretImportPreviewDto(items, unknown.Count, unknown, items.Count);
    }

    private static SecretValidationResultDto ToResult(
        string key,
        string label,
        bool exists,
        bool success,
        string? detail)
        => new(key, label, exists, success, detail);

    private static SecretValidationResultDto ToPairResult(
        string label,
        bool bothExist,
        bool success,
        string? detail,
        string key1,
        string key2)
    {
        var has1 = bothExist || detail is "חסר Client ID" or "חסר שם משתמש";
        var has2 = bothExist || detail is "חסר Client Secret" or "חסרה סיסמה";

        if (!bothExist && detail is not null)
        {
            return new SecretValidationResultDto(
                key1,
                label,
                has1 || has2,
                false,
                detail,
                [key1, key2]);
        }

        return new SecretValidationResultDto(key1, label, bothExist, success, detail, [key1, key2]);
    }

    private SecretStatusDto ResolveEntryStatus(
        SecretCatalogEntry entry,
        IReadOnlyList<SecretValidationResultDto> validations)
    {
        var exists = _vault.HasSecret(entry.Key);

        if (entry.PairKey is not null)
        {
            var pairExists = _vault.HasSecret(entry.PairKey);
            var pairValidation = validations.First(v =>
                v.RelatedKeys?.Contains(entry.Key) == true);

            if (!exists && !pairExists)
            {
                return new SecretStatusDto(entry.Key, SecretStatusLevel.Missing, null, "חסר — לא הוגדר");
            }

            if (!exists || !pairExists)
            {
                var hint = pairValidation.Detail ?? "חסר ערך משלים לבדיקה";
                return new SecretStatusDto(entry.Key, SecretStatusLevel.Incomplete, hint, $"⚠ {hint}");
            }

            return MapValidationToStatus(entry.Key, pairValidation);
        }

        if (!exists)
        {
            return new SecretStatusDto(entry.Key, SecretStatusLevel.Missing, null, "חסר — לא הוגדר");
        }

        var validation = validations.First(v => v.Key == entry.Key);
        return MapValidationToStatus(entry.Key, validation);
    }

    private static SecretStatusDto MapValidationToStatus(string key, SecretValidationResultDto validation)
    {
        if (validation.Success)
        {
            var tip = $"✅ פעיל ונבדק{(validation.Detail != null ? $" ({validation.Detail})" : string.Empty)}";
            return new SecretStatusDto(key, SecretStatusLevel.Valid, validation.Detail, tip);
        }

        return new SecretStatusDto(key, SecretStatusLevel.Invalid, validation.Detail, $"❌ {validation.Detail}");
    }
}
