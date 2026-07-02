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
            var result = await service.ImportAsync(path, "pw-123456", overwrite: true);

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
    public async Task GenerateAccServiceApiKeyAsync_saves_to_vault()
    {
        var vault = new InMemorySecretVaultStore();
        var service = new CredentialVaultSecretSetupService(vault, NullHost.Instance);

        var key = await service.GenerateAccServiceApiKeyAsync();

        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.Equal(key, vault.GetSecret(SecretCatalog.AccServiceApiKey));
        Assert.True(key.Length >= 16);
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

        public IReadOnlyDictionary<string, bool> GetVaultStatus()
            => SecretCatalog.AllKeys.ToDictionary(k => k, HasSecret);
    }

    private sealed class NullHost : ISecretSetupHostConfiguration
    {
        public static NullHost Instance { get; } = new();

        public string? ActiveDirectoryDomainName => null;

        public string? AccServiceBaseUrl => null;
    }
}
