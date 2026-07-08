namespace SiNet.Application.Email.Acc;

public sealed record EmailExternalDownloadItem(
    string FileName,
    string? AccItemId,
    string? AccFolderId,
    bool IsExternalDownload);
