using System.IO;
using System.Text;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Secrets;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeSecretSetupGapTests
{
    [Fact]
    public void Export_does_not_write_plain_text_secrets()
    {
        const string secretValue = "super-secret-gemini-key-12345";
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.GeminiApiKey, secretValue);

        var path = Path.Combine(Path.GetTempPath(), $"sinet-test-{Guid.NewGuid():N}.secrets");
        try
        {
            SecretProvisioningFileService.ExportToFile(vault, path, "test-password-123");

            var fileText = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain(secretValue, fileText, StringComparison.Ordinal);
            Assert.True(SecretProvisioningFileService.IsEncryptedProvisioningFile(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Import_preview_lists_known_keys_without_secret_values()
    {
        var vault = new InMemorySecretVaultStore();
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);

        var path = Path.Combine(Path.GetTempPath(), $"sinet-test-{Guid.NewGuid():N}.secrets");
        try
        {
            SecretProvisioningFileService.WriteEncryptedDictionary(
                new Dictionary<string, string>
                {
                    [SecretCatalog.GeminiApiKey] = "key-one",
                    ["SiNet/Unknown/LegacyKey"] = "should-skip",
                },
                path,
                "pw-123456");

            var preview = await service.PreviewImportAsync(path, "pw-123456");

            Assert.Single(preview.Items);
            Assert.Equal(SecretCatalog.GeminiApiKey, preview.Items[0].Key);
            Assert.Equal(1, preview.UnknownKeyCount);
            Assert.Contains("SiNet/Unknown/LegacyKey", preview.UnknownKeys);

            var serialized = string.Join('|', preview.Items.Select(i => $"{i.Key}:{i.DisplayName}"));
            Assert.DoesNotContain("key-one", serialized, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ImportAsync_skips_unknown_keys_and_reports_counts()
    {
        var vault = new InMemorySecretVaultStore();
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);

        var path = Path.Combine(Path.GetTempPath(), $"sinet-test-{Guid.NewGuid():N}.secrets");
        try
        {
            SecretProvisioningFileService.WriteEncryptedDictionary(
                new Dictionary<string, string>
                {
                    [SecretCatalog.AccServiceApiKey] = "acc-key-value",
                    ["SiNet/Not/InCatalog"] = "x",
                },
                path,
                "pw-123456");
            var result = await service.ImportAsync(path, "pw-123456", SecretImportMode.UpsertFromFile);

            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal("acc-key-value", vault.GetSecret(SecretCatalog.AccServiceApiKey));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task UpsertFromFile_updates_keys_in_file_and_leaves_others()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.GeminiApiKey, "keep-me");
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "old-acc");
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"sinet-test-{Guid.NewGuid():N}.secrets");
        try
        {
            SecretProvisioningFileService.WriteEncryptedDictionary(
                new Dictionary<string, string> { [SecretCatalog.AccServiceApiKey] = "new-acc" },
                path,
                "pw-123456");

            var result = await service.ImportAsync(path, "pw-123456", SecretImportMode.UpsertFromFile);

            Assert.Equal(1, result.UpdatedCount);
            Assert.Equal(0, result.DeletedCount);
            Assert.Equal("new-acc", vault.GetSecret(SecretCatalog.AccServiceApiKey));
            Assert.Equal("keep-me", vault.GetSecret(SecretCatalog.GeminiApiKey));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ReplaceCatalogWithFile_deletes_catalog_keys_absent_from_file()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.GeminiApiKey, "gone");
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "old-acc");
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"sinet-test-{Guid.NewGuid():N}.secrets");
        try
        {
            SecretProvisioningFileService.WriteEncryptedDictionary(
                new Dictionary<string, string> { [SecretCatalog.AccServiceApiKey] = "new-acc" },
                path,
                "pw-123456");

            var result = await service.ImportAsync(path, "pw-123456", SecretImportMode.ReplaceCatalogWithFile);

            Assert.Equal("new-acc", vault.GetSecret(SecretCatalog.AccServiceApiKey));
            Assert.False(vault.HasSecret(SecretCatalog.GeminiApiKey));
            Assert.Contains(SecretCatalog.GeminiApiKey, result.DeletedKeys!);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ReplaceCatalogWithFile_does_not_delete_unknown_non_catalog_vault_keys()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "old-acc");
        vault.SetSecret("SiNet/Not/InCatalog", "keep-outside-catalog");
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"sinet-test-{Guid.NewGuid():N}.secrets");
        try
        {
            SecretProvisioningFileService.WriteEncryptedDictionary(
                new Dictionary<string, string> { [SecretCatalog.AccServiceApiKey] = "new-acc" },
                path,
                "pw-123456");

            await service.ImportAsync(path, "pw-123456", SecretImportMode.ReplaceCatalogWithFile);

            Assert.Equal("keep-outside-catalog", vault.GetSecret("SiNet/Not/InCatalog"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task GenerateAccServiceApiKeyAsync_saves_cryptographic_key_to_vault()
    {
        var vault = new InMemorySecretVaultStore();
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);

        var key = await service.GenerateAccServiceApiKeyAsync();

        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.Equal(key, vault.GetSecret(SecretCatalog.AccServiceApiKey));

        var keyBytes = Convert.FromBase64String(key);
        Assert.Equal(32, keyBytes.Length);
    }

    [Fact]
    public async Task GenerateAccServiceCertificatePasswordAsync_saves_password_to_vault()
    {
        var vault = new InMemorySecretVaultStore();
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);

        var password = await service.GenerateAccServiceCertificatePasswordAsync();

        Assert.False(string.IsNullOrWhiteSpace(password));
        Assert.Equal(password, vault.GetSecret(SecretCatalog.AccServiceCertificatePassword));
    }

    [Fact]
    public async Task TestAccServiceAsync_validates_presence_when_base_url_missing()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, Convert.ToBase64String(new byte[32]));
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);

        var result = await service.TestAccServiceAsync();

        Assert.True(result.Success);
        Assert.False(result.IsNetworkTest);
        Assert.Contains("presence/format", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestAccServiceAsync_uses_network_diag_when_base_url_configured()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, Convert.ToBase64String(new byte[32]));
        var service = new CredentialVaultSecretSetupService(vault, HostWithBaseUrl.Instance);

        var result = await service.TestAccServiceAsync();

        Assert.True(result.IsNetworkTest);
        Assert.Contains("AccService", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Google_path_provider_uses_vault_before_config_fallback()
    {
        const string vaultJson = """{"installed":{"client_id":"vault-id","client_secret":"vault-secret"}}""";
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.GoogleClientSecrets, vaultJson);

        var fallbackFile = Path.Combine(Path.GetTempPath(), $"google-fallback-{Guid.NewGuid():N}.json");
        File.WriteAllText(fallbackFile, """{"installed":{"client_id":"fallback","client_secret":"x"}}""");

        try
        {
            var materializer = new GoogleClientSecretsMaterializer(vault);
            var provider = new VaultGoogleClientSecretsPathProvider(
                vault,
                materializer,
                new GoogleClientSecretsFallbackOptions { GmailClientSecretsPath = fallbackFile });

            var vaultPath = await provider.ResolveClientSecretsPathAsync();

            Assert.NotNull(vaultPath);
            Assert.StartsWith(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiNet", "Secrets"),
                vaultPath!,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(vaultJson, File.ReadAllText(vaultPath!));
            Assert.NotEqual(fallbackFile, vaultPath);
        }
        finally
        {
            if (File.Exists(fallbackFile))
            {
                File.Delete(fallbackFile);
            }
        }
    }

    [Fact]
    public async Task Google_path_provider_uses_config_fallback_only_when_vault_empty()
    {
        var vault = new InMemorySecretVaultStore();
        var fallbackFile = Path.Combine(Path.GetTempPath(), $"google-fallback-{Guid.NewGuid():N}.json");
        File.WriteAllText(fallbackFile, """{"installed":{"client_id":"fallback","client_secret":"x"}}""");

        try
        {
            var materializer = new GoogleClientSecretsMaterializer(vault);
            var provider = new VaultGoogleClientSecretsPathProvider(
                vault,
                materializer,
                new GoogleClientSecretsFallbackOptions { GmailClientSecretsPath = fallbackFile });

            var path = await provider.ResolveClientSecretsPathAsync();

            Assert.Equal(fallbackFile, path);
        }
        finally
        {
            if (File.Exists(fallbackFile))
            {
                File.Delete(fallbackFile);
            }
        }
    }

    [Fact]
    public void Google_path_provider_fallback_emits_explicit_warning()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Secrets/GoogleClientSecretsMaterializer.cs");
        Assert.Contains("WARNING: Using config fallback for client secrets", source, StringComparison.Ordinal);
        Assert.Contains("Configure Google OAuth in Secret Setup (Vault)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Google_materializer_writes_to_local_app_data_only()
    {
        var vault = new InMemorySecretVaultStore();
        const string json = """{"installed":{"client_id":"id","client_secret":"secret"}}""";
        vault.SetSecret(SecretCatalog.GoogleClientSecrets, json);

        var materializer = new GoogleClientSecretsMaterializer(vault);
        var result = await materializer.MaterializeClientSecretsFileAsync();

        Assert.NotNull(result.FilePath);
        Assert.True(result.UsedVault);
        Assert.False(result.UsedFallback);
        Assert.StartsWith(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiNet", "Secrets"),
            result.FilePath!,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repos", result.FilePath!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(json, File.ReadAllText(result.FilePath!));
    }

    [Fact]
    public void GmailClientProvider_does_not_use_appsettings_as_primary_source()
    {
        var gmailProvider = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailClientProvider.cs");
        Assert.Contains("IGoogleClientSecretsPathProvider", gmailProvider, StringComparison.Ordinal);
        Assert.Contains("Vault Google client secrets unavailable", gmailProvider, StringComparison.Ordinal);
    }

    [Fact]
    public void App_wpf_configure_gmail_does_not_bind_client_secrets_from_env()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/App.xaml.cs");
        Assert.DoesNotContain("SINET_GOOGLE_CLIENT_SECRETS", source, StringComparison.Ordinal);
        Assert.Contains("IGoogleClientSecretsPathProvider", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretSetupView_includes_export_import_and_accservice_buttons()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Security/SecretSetupView.xaml");
        Assert.Contains("Export secrets", xaml, StringComparison.Ordinal);
        Assert.Contains("Import secrets", xaml, StringComparison.Ordinal);
        Assert.Contains("GenerateAccServiceKeyCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("TestAccServiceCommand", xaml, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class InMemorySecretVaultStore : ISecretVaultStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public bool HasSecret(string key) => _secrets.ContainsKey(key);

        public string? GetSecret(string key) => _secrets.GetValueOrDefault(key);

        public void SetSecret(string key, string value) => _secrets[key] = value;

        public void DeleteSecret(string key) => _secrets.Remove(key);

        public IReadOnlyDictionary<string, bool> GetVaultStatus()
            => SecretCatalog.AllKeys.ToDictionary(k => k, HasSecret);
    }

    private sealed class NullHost : ISecretSetupHostConfiguration
    {
        public static NullHost Instance { get; } = new();

        public string? ActiveDirectoryDomainName => null;

        public string? AccServiceBaseUrl => null;

        public IReadOnlyList<string> AccServicePinnedCertificateThumbprints => [];
    }

    private sealed class HostWithBaseUrl : ISecretSetupHostConfiguration
    {
        public static HostWithBaseUrl Instance { get; } = new();

        public string? ActiveDirectoryDomainName => null;

        public string? AccServiceBaseUrl => "http://127.0.0.1:9";

        public IReadOnlyList<string> AccServicePinnedCertificateThumbprints => [];
    }
}
