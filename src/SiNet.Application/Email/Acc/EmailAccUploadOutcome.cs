namespace SiNet.Application.Email.Acc;

public enum EmailAccUploadOutcome
{
    Succeeded = 0,
    AlreadyProcessed = 1,
    InProgress = 2,
    SkippedNoAttachments = 3,
    SkippedNotRelevant = 4,
    Failed = 5,
    BackendNotAvailable = 6,
}
