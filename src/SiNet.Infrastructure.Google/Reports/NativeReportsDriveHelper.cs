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

    /// <summary>
    /// Shared Drive can-add-children, matching the status-panel Shared Drive probe.
    /// Does <b>not</b> prove write on <see cref="EnsureFolderPathAsync"/>'s root folder — callers that
    /// write under a configured root should also call <see cref="CheckFolderWriteAccessAsync"/>.
    /// </summary>
    public async Task<bool> CheckWriteAccessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var get = _drive.Drives.Get(_sharedDriveId);
            get.Fields = "id,capabilities(canAddChildren)";
            var drive = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return drive.Capabilities?.CanAddChildren == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Folder-level write probe (<c>capabilities.canAddChildren</c>). Used for
    /// <c>ReportsRootFolderId</c> — Shared Drive write does not imply write on that folder.
    /// </summary>
    public async Task<bool> CheckFolderWriteAccessAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderId))
            return false;

        try
        {
            var get = _drive.Files.Get(folderId.Trim());
            get.SupportsAllDrives = true;
            get.Fields = "id,mimeType,capabilities(canAddChildren)";
            var file = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(file.MimeType, FolderMime, StringComparison.Ordinal))
                return false;
            return file.Capabilities?.CanAddChildren == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Hebrew reason when Shared Drive or reports-root write is denied; <c>null</c> when both pass.
    /// </summary>
    public async Task<string?> GetWriteAccessFailureReasonAsync(
        string? reportsRootFolderId,
        CancellationToken cancellationToken = default)
    {
        if (!await CheckWriteAccessAsync(cancellationToken).ConfigureAwait(false))
            return "אין הרשאות כתיבה ל-Shared Drive.";

        if (string.IsNullOrWhiteSpace(reportsRootFolderId))
            return "תיקיית שורש הדוחות לא הוגדרה.";

        if (!await CheckFolderWriteAccessAsync(reportsRootFolderId, cancellationToken).ConfigureAwait(false))
            return "אין הרשאת כתיבה לתיקיית שורש הדוחות.";

        return null;
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
