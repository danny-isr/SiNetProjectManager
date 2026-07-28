using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using File = Google.Apis.Drive.v3.Data.File;

namespace SiNet.Infrastructure.Google.Reports;

/// <summary>Shared-Drive helpers for MasterPlan report folders/files.</summary>
public sealed class NativeReportsDriveHelper
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string SpreadsheetMime = "application/vnd.google-apps.spreadsheet";

    private readonly DriveService _drive;
    private readonly string _sharedDriveId;

    public NativeReportsDriveHelper(DriveService drive, string sharedDriveId)
    {
        _drive = drive ?? throw new ArgumentNullException(nameof(drive));
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedDriveId);
        _sharedDriveId = sharedDriveId;
    }

    public async Task<bool> CheckWriteAccessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var drive = await _drive.Drives.Get(_sharedDriveId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return drive.Capabilities?.CanAddChildren == true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> EnsureFolderPathAsync(
        string[] pathSegments,
        string? rootFolderId,
        CancellationToken cancellationToken = default)
    {
        var parentId = string.IsNullOrWhiteSpace(rootFolderId) ? _sharedDriveId : rootFolderId;
        foreach (var segment in pathSegments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var existing = await FindFolderAsync(segment, parentId, cancellationToken).ConfigureAwait(false);
            parentId = existing ?? await CreateFolderAsync(segment, parentId, cancellationToken).ConfigureAwait(false);
        }

        return parentId;
    }

    public async Task<string?> FindFolderAsync(
        string folderName,
        string parentId,
        CancellationToken cancellationToken = default)
    {
        var q = $"mimeType='{FolderMime}' and name='{Escape(folderName)}' and '{parentId}' in parents and trashed=false";
        var list = _drive.Files.List();
        list.Q = q;
        list.Spaces = "drive";
        list.Corpora = "drive";
        list.DriveId = _sharedDriveId;
        list.SupportsAllDrives = true;
        list.IncludeItemsFromAllDrives = true;
        list.Fields = "files(id,name)";
        list.PageSize = 10;
        var result = await list.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return result.Files?.FirstOrDefault()?.Id;
    }

    public async Task<string?> FindFileAsync(
        string fileName,
        string parentId,
        CancellationToken cancellationToken = default)
    {
        var q =
            $"mimeType='{SpreadsheetMime}' and name='{Escape(fileName)}' and '{parentId}' in parents and trashed=false";
        var list = _drive.Files.List();
        list.Q = q;
        list.Spaces = "drive";
        list.Corpora = "drive";
        list.DriveId = _sharedDriveId;
        list.SupportsAllDrives = true;
        list.IncludeItemsFromAllDrives = true;
        list.Fields = "files(id,name)";
        list.PageSize = 10;
        var result = await list.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return result.Files?.FirstOrDefault()?.Id;
    }

    public async Task<string> CreateFolderAsync(
        string folderName,
        string parentId,
        CancellationToken cancellationToken = default)
    {
        var meta = new File
        {
            Name = folderName,
            MimeType = FolderMime,
            Parents = new List<string> { parentId },
        };
        var create = _drive.Files.Create(meta);
        create.SupportsAllDrives = true;
        create.Fields = "id";
        var created = await create.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return created.Id ?? throw new InvalidOperationException("Drive folder create returned no id.");
    }

    public async Task<string> CreateSpreadsheetAsync(
        string fileName,
        string parentFolderId,
        CancellationToken cancellationToken = default)
    {
        var meta = new File
        {
            Name = fileName,
            MimeType = SpreadsheetMime,
            Parents = new List<string> { parentFolderId },
        };
        var create = _drive.Files.Create(meta);
        create.SupportsAllDrives = true;
        create.Fields = "id";
        var created = await create.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return created.Id ?? throw new InvalidOperationException("Spreadsheet create returned no id.");
    }

    public async Task<string> CopyTemplateAsync(
        string templateFileId,
        string newFileName,
        string parentFolderId,
        CancellationToken cancellationToken = default)
    {
        var copy = _drive.Files.Copy(
            new File { Name = newFileName, Parents = new List<string> { parentFolderId } },
            templateFileId);
        copy.SupportsAllDrives = true;
        copy.Fields = "id";
        var created = await copy.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return created.Id ?? throw new InvalidOperationException("Template copy returned no id.");
    }

    public async Task<string> GetFileUrlAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var get = _drive.Files.Get(fileId);
        get.SupportsAllDrives = true;
        get.Fields = "webViewLink,id";
        var file = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return file.WebViewLink
               ?? $"https://docs.google.com/spreadsheets/d/{fileId}/edit";
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
