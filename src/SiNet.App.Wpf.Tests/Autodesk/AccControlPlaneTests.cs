using System.Net;
using System.Net.Http;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;
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
    public void AddSiNetAutodesk_registers_acc_runtime_services_without_legacy_write_side_services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretSetupHostConfiguration>(new StubSecretSetupHostConfiguration("https://acc.example.com"));
        services.AddSingleton<ISecretVaultStore>(new InMemorySecretVaultStore());
        services.AddSiNetAutodesk();

        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceModeProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceHealthProbe));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceDiagnosticsProbe));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccServiceKeyDiagnostics));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccProjectCatalogService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccLiveProjectDiscoveryService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccProjectService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccDocumentService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccFolderPathService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccItemService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccFileUploadService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccFileDownloadService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccFolderBrowserService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccProjectTreeSearchService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccInboxBootstrapService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccLookupSeedService));
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.AccBootstrap.IAccProjectProvisioningService");
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.AccBootstrap.IAccInboxProvisioner");
        Assert.DoesNotContain(services, d => d.ServiceType.FullName == "SiNetSQL.Services.Files.IProjectFileFilingService");
    }

    [Fact]
    public void AddSiNetAutodeskStatusWpf_registers_status_window_operator_services()
    {
        var services = new ServiceCollection();

        services.AddSiNetAutodeskStatusWpf();

        Assert.Contains(services, d => d.ServiceType == typeof(AccControlPlaneStatusPresenter));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccResolvedDocsUrlLauncher));
        Assert.Contains(services, d => d.ServiceType == typeof(IClipboardTextWriter));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAccInboxBootstrapLocalExecutor));
        Assert.Contains(services, d => d.ServiceType == typeof(AccControlPlaneStatusWindowViewModel));
        Assert.Contains(services, d => d.ServiceType == typeof(AccControlPlaneStatusWindowView));
        Assert.Contains(services, d => d.ServiceType == typeof(AccControlPlaneStatusWindow));
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
    public async Task Local_project_catalog_service_returns_display_names_when_available()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.ProjectAccMappings.AddRange(
                new ProjectAccMapping { ProjectId = 1, AccHubId = 10, AccProjectId = " b.project-2 ", AccProjectName = "Zeta Tower", Project = null!, AccHub = null! },
                new ProjectAccMapping { ProjectId = 2, AccHubId = 10, AccProjectId = "b.project-1", AccProjectName = "Alpha Campus", Project = null!, AccHub = null! });
            seed.AccSystemResources.Add(
                new AccSystemResource { Key = "OfficeInbox", AccHubId = 10, AccProjectId = "b.system-1", AccHub = null! });
            await seed.SaveChangesAsync();
        }

        var sut = new LocalAccProjectCatalogService(new StubDbContextFactory(options));

        var result = await sut.GetProjectsAsync();

        Assert.Equal(3, result.Count);
        Assert.Equal("Alpha Campus", result[0].DisplayName);
        Assert.Equal("b.project-1", result[0].ProjectId);
        var systemProject = Assert.Single(result, static project => project.ProjectId == "b.system-1");
        Assert.Equal("System: OfficeInbox", systemProject.DisplayName);
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
    public async Task Remote_project_catalog_service_uses_versioned_catalog_endpoint_and_maps_response()
    {
        const string body = """
            {
              "projects": [
                { "projectId": " b.project-2 ", "displayName": "Zeta Tower", "sourceLabel": "ProjectAccMapping" },
                { "projectId": "b.project-1", "displayName": "Alpha Campus", "sourceLabel": "ProjectAccMapping" },
                { "projectId": "b.project-2", "displayName": "Zeta Tower", "sourceLabel": "ProjectAccMapping" }
              ]
            }
            """;

        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccProjectCatalogService(
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

        var result = await sut.GetProjectsAsync();

        Assert.Equal("https://acc.example.com/v1/acc/projects/catalog", requestedUri?.ToString());
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.Equal(["b.project-1", "b.project-2"], result.Select(static project => project.ProjectId).ToArray());
        Assert.Equal("Alpha Campus", result[0].DisplayName);
    }

    [Fact]
    public async Task Local_live_project_discovery_service_returns_hubs_and_projects_from_readers()
    {
        var sut = new LocalAccLiveProjectDiscoveryService(
            new StubAccHubReader([new AccHubCatalogEntry("b.hub-1", "Primary Hub", "EMEA")]),
            new StubAccLiveProjectReader([new AccProjectCatalogEntry("b.project-1", "Alpha Campus", "LiveAcc")]));

        var hubs = await sut.GetHubsAsync();
        var projects = await sut.GetProjectsAsync("b.hub-1");

        Assert.Single(hubs);
        Assert.Equal("b.hub-1", hubs[0].HubId);
        Assert.Single(projects);
        Assert.Equal("b.project-1", projects[0].ProjectId);
        Assert.Equal("Alpha Campus", projects[0].DisplayName);
    }

    [Fact]
    public async Task Remote_live_project_discovery_service_uses_versioned_live_endpoints_and_maps_response()
    {
        const string hubsBody = """
            {
              "hubs": [
                { "hubId": "b.hub-2", "displayName": "Zeta Hub", "region": "US" },
                { "hubId": "b.hub-1", "displayName": "Alpha Hub", "region": "EMEA" }
              ]
            }
            """;
        const string projectsBody = """
            {
              "projects": [
                { "projectId": " b.project-2 ", "displayName": "Zeta Tower" },
                { "projectId": "b.project-1", "displayName": "Alpha Campus" }
              ]
            }
            """;

        var requestedUris = new List<string>();
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccLiveProjectDiscoveryService(
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                requestedUris.Add(request.RequestUri!.ToString());
                var body = request.RequestUri!.AbsoluteUri.EndsWith("/live/hubs", StringComparison.Ordinal)
                    ? hubsBody
                    : projectsBody;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                });
            })),
            vault,
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration("https://acc.example.com/")));

        var hubs = await sut.GetHubsAsync();
        var projects = await sut.GetProjectsAsync("b.hub-1");

        Assert.Equal("https://acc.example.com/v1/acc/live/hubs", requestedUris[0]);
        Assert.Equal("https://acc.example.com/v1/acc/live/hubs/b.hub-1/projects", requestedUris[1]);
        Assert.Equal(["b.hub-1", "b.hub-2"], hubs.Select(static hub => hub.HubId).ToArray());
        Assert.Equal(["b.project-1", "b.project-2"], projects.Select(static project => project.ProjectId).ToArray());
    }

    [Fact]
    public async Task Local_folder_browser_service_resolves_root_folder_and_maps_entries()
    {
        var sut = new LocalAccFolderBrowserService(
            new StubAccProjectRootFolderResolver("root-folder"),
            new StubFolderContentsReader(
            [
                new AccFolderBrowseEntry("folder-a", "A Folder", AccFolderEntryKind.Folder, 0, null, null),
                new AccFolderBrowseEntry("item-b", "B File.pdf", AccFolderEntryKind.Item, 123, null, null),
            ]));

        var result = await sut.BrowseAsync("project-123");

        Assert.NotNull(result);
        Assert.Equal("b.project-123", result!.ProjectId);
        Assert.Equal("root-folder", result.FolderId);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(AccFolderEntryKind.Folder, result.Entries[0].Kind);
        Assert.Equal(AccFolderEntryKind.Item, result.Entries[1].Kind);
    }

    [Fact]
    public async Task Remote_folder_browser_service_uses_versioned_browse_endpoint_and_maps_response()
    {
        const string body = """
            {
              "projectId": "b.project-123",
              "folderId": "root-folder",
              "entries": [
                { "id": "folder-a", "displayName": "A Folder", "kind": 0, "fileSize": 0, "lastModifiedTime": null, "createTime": null },
                { "id": "item-b", "displayName": "B File.pdf", "kind": 1, "fileSize": 123, "lastModifiedTime": null, "createTime": null }
              ]
            }
            """;

        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccFolderBrowserService(
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

        var result = await sut.BrowseAsync("b.project-123", "root-folder");

        Assert.NotNull(result);
        Assert.Equal("https://acc.example.com/v1/acc/projects/b.project-123/folders/browse?folderId=root-folder", requestedUri?.AbsoluteUri);
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.Equal("root-folder", result!.FolderId);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(AccFolderEntryKind.Folder, result.Entries[0].Kind);
        Assert.Equal(AccFolderEntryKind.Item, result.Entries[1].Kind);
    }

    [Fact]
    public async Task Mode_switching_inbox_bootstrap_service_uses_local_executor_when_mode_is_local()
    {
        var sut = new ModeSwitchingAccInboxBootstrapService(
            new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration(null)),
            new StubAccInboxBootstrapLocalExecutor(new AccInboxBootstrapResult(
                "b.hub-1",
                "b.project-inbox",
                "root-folder",
                "inbox-folder")),
            new RemoteAccInboxBootstrapService(
                new HttpClient(new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("remote should not be used"))),
                new InMemorySecretVaultStore(),
                new ConfigurationAccServiceModeProvider(new StubSecretSetupHostConfiguration("https://acc.example.com"))));

        var result = await sut.EnsureAsync();

        Assert.Equal("b.project-inbox", result.AccProjectId);
        Assert.Equal("inbox-folder", result.AccInboxFolderId);
    }

    [Fact]
    public async Task Remote_inbox_bootstrap_service_uses_versioned_endpoint_and_maps_response()
    {
        const string body = """
            {
              "accHubDbId": 10,
              "hubId": "b.hub-1",
              "accProjectId": "b.project-inbox",
              "accRootFolderId": "root-folder",
              "accInboxFolderId": "inbox-folder"
            }
            """;

        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        HttpMethod? requestedMethod = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccInboxBootstrapService(
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                requestedUri = request.RequestUri;
                requestedMethod = request.Method;
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

        var result = await sut.EnsureAsync();

        Assert.Equal(HttpMethod.Post, requestedMethod);
        Assert.Equal("https://acc.example.com/v1/acc/inbox/ensure", requestedUri?.AbsoluteUri);
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.Equal("b.hub-1", result.HubId);
        Assert.Equal("b.project-inbox", result.AccProjectId);
        Assert.Equal("root-folder", result.AccRootFolderId);
        Assert.Equal("inbox-folder", result.AccInboxFolderId);
    }

    [Fact]
    public async Task Local_project_tree_search_service_scans_nested_folders_and_returns_matches()
    {
        var folderBrowser = new LocalAccFolderBrowserService(
            new StubAccProjectRootFolderResolver("root-folder"),
            new StubFolderContentsByFolderReader((_, folderId) => folderId switch
            {
                "root-folder" =>
                [
                    new AccFolderBrowseEntry("folder-a", "Discipline A", AccFolderEntryKind.Folder, 0, null, null),
                    new AccFolderBrowseEntry("folder-b", "Discipline B", AccFolderEntryKind.Folder, 0, null, null),
                ],
                "folder-a" =>
                [
                    new AccFolderBrowseEntry("folder-a1", "Sheets", AccFolderEntryKind.Folder, 0, null, null),
                ],
                "folder-a1" =>
                [
                    new AccFolderBrowseEntry("item-1", "Tower Plan.pdf", AccFolderEntryKind.Item, 10, null, null),
                ],
                "folder-b" =>
                [
                    new AccFolderBrowseEntry("item-2", "Other Notes.pdf", AccFolderEntryKind.Item, 10, null, null),
                ],
                _ => [],
            }));
        var sut = new LocalAccProjectTreeSearchService(folderBrowser);

        var result = await sut.SearchAsync("project-123", "plan");

        Assert.Single(result.Matches);
        Assert.Equal("b.project-123", result.Matches[0].ProjectId);
        Assert.Equal("folder-a1", result.Matches[0].FolderId);
        Assert.Equal("Project Files / Discipline A / Sheets", result.Matches[0].FolderPath);
        Assert.Equal("Tower Plan.pdf", result.Matches[0].FileName);
        Assert.Equal(4, result.VisitedFolderCount);
        Assert.False(result.HitFolderLimit);
        Assert.False(result.HitResultLimit);
    }

    [Fact]
    public async Task Remote_project_tree_search_service_uses_versioned_search_endpoint_and_maps_response()
    {
        const string body = """
            {
              "matches": [
                { "projectId": "b.project-123", "folderId": "folder-a1", "folderPath": "Project Files / Discipline A / Sheets", "fileName": "Tower Plan.pdf" }
              ],
              "visitedFolderCount": 4,
              "hitFolderLimit": false,
              "hitResultLimit": false
            }
            """;

        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccProjectTreeSearchService(
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

        var result = await sut.SearchAsync("b.project-123", "Tower Plan", "root-folder");

        Assert.Equal("https://acc.example.com/v1/acc/projects/b.project-123/folders/search?fileName=Tower%20Plan&folderId=root-folder", requestedUri?.AbsoluteUri);
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.Single(result.Matches);
        Assert.Equal("folder-a1", result.Matches[0].FolderId);
        Assert.Equal(4, result.VisitedFolderCount);
    }

    [Fact]
    public async Task Local_lookup_seed_service_returns_recent_db_backed_candidates()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.EmailInboxMessages.AddRange(
                new EmailInboxMessage
                {
                    Id = 10,
                    MessageUniqueId = "msg-10",
                    ProjectId = 1,
                    InternetMessageId = "<msg-10@example.com>",
                    ThreadUniqueId = "thread-10",
                    ThreadKey = "thr010",
                    ReceivedUtc = new DateTime(2026, 7, 3, 20, 00, 00, DateTimeKind.Utc),
                    InboxAccProjectId = " b.project-1 ",
                    InboxAccFolderId = " folder-22 "
                },
                new EmailInboxMessage
                {
                    Id = 11,
                    MessageUniqueId = "msg-11",
                    ProjectId = 1,
                    InternetMessageId = "<msg-11@example.com>",
                    ThreadUniqueId = "thread-11",
                    ThreadKey = "thr011",
                    ReceivedUtc = new DateTime(2026, 7, 3, 19, 00, 00, DateTimeKind.Utc),
                    InboxAccProjectId = "b.project-1",
                    InboxAccFolderId = "folder-22"
                });
            seed.EmailInboxAttachments.AddRange(
                new EmailInboxAttachment
                {
                    Id = 100,
                    MessageId = 10,
                    AttachmentIndex = 0,
                    SavedFileName = "Drawing Set.pdf",
                    ContentSha256 = new string('a', 64),
                    AccItemId = "item-100"
                },
                new EmailInboxAttachment
                {
                    Id = 101,
                    MessageId = 11,
                    AttachmentIndex = 0,
                    OriginalFileName = "Drawing Set.pdf",
                    ContentSha256 = new string('b', 64),
                    AccItemId = "item-101"
                });
            await seed.SaveChangesAsync();
        }

        var sut = new LocalAccLookupSeedService(new StubDbContextFactory(options));

        var result = await sut.GetRecentSeedsAsync();

        var candidate = Assert.Single(result);
        Assert.Equal("b.project-1", candidate.ProjectId);
        Assert.Equal("folder-22", candidate.FolderId);
        Assert.Equal("Drawing Set.pdf", candidate.FileName);
        Assert.Equal("item-100", candidate.ItemId);
        Assert.Contains("EmailInboxAttachment", candidate.SourceLabel, StringComparison.Ordinal);
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
        Assert.Contains("/projects/catalog", source, StringComparison.Ordinal);
        Assert.Contains("/live/hubs", source, StringComparison.Ordinal);
        Assert.Contains("/live/hubs/{hubId}/projects", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId}/folders/browse", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId}/folders/search", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId}/files/upload", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId}/items/{itemId}/download", source, StringComparison.Ordinal);
        Assert.Contains("/inbox/ensure", source, StringComparison.Ordinal);
        Assert.Contains("/projects/{projectId}/folders/{folderId}/items/resolve", source, StringComparison.Ordinal);
        Assert.Contains("ProjectAccMappings", source, StringComparison.Ordinal);
        Assert.Contains("AccSystemResources", source, StringComparison.Ordinal);
        Assert.Contains("GetFolderItemsAsync(projectId, folderId, ct)", source, StringComparison.Ordinal);
        Assert.Contains("GetFolderContentsAsync", source, StringComparison.Ordinal);
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
        Assert.Contains("services.AddSiNetNewSystemWpf();", source, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient<IAccInboxBootstrapLocalExecutor, LegacyHostLocalAccInboxBootstrapExecutor>();", source, StringComparison.Ordinal);
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

    private sealed class StubFolderContentsReader(IReadOnlyList<AccFolderBrowseEntry> entries) : IAccFolderContentsReader
    {
        private readonly IReadOnlyList<AccFolderBrowseEntry> _entries = entries;

        public Task<IReadOnlyList<AccFolderBrowseEntry>> GetFolderContentsAsync(
            string projectId,
            string folderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries);
    }

    private sealed class StubFolderContentsByFolderReader(
        Func<string, string, IReadOnlyList<AccFolderBrowseEntry>> handler) : IAccFolderContentsReader
    {
        private readonly Func<string, string, IReadOnlyList<AccFolderBrowseEntry>> _handler = handler;

        public Task<IReadOnlyList<AccFolderBrowseEntry>> GetFolderContentsAsync(
            string projectId,
            string folderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_handler(projectId, folderId));
    }

    private sealed class StubAccProjectRootFolderResolver(string? folderId) : IAccProjectRootFolderResolver
    {
        public Task<string?> ResolveProjectFilesRootFolderIdAsync(
            string projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(folderId);
    }

    private sealed class StubAccHubReader(IReadOnlyList<AccHubCatalogEntry> hubs) : IAccHubReader
    {
        public Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(hubs);
    }

    private sealed class StubAccLiveProjectReader(IReadOnlyList<AccProjectCatalogEntry> projects) : IAccLiveProjectReader
    {
        public Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(
            string hubId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(projects);
    }

    private sealed class StubAccInboxBootstrapLocalExecutor(AccInboxBootstrapResult result) : IAccInboxBootstrapLocalExecutor
    {
        public Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
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
