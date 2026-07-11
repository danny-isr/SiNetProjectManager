namespace SiNet.Application.Email.Acc;

public enum EmailExternalDownloadStage
{
    Downloading,
    Extracting,
    Uploading,
    Completed,
    Failed,
}

/// <summary>UI progress for external link download → ACC upload pipeline.</summary>
public sealed record EmailExternalDownloadProgress(
    EmailExternalDownloadStage Stage,
    string Message,
    int? Percent = null,
    int? CurrentFile = null,
    int? TotalFiles = null,
    string? FileName = null);
