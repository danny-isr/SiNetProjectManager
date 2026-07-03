using System.Net;
using System.Net.Http;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class AccControlPlaneTests
{
    [Fact]
    public void Mode_provider_returns_local_when_base_url_missing()
    {
        var sut = new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration(null));

        Assert.Equal(AccServiceMode.Local, sut.Mode);
        Assert.Null(sut.BaseUrl);
    }

    [Fact]
    public void Mode_provider_trims_trailing_slash_and_marks_remote()
    {
        var sut = new ConfigurationAccServiceModeProvider(
            new StubSecretSetupHostConfiguration(" https://acc.example.com/ "));

        Assert.Equal(AccServiceMode.Remote, sut.Mode);
        Assert.Equal("https://acc.example.com", sut.BaseUrl);
    }

    [Fact]
    public async Task Health_probe_returns_not_configured_when_base_url_missing()
    {
        var sut = new HttpAccServiceHealthProbe(
            new HttpClient(new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("should not call"))),
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration(null)),
            new AccServiceControlPlaneOptions());

        var result = await sut.CheckAsync();

        Assert.False(result.IsConfigured);
        Assert.Equal(AccServiceHealthState.NotConfigured, result.State);
        Assert.Null(result.Endpoint);
    }

    [Fact]
    public async Task Health_probe_uses_versioned_endpoint_and_reports_online()
    {
        Uri? requestedUri = null;
        var sut = new HttpAccServiceHealthProbe(
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                requestedUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })),
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration("https://acc.example.com")),
            new AccServiceControlPlaneOptions());

        var result = await sut.CheckAsync();

        Assert.True(result.IsConfigured);
        Assert.Equal(AccServiceHealthState.Online, result.State);
        Assert.Equal("https://acc.example.com/v1/acc/health", requestedUri?.ToString());
        Assert.Equal("https://acc.example.com/v1/acc/health", result.Endpoint);
    }

    [Fact]
    public async Task Health_probe_reports_offline_for_non_success_status()
    {
        var sut = new HttpAccServiceHealthProbe(
            new HttpClient(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))),
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration("https://acc.example.com")),
            new AccServiceControlPlaneOptions());

        var result = await sut.CheckAsync();

        Assert.True(result.IsConfigured);
        Assert.Equal(AccServiceHealthState.Offline, result.State);
        Assert.Equal("HTTP 503", result.Detail);
    }

    [Fact]
    public async Task Diagnostics_probe_maps_safe_fields_from_json()
    {
        const string body = """
            {
              "status": "ok",
              "windowsUser": "DOMAIN\\user",
              "hasApiKey": true,
              "keySource": "CredentialManager",
              "keyLength": 44,
              "keyHashPrefix": "abc123def456",
              "autodeskStatus": true,
              "autodeskDetail": "Autodesk token retrieved successfully.",
              "dbStatus": false,
              "dbDetail": "DB failed"
            }
            """;

        var sut = new HttpAccServiceDiagnosticsProbe(
            new HttpClient(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                }))),
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration("https://acc.example.com")),
            new AccServiceControlPlaneOptions());

        var result = await sut.ProbeAsync();

        Assert.True(result.Reachable);
        Assert.Equal("DOMAIN\\user", result.WindowsUser);
        Assert.True(result.HasApiKey);
        Assert.Equal("CredentialManager", result.KeySource);
        Assert.Equal(44, result.KeyLength);
        Assert.Equal("abc123def456", result.KeyHashPrefix);
        Assert.True(result.AutodeskOk);
        Assert.Equal("Autodesk token retrieved successfully.", result.AutodeskDetail);
        Assert.False(result.DbOk);
        Assert.Equal("DB failed", result.DbDetail);
    }

    [Fact]
    public void Key_diagnostics_hashes_key_without_exposing_secret()
    {
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "abcdefghijklmnop");
        var sut = new VaultAccServiceKeyDiagnostics(vault);

        var result = sut.Describe();

        Assert.True(result.HasApiKey);
        Assert.Equal(16, result.KeyLength);
        Assert.Equal(12, result.KeyHashPrefix?.Length);
        Assert.DoesNotContain("abcdefghijklmnop", result.KeyHashPrefix ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSiNetAutodesk_registers_only_control_plane_services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretSetupHostConfiguration>(new StubSecretSetupHostConfiguration("https://acc.example.com"));
        services.AddSingleton<ISecretVaultStore>(new InMemorySecretVaultStore());
        services.AddSiNetAutodesk();

        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceModeProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceHealthProbe));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceDiagnosticsProbe));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceKeyDiagnostics));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAccProjectService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAccDocumentService));
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.AccBootstrap.IAccProjectProvisioningService");
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.AccBootstrap.IAccInboxProvisioner");
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.Files.IProjectFileFilingService");
    }

    [Fact]
    public void Wpf_harness_registers_native_secrets_before_startup()
    {
        var source = File.ReadAllText(Path.Combine(AppWpfRoot, "App.xaml.cs"));

        Assert.Contains("services.AddSiNetSecrets();", source, StringComparison.Ordinal);
    }

    private sealed class StubSecretSetupHostConfiguration(string? baseUrl) : ISecretSetupHostConfiguration
    {
        public string? ActiveDirectoryDomainName => null;

        public string? AccServiceBaseUrl { get; } = baseUrl;
    }

    private sealed class InMemorySecretVaultStore : ISecretVaultStore
    {
        private readonly Dictionary<string, string> _secrets = [];

        public bool HasSecret(string key) => _secrets.ContainsKey(key);

        public string? GetSecret(string key) => _secrets.GetValueOrDefault(key);

        public void SetSecret(string key, string value) => _secrets[key] = value;

        public IReadOnlyDictionary<string, bool> GetVaultStatus() =>
            _secrets.Keys.ToDictionary(k => k, _ => true);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private static string AppWpfRoot =>
        Path.Combine(Boundary.RepoPaths.RepoRoot, "src", "SiNet.App.Wpf");
}
