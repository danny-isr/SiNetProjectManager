using System.Net;
using System.Net.Http;
using System.Text;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class AccFileTransferTests : IDisposable
{
    private readonly string _tempRoot;

    public AccFileTransferTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AccFileTransferTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task Local_upload_service_same_source_short_circuits_and_skips_upload()
    {
        var connector = new StubTransferConnector();
        connector.FolderItems.Add(new AccFolderItem { ItemId = "EXISTING-ITEM", DisplayName = "Drawing.dwg" });
        connector.CustomAttributes["EXISTING-ITEM"] = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AccFileTransferAttributeMap.SourceNames.ContentSha256] = "SHA-1",
        };
        var sut = new LocalAccFileUploadService(connector);
        var sourcePath = WriteSource("payload", "source.dwg");

        var result = await sut.UploadAsync(new AccFileUploadRequest("b.project-1", sourcePath, "Drawing.dwg")
        {
            RootFolderId = "ROOT-FOLDER",
            SourceIdentity = new AccFileSourceIdentity(
                GmailMessageId: "msg-1",
                MessageDateUtc: DateTime.UtcNow,
                OriginalFileName: "source.dwg",
                FileSizeBytes: 7,
                ContentSha256: "SHA-1",
                AttachmentId: 1),
            CompanionDocument = new AccFileUploadCompanionDocument("folder.json", "{\"ok\":true}"),
        });

        Assert.True(result.AlreadySameSource);
        Assert.Equal("EXISTING-ITEM", result.ItemId);
        Assert.Equal(0, connector.UploadFileFinalCalls);
        Assert.Equal(0, connector.UploadNewVersionCalls);
        Assert.Empty(connector.AttributeWrites);
    }

    [Fact]
    public async Task Local_upload_service_writes_metadata_and_uploads_companion_best_effort()
    {
        var connector = new StubTransferConnector();
        var sut = new LocalAccFileUploadService(connector);
        var sourcePath = WriteSource("payload", "source.dwg");

        var result = await sut.UploadAsync(new AccFileUploadRequest("b.project-1", sourcePath, "Drawing.dwg")
        {
            RootFolderId = "ROOT-FOLDER",
            PathSegments = ["Discipline", "Plans"],
            SourceIdentity = new AccFileSourceIdentity(
                GmailMessageId: "msg-1",
                MessageDateUtc: new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc),
                OriginalFileName: "source.dwg",
                FileSizeBytes: 7,
                ContentSha256: "SHA-2",
                AttachmentId: 2),
            Snapshot = new AccFileUploadSnapshot(
                LastFileName: "Drawing.dwg",
                LastSizeBytes: 7,
                LastSavedUtc: new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc),
                SourceFileNames: ["source.dwg"],
                Notes: null,
                IsManualUpload: false,
                OriginalFolderPath: null),
            CompanionDocument = new AccFileUploadCompanionDocument("Imported Folder.json", "{\"kind\":\"folder\"}"),
        });

        Assert.False(result.AlreadySameSource);
        Assert.Equal("PATH-FOLDER", result.FolderId);
        Assert.Equal(2, connector.UploadFileFinalCalls);
        Assert.Equal(0, connector.UploadNewVersionCalls);
        Assert.Equal(2, connector.AttributeWrites.Count);
        Assert.Contains(connector.AttributeWrites, write =>
            write.ContainsKey(AccFileTransferAttributeMap.SourceNames.GmailMessageId));
        Assert.Contains(connector.AttributeWrites, write =>
            write.ContainsKey(AccFileTransferAttributeMap.SnapshotNames.LastFileName));
    }

    [Fact]
    public async Task Remote_upload_service_uses_versioned_endpoint_api_key_and_multipart_payload()
    {
        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        string? body = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sourcePath = WriteSource("payload-remote", "source.dwg");
        var sut = new RemoteAccFileUploadService(
            new HttpClient(new StubHttpMessageHandler(async (request, _) =>
            {
                requestedUri = request.RequestUri;
                apiKeyHeader = request.Headers.TryGetValues(AccServiceContractConstants.ApiKeyHeader, out var values)
                    ? values.Single()
                    : null;
                body = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "folderId": "target-folder",
                          "itemId": "item-77",
                          "versionId": "version-77",
                          "fileName": "Drawing.dwg",
                          "alreadySameSource": false
                        }
                        """),
                };
            })),
            vault,
            new StubAccServiceModeProvider("https://acc.example.com/"));

        var result = await sut.UploadAsync(new AccFileUploadRequest("b.project-1", sourcePath, "Drawing.dwg")
        {
            RootFolderId = "ROOT-FOLDER",
            PathSegments = ["Discipline", "Plans"],
        });

        Assert.Equal("https://acc.example.com/v1/acc/projects/b.project-1/files/upload", requestedUri?.AbsoluteUri);
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.NotNull(body);
        Assert.Contains("form-data; name=request", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"displayName\":\"Drawing.dwg\"", body, StringComparison.Ordinal);
        Assert.Contains("\"rootFolderId\":\"ROOT-FOLDER\"", body, StringComparison.Ordinal);
        Assert.Contains("payload-remote", body, StringComparison.Ordinal);
        Assert.Equal("item-77", result.ItemId);
        Assert.Equal("version-77", result.VersionId);
    }

    [Fact]
    public async Task Remote_download_service_streams_file_to_temp_path()
    {
        Uri? requestedUri = null;
        string? apiKeyHeader = null;
        var vault = new InMemorySecretVaultStore();
        vault.SetSecret(SecretCatalog.AccServiceApiKey, "native-api-key");
        var sut = new RemoteAccFileDownloadService(
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                requestedUri = request.RequestUri;
                apiKeyHeader = request.Headers.TryGetValues(AccServiceContractConstants.ApiKeyHeader, out var values)
                    ? values.Single()
                    : null;

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("downloaded payload")),
                };
                response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
                {
                    FileName = "\"Downloaded.dwg\"",
                };
                response.Headers.Add("X-Acc-Downloaded-FileName", "Downloaded.dwg");
                return Task.FromResult(response);
            })),
            vault,
            new StubAccServiceModeProvider("https://acc.example.com/"));

        var result = await sut.DownloadToTempAsync("b.project-1", "item-1");

        Assert.NotNull(result);
        Assert.Equal("https://acc.example.com/v1/acc/projects/b.project-1/items/item-1/download", requestedUri?.AbsoluteUri);
        Assert.Equal("native-api-key", apiKeyHeader);
        Assert.Equal("Downloaded.dwg", result!.DownloadedFileName);
        Assert.True(File.Exists(result.TempFilePath));
        Assert.Equal("downloaded payload", await File.ReadAllTextAsync(result.TempFilePath));
        File.Delete(result.TempFilePath);
    }

    private string WriteSource(string content, string fileName)
    {
        var directory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class StubTransferConnector : IAccTransferConnector
    {
        public List<AccFolderItem> FolderItems { get; } = new();
        public Dictionary<string, IReadOnlyDictionary<string, string?>> CustomAttributes { get; } =
            new(StringComparer.Ordinal);
        public List<IReadOnlyDictionary<string, string?>> AttributeWrites { get; } = new();
        public int UploadFileFinalCalls { get; private set; }
        public int UploadNewVersionCalls { get; private set; }

        public Task<string> EnsureFolderPathAsync(string projectId, string rootFolderId, IReadOnlyList<string> pathSegments, CancellationToken cancellationToken = default) =>
            Task.FromResult(pathSegments.Count == 0 ? rootFolderId : "PATH-FOLDER");

        public Task<IReadOnlyList<AccFolderItem>> GetFolderItemsAsync(string projectId, string folderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccFolderItem>>(FolderItems.ToList());

        public Task<UploadResult> UploadFileFinalAsync(string projectId, string folderId, string localSourcePath, string? displayName, CancellationToken cancellationToken = default)
        {
            UploadFileFinalCalls++;
            var itemId = $"ITEM-{UploadFileFinalCalls + UploadNewVersionCalls}";
            FolderItems.Add(new AccFolderItem { ItemId = itemId, DisplayName = displayName ?? Path.GetFileName(localSourcePath) });
            return Task.FromResult(new UploadResult(itemId, $"VER-{UploadFileFinalCalls + UploadNewVersionCalls}"));
        }

        public Task<UploadResult> UploadNewVersionAsync(string projectId, string folderId, string itemId, string localSourcePath, CancellationToken cancellationToken = default)
        {
            UploadNewVersionCalls++;
            return Task.FromResult(new UploadResult(itemId, $"VER-{UploadNewVersionCalls}"));
        }

        public Task<(string TempFilePath, string FileName)?> DownloadFileToTempAsync(string projectId, string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<(string TempFilePath, string FileName)?>(null);

        public Task<AccMetadataResult<IReadOnlyDictionary<string, string?>>> GetItemCustomAttributesAsync(string projectId, string itemId, CancellationToken cancellationToken = default)
        {
            var value = CustomAttributes.TryGetValue(itemId, out var attributes)
                ? attributes
                : new Dictionary<string, string?>(StringComparer.Ordinal);
            return Task.FromResult(AccMetadataResult<IReadOnlyDictionary<string, string?>>.Ok(value));
        }

        public Task<AccMetadataResult> SetItemCustomAttributesAsync(string projectId, string folderId, string versionId, IReadOnlyDictionary<string, string?> attributes, CancellationToken cancellationToken = default)
        {
            AttributeWrites.Add(new Dictionary<string, string?>(attributes, StringComparer.Ordinal));
            return Task.FromResult(AccMetadataResult.Ok());
        }
    }

    private sealed class InMemorySecretVaultStore : ISecretVaultStore
    {
        private readonly Dictionary<string, string> _secrets = [];

        public bool HasSecret(string key) => _secrets.ContainsKey(key);
        public string? GetSecret(string key) => _secrets.GetValueOrDefault(key);
        public void SetSecret(string key, string value) => _secrets[key] = value;
        public IReadOnlyDictionary<string, bool> GetVaultStatus() =>
            _secrets.Keys.ToDictionary(static key => key, static _ => true);
    }

    private sealed class StubAccServiceModeProvider(string? baseUrl) : IAccServiceModeProvider
    {
        public AccServiceMode Mode => string.IsNullOrWhiteSpace(BaseUrl) ? AccServiceMode.Local : AccServiceMode.Remote;
        public string? BaseUrl { get; } = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
