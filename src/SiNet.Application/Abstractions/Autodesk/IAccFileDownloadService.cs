namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Downloads an ACC item to a caller-owned local temp file for transfer/re-file style workflows.
/// </summary>
public interface IAccFileDownloadService
{
    Task<AccFileDownloadResult?> DownloadToTempAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of downloading an ACC item to a temp file.
/// </summary>
public sealed record AccFileDownloadResult(
    string TempFilePath,
    string DownloadedFileName);
