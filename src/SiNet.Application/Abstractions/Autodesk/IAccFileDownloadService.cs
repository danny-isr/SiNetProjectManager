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
/// <param name="TempFilePath">Caller-owned temp path.</param>
/// <param name="DownloadedFileName">Display / original file name from ACC tip.</param>
/// <param name="TipVersionId">Tip version URN from the same tip response used for download (when known).</param>
public sealed record AccFileDownloadResult(
    string TempFilePath,
    string DownloadedFileName,
    string? TipVersionId = null);
