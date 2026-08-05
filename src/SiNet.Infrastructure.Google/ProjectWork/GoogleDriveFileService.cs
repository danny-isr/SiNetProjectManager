using Google;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Upload;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.ProjectWork;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace SiNet.Infrastructure.Google.ProjectWork;

/// <summary>
/// Shared-Drive-compliant Drive primitives backed by the shared user credential in
/// <see cref="GmailClientProvider"/>. No per-call OAuth.
/// </summary>
public sealed class GoogleDriveFileService : IGoogleDriveFileService
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private readonly GmailClientProvider _credentialProvider;
    private readonly GmailOptions _options;
    private readonly IAppLogger _logger;

    public GoogleDriveFileService(
        GmailClientProvider credentialProvider,
        GmailOptions options,
        IAppLogger logger)
    {
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> EnsureFolderPathAsync(
        IReadOnlyList<string> pathSegments,
        string rootFolderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathSegments);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolderId);

        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var parentId = rootFolderId;

        foreach (var segment in pathSegments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var existingId = await FindFolderAsync(drive, segment, parentId, cancellationToken).ConfigureAwait(false);
            parentId = existingId ?? await CreateFolderAsync(drive, segment, parentId, cancellationToken).ConfigureAwait(false);
        }

        return parentId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoogleDriveFileEntry>> ListFilesAsync(
        string parentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);

        var request = drive.Files.List();
        request.Q = $"'{parentId}' in parents and mimeType != '{FolderMimeType}' and trashed = false";
        ApplySharedDriveListFlags(request);
        request.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime)";
        request.PageSize = 1000;

        var all = new List<GoogleDriveFileEntry>();
        string? pageToken = null;
        try
        {
            do
            {
                request.PageToken = pageToken;
                var page = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (page.Files != null)
                {
                    foreach (var f in page.Files)
                        all.Add(ToEntry(f));
                }
                pageToken = page.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }

        return all;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoogleDriveFileEntry>> FindFilesByNameAsync(
        string fileName,
        string parentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);

        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var request = drive.Files.List();
        var escapedName = fileName.Replace("'", "\\'", StringComparison.Ordinal);
        request.Q = $"name = '{escapedName}' and mimeType != '{FolderMimeType}' and '{parentId}' in parents and trashed = false";
        ApplySharedDriveListFlags(request);
        request.Fields = "files(id, name, mimeType, size, modifiedTime)";

        try
        {
            var result = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return (result.Files ?? new List<DriveFile>()).Select(ToEntry).ToList();
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    /// <inheritdoc />
    public async Task<GoogleDriveFileEntry> UploadFileAsync(
        string parentId,
        string localFilePath,
        string targetName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var metadata = new DriveFile
        {
            Name = targetName,
            Parents = new List<string> { parentId },
        };

        await using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var request = drive.Files.Create(metadata, stream, "application/octet-stream");
        request.SupportsAllDrives = true;
        request.Fields = "id, name, mimeType, size, modifiedTime";

        try
        {
            var progress = await request.UploadAsync(cancellationToken).ConfigureAwait(false);
            if (progress.Status == UploadStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"Google Drive upload failed for '{targetName}': {progress.Exception?.Message}",
                    progress.Exception);
            }

            return ToEntry(request.ResponseBody
                ?? throw new InvalidOperationException($"Google Drive upload returned no metadata for '{targetName}'."));
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    /// <inheritdoc />
    public async Task<GoogleDriveFileEntry> UploadStringAsync(
        string parentId,
        string content,
        string targetName,
        string mimeType = "application/json",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var metadata = new DriveFile
        {
            Name = targetName,
            Parents = new List<string> { parentId },
        };

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var request = drive.Files.Create(metadata, stream, mimeType);
        request.SupportsAllDrives = true;
        request.Fields = "id, name, mimeType, size, modifiedTime";

        try
        {
            var progress = await request.UploadAsync(cancellationToken).ConfigureAwait(false);
            if (progress.Status == UploadStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"Google Drive upload failed for '{targetName}': {progress.Exception?.Message}",
                    progress.Exception);
            }

            return ToEntry(request.ResponseBody
                ?? throw new InvalidOperationException($"Google Drive upload returned no metadata for '{targetName}'."));
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    /// <inheritdoc />
    public async Task DownloadFileAsync(string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(destination);

        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var request = drive.Files.Get(fileId);
        request.SupportsAllDrives = true;

        try
        {
            await request.DownloadAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    /// <inheritdoc />
    public async Task TrashFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var update = new DriveFile { Trashed = true };
        var request = drive.Files.Update(update, fileId);
        request.SupportsAllDrives = true;
        request.Fields = "id, name, trashed";

        try
        {
            await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    /// <inheritdoc />
    public async Task<GoogleDriveFileEntry> RenameFileAsync(
        string fileId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var update = new DriveFile { Name = newName };
        var request = drive.Files.Update(update, fileId);
        request.SupportsAllDrives = true;
        request.Fields = "id, name, mimeType, size, modifiedTime";

        try
        {
            var file = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return ToEntry(file);
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    /// <inheritdoc />
    public async Task<string?> FindFolderIdByNameAsync(
        string folderName,
        string parentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);

        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        return await FindFolderAsync(drive, folderName, parentId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetParentIdsAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        var drive = await RequireDriveAsync(cancellationToken).ConfigureAwait(false);
        var request = drive.Files.Get(fileId);
        request.SupportsAllDrives = true;
        request.Fields = "id, parents";

        try
        {
            var file = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return file.Parents?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    private async Task<DriveService> RequireDriveAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsDriveConfigured)
        {
            throw new InvalidOperationException(
                "Google Drive is not configured. Set GoogleDrive:SharedDriveId and GoogleDrive:ProjectsRootFolderId.");
        }

        var drive = await _credentialProvider.TryGetDriveServiceAsync(cancellationToken).ConfigureAwait(false);
        if (drive is null)
        {
            throw new InvalidOperationException(
                "Google Drive is not available: the shared Google user session is not signed in. " +
                "Connect Google once via the connector auth flow; no per-window login is required.");
        }

        return drive;
    }

    private async Task<string?> FindFolderAsync(
        DriveService drive,
        string folderName,
        string parentId,
        CancellationToken cancellationToken)
    {
        var request = drive.Files.List();
        var escapedName = folderName.Replace("'", "\\'", StringComparison.Ordinal);
        request.Q = $"name = '{escapedName}' and mimeType = '{FolderMimeType}' and '{parentId}' in parents and trashed = false";
        ApplySharedDriveListFlags(request);
        request.Fields = "files(id, name)";

        try
        {
            var result = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return result.Files?.FirstOrDefault()?.Id;
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    private async Task<string> CreateFolderAsync(
        DriveService drive,
        string folderName,
        string parentId,
        CancellationToken cancellationToken)
    {
        var metadata = new DriveFile
        {
            Name = folderName,
            MimeType = FolderMimeType,
            Parents = new List<string> { parentId },
        };

        var request = drive.Files.Create(metadata);
        request.SupportsAllDrives = true;
        request.Fields = "id, name";

        try
        {
            var folder = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return folder.Id;
        }
        catch (GoogleApiException ex) when (IsInsufficientScope(ex))
        {
            throw CreateConsentRequired(ex);
        }
    }

    private void ApplySharedDriveListFlags(FilesResource.ListRequest request)
    {
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;
        request.Corpora = "drive";
        request.DriveId = _options.SharedDriveId;
    }

    private static GoogleDriveFileEntry ToEntry(DriveFile file) =>
        new(
            file.Id ?? string.Empty,
            file.Name ?? string.Empty,
            file.Size ?? 0,
            file.ModifiedTimeDateTimeOffset?.UtcDateTime);

    private static bool IsInsufficientScope(GoogleApiException ex)
    {
        if (ex.HttpStatusCode != System.Net.HttpStatusCode.Forbidden)
            return false;

        var text = (ex.Error?.Message ?? ex.Message ?? string.Empty).ToLowerInvariant();
        return text.Contains("insufficient", StringComparison.Ordinal)
               || text.Contains("access_not_configured", StringComparison.Ordinal)
               || text.Contains("accessnotconfigured", StringComparison.Ordinal)
               || text.Contains("permission", StringComparison.Ordinal) && text.Contains("scope", StringComparison.Ordinal);
    }

    private GoogleConsentRequiredException CreateConsentRequired(GoogleApiException ex)
    {
        _logger.Warn("[Google Drive] Operation failed: session lacks Drive scope. Interactive re-consent required.");
        return new GoogleConsentRequiredException(
            "The Google session does not include Drive permission yet. Sign in again once to grant Drive access. " +
            $"({ex.Message})");
    }
}
