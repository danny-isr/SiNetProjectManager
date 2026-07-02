using System.IO;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Secrets;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeSecretSetupCatalogTests
{
    private static readonly string[] LegacySecretKeys =
    [
        "SiNet/GeminiApiKey",
        "SiNet/Autodesk/ClientId",
        "SiNet/Autodesk/ClientSecret",
        "SiNet/Google/ClientSecrets",
        "SiNet/ConnectionStrings/SiNetDatabase",
        "SiNet/ConnectionStrings/ReplicaDatabase",
        "SiNet/ConnectionStrings/MasterPlanDatabase",
        "SiNet/ActiveDirectory/Username",
        "SiNet/ActiveDirectory/Password",
        "SiNet/AccService/ApiKey",
        "SiNet/MasterPlanApi/ApiKey",
    ];

    [Fact]
    public void SecretCatalog_includes_all_legacy_secret_keys()
    {
        Assert.Equal(LegacySecretKeys.Length, SecretCatalog.All.Count);

        foreach (var key in LegacySecretKeys)
        {
            Assert.Contains(SecretCatalog.All, e => e.Key == key);
        }
    }

    [Fact]
    public void Google_validation_accepts_installed_credentials_json()
    {
        const string json = """
            {
              "installed": {
                "client_id": "abc.apps.googleusercontent.com",
                "client_secret": "secret-value"
              }
            }
            """;

        var (success, detail) = GoogleClientSecretsValidator.ValidateJsonContent(json);

        Assert.True(success);
        Assert.Equal("Google OAuth", detail);
    }

    [Fact]
    public void Google_validation_accepts_web_credentials_json()
    {
        const string json = """
            {
              "web": {
                "client_id": "abc.apps.googleusercontent.com",
                "client_secret": "secret-value"
              }
            }
            """;

        var (success, _) = GoogleClientSecretsValidator.ValidateJsonContent(json);

        Assert.True(success);
    }

    [Fact]
    public void Google_validation_fails_when_client_id_or_secret_missing()
    {
        const string missingId = """{"installed":{"client_secret":"x"}}""";
        const string missingSecret = """{"web":{"client_id":"x"}}""";

        Assert.False(GoogleClientSecretsValidator.ValidateJsonContent(missingId).Success);
        Assert.False(GoogleClientSecretsValidator.ValidateJsonContent(missingSecret).Success);
    }

    [Fact]
    public void ConnectionStringNormalizer_fixes_double_backslash_and_trust_server_certificate()
    {
        const string raw = "Server=MYPC\\\\SQLEXPRESS;Database=SiNet;Integrated Security=True;TrustServerCertificate=False";

        var normalized = ConnectionStringNormalizer.Normalize(raw);

        Assert.Contains("MYPC\\SQLEXPRESS", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("MYPC\\\\SQLEXPRESS", normalized, StringComparison.Ordinal);
        Assert.Contains("Trust Server Certificate=True", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAndValidateAsync_stores_google_json_content_not_path()
    {
        const string json = """
            {"installed":{"client_id":"id","client_secret":"secret"}}
            """;
        var vault = new InMemorySecretVaultStore();
        var service = new CredentialVaultSecretSetupService(vault, NullSecretSetupHostConfiguration.Instance);

        await service.SaveAndValidateAsync(new SecretSetupUpdateDto(
            new Dictionary<string, string?> { [SecretCatalog.GoogleClientSecrets] = json }));

        Assert.Equal(json, vault.GetSecret(SecretCatalog.GoogleClientSecrets));
        Assert.DoesNotContain(".json", vault.GetSecret(SecretCatalog.GoogleClientSecrets)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAndValidateAsync_reports_incomplete_autodesk_pair()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AutodeskClientId, "client-id-only");
        var service = new CredentialVaultSecretSetupService(vault, NullSecretSetupHostConfiguration.Instance);

        var result = await service.SaveAndValidateAsync(new SecretSetupUpdateDto(new Dictionary<string, string?>()));

        Assert.Contains(result.FailedSummaries, s => s.Contains("Autodesk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FailedSummaries, s => s.Contains("Client Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveAndValidateAsync_reports_incomplete_ad_pair()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AdUsername, "DOMAIN\\user");
        var service = new CredentialVaultSecretSetupService(vault, NullSecretSetupHostConfiguration.Instance);

        var result = await service.SaveAndValidateAsync(new SecretSetupUpdateDto(new Dictionary<string, string?>()));

        Assert.Contains(result.FailedSummaries, s => s.Contains("Active Directory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NewShellFactory_opens_native_SecretSetupWindow_not_legacy()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("SecretSetupWindow", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeSecretSetup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WPF_Window.SecretSetupWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetProjectManagerV2.WPF_Window", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_secret_setup_has_no_legacy_mvvm_reference()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Admin/Security/SecretSetupViewModel.cs");
        var view = ReadRepoFile("src/SiNet.App.Wpf/Admin/Security/SecretSetupView.xaml");

        Assert.DoesNotContain("SiNetSQL.MVVM", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.MVVM", view, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings", vm, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CredentialVaultSecretSetupService_does_not_write_appsettings()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Secrets/CredentialVaultSecretSetupService.cs");

        Assert.DoesNotContain("appsettings", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IConfiguration", source, StringComparison.Ordinal);
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

    private sealed class NullSecretSetupHostConfiguration : ISecretSetupHostConfiguration
    {
        public static NullSecretSetupHostConfiguration Instance { get; } = new();

        public string? ActiveDirectoryDomainName => null;
    }
}
