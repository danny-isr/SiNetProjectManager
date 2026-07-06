namespace SiNet.Application.Email;

/// <summary>Command to file an inbox message under a project label.</summary>
public sealed record FileEmailToProjectCommand(
    int InboxMessageId,
    int TargetProjectId,
    int ActingUserId,
    int? TaskId = null,
    string? TaskResultCode = null);

/// <summary>Command to remove project filing from an inbox message.</summary>
public sealed record UnfileEmailCommand(
    int InboxMessageId,
    int ActingUserId,
    int? TaskId = null);

/// <summary>Outcome of a filing attempt (success or structured failure).</summary>
public sealed record EmailFilingResult(
    bool Succeeded,
    string? ErrorMessage = null,
    int? AssignedProjectId = null);
