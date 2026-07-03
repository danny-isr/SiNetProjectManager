using System.Net;
using System.Net.Http;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Autodesk;
using SiNetSQL.Data;
using SiNetSQL.Models;
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
    public void AddSiNetAutodesk_registers_read_only_project_and_document_services_without_write_side_services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretSetupHostConfiguration>(new StubSecretSetupHostConfiguration("https://acc.example.com"));
        services.AddSingleton<ISecretVaultStore>(new InMemorySecretVaultStore());
        services.AddSiNetAutodesk();

        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceModeProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceHealthProbe));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceDiagnosticsProbe));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceKeyDiagnostics));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccProjectService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccDocumentService));
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.AccBootstrap.IAccProjectProvisioningService");
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.AccBootstrap.IAccInboxProvisioner");
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.Files.IProjectFileFilingService");
    }

    [Fact]
    public async Task Local_project_service_returns_known_acc_project_ids_from_mappings_and_system_resources()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.ProjectAccMappings.AddRange(
                new ProjectAccMapping { ProjectId = 1, AccHubId = 10, AccProjectId = " b.project-2 ", Project = null!, AccHub = null! },
                new ProjectAccMapping { ProjectId = 2, AccHubId = 10, AccProjectId = "b.project-1", Project = null!, AccHub = null! },
                new ProjectAccMapping { ProjectId = 3, AccHubId = 10, AccProjectId = "b.project-2", Project = null!, AccHub = null! });
            seed.AccSystemResources.AddRange(
                new AccSystemResource { Key = "OfficeInbox", AccHubId = 10, AccProjectId = "b.system-1", AccHub = null! },
                new AccSystemResource { Key = "Other", AccHubId = 10, AccProjectId = " ", AccHub = null! });
            await seed.SaveChangesAsync();
        }

        var sut = new LocalAccProjectService(new StubDbContextFactory(options));

        var result = await sut.GetProjectIdsAsync();

        Assert.Equal(["b.project-1", "b.project-2", "b.system-1"], result);
    }

    [Fact]
    public async Task Remote_project_service_uses_versioned_ids_endpoint_and_maps_response()
    {
        const string body = """
            {
              "projectIds": [" b.project-2 ", "b.project-1", "b.project-2"]
            }
            """;

        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccProjectService(
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                requestedUri = request.RequestUri;
                apiKeyHeader = request.Headers.TryGetValues(AccServiceContractConstants.ApiKeyHeader, out var values)
                    ? values.Single()
                    : null;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                });
            })),
            vault,
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration("https://acc.example.com/")));

        var result = await sut.GetProjectIdsAsync();

        Assert.Equal("https://acc.example.com/v1/acc/projects/ids", requestedUri?.ToString());
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.Equal(["b.project-1", "b.project-2"], result);
    }

    [Fact]
    public async Task Local_document_service_returns_null_when_file_not_found()
    {
        var sut = new LocalAccDocumentService(new StubFolderItemsReader(
        [
            new AccDocumentLookupResult("project-123", "item-a", "Existing.pdf", null, null),
        ]));

        var result = await sut.FindItemAsync("project-123", "folder-456", "Missing.pdf");

        Assert.Null(result);
    }

    [Fact]
    public async Task Local_document_service_maps_matched_item_into_acc_item_ref()
    {
        var sut = new LocalAccDocumentService(new StubFolderItemsReader(
        [
            new AccDocumentLookupResult("project-123", "item-a", "other.pdf", null, null),
            new AccDocumentLookupResult("project-123", "item-b", "Drawing.pdf", "version-9", "https://viewer.example/item-b"),
        ]));

        var result = await sut.FindItemAsync("project-123", "folder-456", "Drawing.pdf");

        Assert.NotNull(result);
        Assert.Equal("project-123", result!.ProjectId);
        Assert.Equal("item-b", result.ItemId);
        Assert.Equal("version-9", result.VersionId);
        Assert.Equal("https://viewer.example/item-b", result.ViewerUrl);
    }

    [Fact]
    public async Task Remote_document_service_uses_versioned_lookup_endpoint_and_maps_response()
    {
        const string body = """
            {
              "projectId": "project-123",
              "itemId": "item-789",
              "versionId": "version-5",
              "viewerUrl": "https://viewer.example/item-789"
            }
            """;

        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccDocumentService(
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                requestedUri = request.RequestUri;
                apiKeyHeader = request.Headers.TryGetValues(AccServiceContractConstants.ApiKeyHeader, out var values)
                    ? values.Single()
                    : null;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                });
            })),
            vault,
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration("https://acc.example.com/")));

        var result = await sut.FindItemAsync("project-123", "folder-456", "Drawing Set.pdf");

        Assert.NotNull(result);
        Assert.Equal("https://acc.example.com/v1/acc/projects/project-123/folders/folder-456/items/resolve?fileName=Drawing%20Set.pdf", requestedUri?.AbsoluteUri);
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.Equal("project-123", result!.ProjectId);
        Assert.Equal("item-789", result.ItemId);
        Assert.Equal("version-5", result.VersionId);
        Assert.Equal("https://viewer.example/item-789", result.ViewerUrl);
    }

    [Fact]
    public void Acc_service_source_contains_read_only_item_lookup_endpoint()
    {
        var source = File.ReadAllText(Path.Combine(Boundary.RepoPaths.RepoRoot, "SiOffice.AccService", "Endpoints", "AccEndpoints.cs"));

        Assert.Contains("/projects/ids", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId}/folders/{folderId}/items/resolve", source, StringComparison.Ordinal);
        Assert.Contains("ProjectAccMappings", source, StringComparison.Ordinal);
        Assert.Contains("AccSystemResources", source, StringComparison.Ordinal);
        Assert.Contains("GetFolderItemsAsync(projectId, folderId, ct)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_new_system_graph_registers_autodesk_core_before_status_window()
    {
        var source = File.ReadAllText(Path.Combine(
            Boundary.RepoPaths.RepoRoot,
            "SiNetProjectManagerV2",
            "Services",
            "Composition",
            "NewSystemServiceCollectionExtensions.cs"));

        Assert.Contains("services.AddSiNetAutodesk();", source, StringComparison.Ordinal);
        Assert.Contains("services.AddSiNetAutodeskStatusWpf();", source, StringComparison.Ordinal);
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

    private sealed class StubFolderItemsReader(IReadOnlyList<AccDocumentLookupResult> items) : IAccFolderItemsReader
    {
        private readonly IReadOnlyList<AccDocumentLookupResult> _items = items;

        public Task<IReadOnlyList<AccDocumentLookupResult>> GetFolderItemsAsync(
            string projectId,
            string folderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options) : IDbContextFactory<SiNetSQLDbContext>
    {
        private readonly DbContextOptions<SiNetSQLDbContext> _options = options;

        public SiNetSQLDbContext CreateDbContext() => new(_options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(_options));
    }

    private static string AppWpfRoot =>
        Path.Combine(Boundary.RepoPaths.RepoRoot, "src", "SiNet.App.Wpf");
}
