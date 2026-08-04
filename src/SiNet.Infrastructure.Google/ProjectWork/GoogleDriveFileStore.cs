using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;

namespace SiNet.Infrastructure.Google.ProjectWork;

/// <summary>
/// Google Drive <see cref="IFileStore"/> for ProjectWork. Uses the shared user OAuth session
/// (<see cref="GmailClientProvider"/>) and a central Shared Drive / projects-root folder pair from
/// <see cref="GmailOptions"/>. Layout mirrors FileServer:
/// <c>ProjectsRoot / {projectRoot} / {ProjectFolder hierarchy} / {file}</c> with a
/// <c>{file}.si.json</c> sidecar beside each data file.
/// </summary>
public sealed class GoogleDriveFileStore : IFileStore
{
    private const string SidecarSuffix = ".si.json";
    private const string SidecarMimeType = "application/json";

    private readonly IGoogleDriveFileService _drive;
    private readonly IProjectDriveFolderResolver _folderResolver;
    private readonly GmailOptions _options;
    private readonly IAppLogger _logger;
    private readonly IProjectWorkScanExclusionPolicy _scanExclusions;

    public GoogleDriveFileStore(
        IGoogleDriveFileService drive,
        IProjectDriveFolderResolver folderResolver,
        GmailOptions options,
        IAppLogger logger,
        IProjectWorkScanExclusionPolicy? scanExclusions = null)
    {
        _drive = drive ?? throw new ArgumentNullException(nameof(drive));
        _folderResolver = folderResolver ?? throw new ArgumentNullException(nameof(folderResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scanExclusions = scanExclusions ?? new SettingsBackedProjectWorkScanExclusionPolicy();
    }

    /// <inheritdoc />
    public FileStorageDestination Destination => FileStorageDestination.GoogleDrive;

    /// <inheritdoc />
    public async Task<string?> ResolveFolderHandleAsync(
        int projectId,
        int projectFolderId,
        CancellationToken cancellationToken = default)
    {
        if (projectFolderId <= 0 || !_options.IsDriveConfigured)
            return null;

        var segments = await _folderResolver
            .ResolveRelativeSegmentsAsync(projectId, projectFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (segments is null || segments.Count == 0)
            return null;

        try
        {
            return await _drive
                .EnsureFolderPathAsync(segments, _options.ProjectsRootFolderId!, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GoogleConsentRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn(
                $"[GoogleDriveFileStore] ResolveFolderHandleAsync failed for projectId={projectId}, folderId={projectFolderId}: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ScannedFile> ListFilesAsync(
        string folderHandle,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderHandle) || !_options.IsDriveConfigured)
            yield break;

        IReadOnlyList<GoogleDriveFileEntry> files;
        try
        {
            files = await _drive.ListFilesAsync(folderHandle, cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleConsentRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[GoogleDriveFileStore] ListFilesAsync failed for folder '{folderHandle}': {ex.Message}");
            yield break;
        }

        var grouped = files.GroupBy(f => f.Name, StringComparer.Ordinal);
        foreach (var group in grouped)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var name = group.Key;
            if (string.IsNullOrEmpty(name))
                continue;
            // Sidecars hard-coded; lock/noise rules via settings-backed policy (DEV-006).
            if (IsMetadataCompanion(name) || _scanExclusions.ShouldExclude(name))
                continue;

            var list = group.ToList();
            if (list.Count > 1)
            {
                _logger.Warn(
                    $"[GoogleDriveFileStore] Duplicate filename '{name}' in folder '{folderHandle}' " +
                    $"({list.Count} candidates). Skipping in scan.");
                continue;
            }

            var f = list[0];
            yield return new ScannedFile(
                Source: FileStorageDestination.GoogleDrive,
                FileName: f.Name,
                NativeId: f.Id,
                SizeBytes: f.SizeBytes,
                LastModified: f.LastModifiedUtc,
                Parsed: ProjectFileNameParser.TryParse(f.Name),
                SourceFileName: null);

            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public async Task<string> DownloadToLocalAsync(ScannedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(file.NativeId))
            throw new InvalidOperationException("Cannot download Google Drive file: missing NativeId.");

        EnsureDriveConfigured();

        var safeName = MakeSafeLocalFileName(file.FileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"sinet-drive-{Guid.NewGuid():N}-{safeName}");
        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await _drive.DownloadFileAsync(file.NativeId, fs, cancellationToken).ConfigureAwait(false);
        }

        return tempPath;
    }

    /// <inheritdoc />
    public async Task<ScannedFile> UploadAsync(
        string folderHandle,
        string localSourcePath,
        string targetFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(localSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFileName);

        if (!File.Exists(localSourcePath))
            throw new FileNotFoundException("Source file not found for Drive upload.", localSourcePath);

        EnsureDriveConfigured();

        var fileName = Path.GetFileName(targetFileName);
        var sidecarName = fileName + SidecarSuffix;

        var existing = await _drive.FindFilesByNameAsync(fileName, folderHandle, cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            throw new FileStoreConflictException(
                $"Refusing to upload '{fileName}' to Drive folder '{folderHandle}': {existing.Count} file(s) with the same name already exist.");
        }

        var existingSidecars = await _drive.FindFilesByNameAsync(sidecarName, folderHandle, cancellationToken)
            .ConfigureAwait(false);
        if (existingSidecars.Count > 0)
        {
            throw new FileStoreConflictException(
                $"Refusing to upload sidecar '{sidecarName}': existing sidecar(s) present in folder '{folderHandle}'.");
        }

        var uploaded = await _drive
            .UploadFileAsync(folderHandle, localSourcePath, fileName, cancellationToken)
            .ConfigureAwait(false);

        var sidecar = new Dictionary<string, object?>
        {
            ["lastFileName"] = fileName,
            ["lastSizeBytes"] = new FileInfo(localSourcePath).Length,
            ["lastSavedUtc"] = File.GetLastWriteTimeUtc(localSourcePath),
            ["SourceFileName"] = Path.GetFileName(localSourcePath),
        };

        try
        {
            var json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
            await _drive.UploadStringAsync(folderHandle, json, sidecarName, SidecarMimeType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(
                $"[GoogleDriveFileStore] Data file '{fileName}' uploaded (id={uploaded.Id}) but sidecar write failed: {ex.Message}.");
            throw;
        }

        return new ScannedFile(
            Source: FileStorageDestination.GoogleDrive,
            FileName: uploaded.Name,
            NativeId: uploaded.Id,
            SizeBytes: uploaded.SizeBytes,
            LastModified: uploaded.LastModifiedUtc,
            Parsed: ProjectFileNameParser.TryParse(uploaded.Name),
            SourceFileName: Path.GetFileName(localSourcePath));
    }

    /// <inheritdoc />
    public async Task DeleteAsync(ScannedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(file.NativeId))
            throw new InvalidOperationException("Cannot delete Google Drive file: missing NativeId.");

        EnsureDriveConfigured();

        var parents = await _drive.GetParentIdsAsync(file.NativeId, cancellationToken).ConfigureAwait(false);
        var sidecarName = file.FileName + SidecarSuffix;

        await _drive.TrashFileAsync(file.NativeId, cancellationToken).ConfigureAwait(false);

        foreach (var parentId in parents)
        {
            try
            {
                var sidecars = await _drive.FindFilesByNameAsync(sidecarName, parentId, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var sidecar in sidecars)
                    await _drive.TrashFileAsync(sidecar.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[GoogleDriveFileStore] Sidecar trash for '{sidecarName}' failed: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public async Task<ScannedFile> RenameAsync(
        ScannedFile file,
        string newFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFileName);
        if (string.IsNullOrWhiteSpace(file.NativeId))
            throw new InvalidOperationException("Cannot rename Google Drive file: missing NativeId.");

        EnsureDriveConfigured();

        var newName = Path.GetFileName(newFileName);
        var oldSidecarName = file.FileName + SidecarSuffix;
        var newSidecarName = newName + SidecarSuffix;
        var parents = await _drive.GetParentIdsAsync(file.NativeId, cancellationToken).ConfigureAwait(false);

        var renamed = await _drive.RenameFileAsync(file.NativeId, newName, cancellationToken).ConfigureAwait(false);

        foreach (var parentId in parents)
        {
            try
            {
                var sidecars = await _drive.FindFilesByNameAsync(oldSidecarName, parentId, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var sidecar in sidecars)
                    await _drive.RenameFileAsync(sidecar.Id, newSidecarName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[GoogleDriveFileStore] Sidecar rename '{oldSidecarName}' → '{newSidecarName}' failed: {ex.Message}");
            }
        }

        return file with
        {
            FileName = renamed.Name,
            NativeId = renamed.Id,
            SizeBytes = renamed.SizeBytes,
            LastModified = renamed.LastModifiedUtc,
            Parsed = ProjectFileNameParser.TryParse(renamed.Name),
        };
    }

    private void EnsureDriveConfigured()
    {
        if (!_options.IsDriveConfigured)
        {
            throw new InvalidOperationException(
                "Google Drive is not configured. Set GoogleDrive:SharedDriveId and GoogleDrive:ProjectsRootFolderId.");
        }
    }

    private static bool IsMetadataCompanion(string fileName) =>
        fileName.EndsWith(SidecarSuffix, StringComparison.OrdinalIgnoreCase);

    private static string MakeSafeLocalFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "file";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }
}
