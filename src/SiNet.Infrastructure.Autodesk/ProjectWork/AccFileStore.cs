using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Autodesk.ProjectWork;

/// <summary>
/// Read-only <see cref="IFileStore"/> over Autodesk Construction Cloud, built on the clean ACC ports
/// (<see cref="IAccFolderBrowserService"/>, <see cref="IAccFolderPathService"/>,
/// <see cref="IAccFileDownloadService"/>). Clean-layer port of the read half of the legacy
/// <c>SiNetSQL.FileIndex.Stores.AccFileStore</c>.
/// <para>
/// The <c>folderHandle</c> has the format <c>"{accProjectId}|{accFolderId}"</c>. Uploads are gated by
/// the ACC-write policy and fail-fast until the write phase is enabled.
/// </para>
/// </summary>
public sealed class AccFileStore : IFileStore
{
    private const string ProjectRootFolderTitle = "\u05EA\u05D9\u05E7\u05D9\u05EA \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8";
    private const int MaxDepth = 32;

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IAccFolderPathService _folderPathService;
    private readonly IAccFolderBrowserService _folderBrowserService;
    private readonly IAccFileDownloadService _downloadService;
    private readonly IAccWritePolicy _writePolicy;
    private readonly IAccFileUploadService _uploadService;
    private readonly IAccItemService _itemService;

    public AccFileStore(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IAccFolderPathService folderPathService,
        IAccFolderBrowserService folderBrowserService,
        IAccFileDownloadService downloadService,
        IAccWritePolicy writePolicy,
        IAccFileUploadService uploadService,
        IAccItemService itemService)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(folderPathService);
        ArgumentNullException.ThrowIfNull(folderBrowserService);
        ArgumentNullException.ThrowIfNull(downloadService);
        ArgumentNullException.ThrowIfNull(writePolicy);
        ArgumentNullException.ThrowIfNull(uploadService);
        ArgumentNullException.ThrowIfNull(itemService);
        _dbFactory = dbFactory;
        _folderPathService = folderPathService;
        _folderBrowserService = folderBrowserService;
        _downloadService = downloadService;
        _writePolicy = writePolicy;
        _uploadService = uploadService;
        _itemService = itemService;
    }

    /// <inheritdoc />
    public FileStorageDestination Destination => FileStorageDestination.Acc;

    /// <inheritdoc />
    public async Task<string?> ResolveFolderHandleAsync(int projectId, int projectFolderId, CancellationToken cancellationToken = default)
    {
        if (projectFolderId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var mapping = await db.ProjectAccMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (mapping is null
            || string.IsNullOrEmpty(mapping.AccProjectId)
            || string.IsNullOrEmpty(mapping.AccTargetFolderId))
        {
            return null;
        }

        var segments = await ResolveFolderSegmentsAsync(db, projectFolderId, cancellationToken).ConfigureAwait(false);

        var accFolderId = mapping.AccTargetFolderId!;
        if (segments.Count > 0)
        {
            // Read-only walk: do NOT create missing folders (that is a gated write operation).
            var resolved = await _folderPathService
                .TryResolvePathAsync(mapping.AccProjectId!, accFolderId, segments, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(resolved))
                return null;
            accFolderId = resolved;
        }

        return $"{mapping.AccProjectId}|{accFolderId}";
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ScannedFile> ListFilesAsync(
        string folderHandle,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryParseHandle(folderHandle, out var accProjectId, out var accFolderId))
            yield break;

        AccFolderBrowseResult? result;
        try
        {
            result = await _folderBrowserService.BrowseAsync(accProjectId, accFolderId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            yield break;
        }

        if (result is null)
            yield break;

        var viewerUrl = BuildFolderViewerUrl(accProjectId, accFolderId);
        // #region agent log
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
            "ProjectWork.AccUrl",
            $"ListFiles folder-only url hasEntityId={viewerUrl.Contains("entityId=", StringComparison.Ordinal)} folderIdLen={accFolderId.Length}");
        // #endregion

        foreach (var entry in result.Entries)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (entry.Kind != AccFolderEntryKind.Item)
                continue;

            yield return new ScannedFile(
                Source: FileStorageDestination.Acc,
                FileName: entry.DisplayName,
                NativeId: entry.Id,
                SizeBytes: entry.FileSize,
                LastModified: entry.LastModifiedTime,
                Parsed: ProjectFileNameParser.TryParse(entry.DisplayName),
                AccViewerUrl: viewerUrl,
                AccProjectId: accProjectId);
        }
    }

    /// <inheritdoc />
    public async Task<string> DownloadToLocalAsync(ScannedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrEmpty(file.AccProjectId))
            throw new InvalidOperationException("ACC download requires the owning ACC project id on the scanned file.");

        var download = await _downloadService
            .DownloadToTempAsync(file.AccProjectId, file.NativeId, cancellationToken)
            .ConfigureAwait(false);
        if (download is null)
            throw new InvalidOperationException($"ACC item '{file.NativeId}' could not be downloaded.");

        return download.TempFilePath;
    }

    /// <inheritdoc />
    public async Task<ScannedFile> UploadAsync(
        string folderHandle,
        string localSourcePath,
        string targetFileName,
        CancellationToken cancellationToken = default)
    {
        _writePolicy.EnsureWriteAllowed("acc-upload");

        if (!TryParseHandle(folderHandle, out var accProjectId, out var accFolderId))
            throw new InvalidOperationException($"Invalid ACC folder handle: '{folderHandle}'.");

        var request = new AccFileUploadRequest(accProjectId, localSourcePath, targetFileName)
        {
            TargetFolderId = accFolderId,
        };

        var result = await _uploadService.UploadAsync(request, cancellationToken).ConfigureAwait(false);

        return new ScannedFile(
            Source: FileStorageDestination.Acc,
            FileName: string.IsNullOrEmpty(result.FileName) ? targetFileName : result.FileName,
            NativeId: result.ItemId,
            SizeBytes: 0,
            LastModified: DateTime.UtcNow,
            Parsed: ProjectFileNameParser.TryParse(targetFileName),
            AccViewerUrl: BuildFolderViewerUrl(accProjectId, accFolderId),
            AccProjectId: accProjectId);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(ScannedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        _writePolicy.EnsureWriteAllowed("acc-delete");

        if (string.IsNullOrEmpty(file.AccProjectId))
            throw new InvalidOperationException("ACC delete requires the owning ACC project id on the scanned file.");

        // ACC "delete" is a soft hide of the item lineage (parity with the legacy file-tree delete).
        await _itemService.HideAsync(file.AccProjectId, file.NativeId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ScannedFile> RenameAsync(ScannedFile file, string newFileName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Renaming an ACC item is not supported from ProjectWork; ACC items keep their lineage filename.");

    private async Task<IReadOnlyList<string>> ResolveFolderSegmentsAsync(
        SiNetSQLDbContext db,
        int projectFolderId,
        CancellationToken cancellationToken)
    {
        var segments = new List<string>();
        var currentId = projectFolderId;

        for (var safety = 0; safety < MaxDepth; safety++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var folder = await db.ProjectFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == currentId, cancellationToken)
                .ConfigureAwait(false);
            if (folder is null)
                break;

            if (folder.Title == ProjectRootFolderTitle)
                break;

            if (!string.IsNullOrWhiteSpace(folder.Title))
                segments.Add(folder.Title!);

            if (!folder.Infolderid.HasValue || folder.Infolderid.Value == folder.Id)
                break;
            currentId = folder.Infolderid.Value;
        }

        segments.Reverse();
        return segments;
    }

    private static bool TryParseHandle(string? folderHandle, out string accProjectId, out string accFolderId)
    {
        accProjectId = string.Empty;
        accFolderId = string.Empty;
        if (string.IsNullOrWhiteSpace(folderHandle))
            return false;

        var parts = folderHandle.Split('|', 2);
        if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
            return false;

        accProjectId = parts[0];
        accFolderId = parts[1];
        return true;
    }

    private static string BuildFolderViewerUrl(string accProjectId, string accFolderId)
    {
        var guid = accProjectId.StartsWith("b.", StringComparison.Ordinal) ? accProjectId[2..] : accProjectId;
        return $"https://acc.autodesk.com/docs/files/projects/{guid}?folderUrn={Uri.EscapeDataString(accFolderId)}";
    }
}
