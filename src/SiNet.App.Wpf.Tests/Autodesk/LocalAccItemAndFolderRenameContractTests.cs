using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class LocalAccItemAndFolderRenameContractTests
{
    [Fact]
    public async Task LocalAccItemService_empty_itemId_returns_null_without_calling_connector()
    {
        var connector = new RecordingConnector();
        var sut = CreateItemService(connector);

        Assert.Null(await sut.GetTipVersionIdAsync("b.project", "  "));
        Assert.Equal(0, connector.TipVersionCalls);
    }

    [Fact]
    public async Task LocalAccItemService_forwards_trimmed_itemId_to_connector()
    {
        var connector = new RecordingConnector { TipVersionResult = "urn:adsk.wipprod:fs.file:vf.tip-1" };
        var sut = CreateItemService(connector);

        var tip = await sut.GetTipVersionIdAsync("project-1", "  item-1  ");

        Assert.Equal("urn:adsk.wipprod:fs.file:vf.tip-1", tip);
        Assert.Equal(("b.project-1", "item-1"), Assert.Single(connector.TipVersionRequests));
    }

    [Fact]
    public async Task LocalAccFolderRenameService_maps_connector_InvalidOperationException_to_Failed()
    {
        var connector = new RecordingConnector
        {
            RenameException = new InvalidOperationException("Error RenameFolder: HTTP 400 {\"errors\":[]}"),
        };
        var sut = CreateRenameService(connector);

        var outcome = await sut.RenameFolderAsync("b.project", "folder-1", "New Name");

        Assert.Equal(AccFolderRenameStatus.Failed, outcome.Status);
        Assert.Contains("Error RenameFolder: HTTP 400", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalAccFolderRenameService_rejects_blank_inputs_without_calling_connector()
    {
        var connector = new RecordingConnector();
        var sut = CreateRenameService(connector);

        var outcome = await sut.RenameFolderAsync("b.project", " ", "New Name");

        Assert.Equal(AccFolderRenameStatus.Failed, outcome.Status);
        Assert.Equal(0, connector.RenameCalls);
    }

    private static IAccItemService CreateItemService(IAccTransferConnector connector) =>
        new LocalAccItemService(connector);

    private static IAccFolderRenameService CreateRenameService(IAccTransferConnector connector) =>
        new LocalAccFolderRenameService(connector);

    private sealed class RecordingConnector : IAccTransferConnector
    {
        public string? TipVersionResult { get; set; }
        public Exception? RenameException { get; set; }
        public int TipVersionCalls { get; private set; }
        public int RenameCalls { get; private set; }
        public List<(string ProjectId, string ItemId)> TipVersionRequests { get; } = [];

        public Task<string> EnsureFolderPathAsync(
            string projectId,
            string rootFolderId,
            IReadOnlyList<string> pathSegments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(rootFolderId);

        public Task<IReadOnlyList<AccFolderItem>> GetFolderItemsAsync(
            string projectId,
            string folderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccFolderItem>>([]);

        public Task<string?> GetFolderByNameAsync(
            string projectId,
            string parentFolderId,
            string folderName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<UploadResult> UploadFileFinalAsync(
            string projectId,
            string folderId,
            string localSourcePath,
            string? displayName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UploadResult> UploadNewVersionAsync(
            string projectId,
            string folderId,
            string itemId,
            string localSourcePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(string TempFilePath, string FileName, string? TipVersionId)?> DownloadFileToTempAsync(
            string projectId,
            string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(string TempFilePath, string FileName, string? TipVersionId)?>(null);

        public Task<string?> GetItemTipVersionIdAsync(
            string projectId,
            string itemId,
            CancellationToken cancellationToken = default)
        {
            TipVersionCalls++;
            TipVersionRequests.Add((projectId, itemId));
            return Task.FromResult(TipVersionResult);
        }

        public Task<string?> GetItemDisplayNameAsync(
            string projectId,
            string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<int?> GetItemVersionCountAsync(
            string projectId,
            string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(null);

        public Task<bool> HideItemAsync(
            string projectId,
            string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task RenameFolderAsync(
            string projectId,
            string folderId,
            string newFolderName,
            CancellationToken cancellationToken = default)
        {
            RenameCalls++;
            if (RenameException is not null)
            {
                throw RenameException;
            }

            return Task.CompletedTask;
        }

        public Task<AccMetadataResult<IReadOnlyDictionary<string, string?>>> GetItemCustomAttributesAsync(
            string projectId,
            string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AccMetadataResult<IReadOnlyDictionary<string, string?>>.Ok(
                new Dictionary<string, string?>()));

        public Task<AccMetadataResult> SetItemCustomAttributesAsync(
            string projectId,
            string folderId,
            string versionId,
            IReadOnlyDictionary<string, string?> attributes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AccMetadataResult.Ok());
    }
}
