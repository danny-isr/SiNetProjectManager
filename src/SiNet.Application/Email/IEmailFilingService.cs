namespace SiNet.Application.Email;

/// <summary>
/// Application port for project filing side effects (Gmail labels, DB project assignment, workflow events).
/// Implemented by <c>SqlEmailFilingService</c> and wired from <c>EmailListViewModel</c> context menu actions.
/// </summary>
public interface IEmailFilingService
{
    /// <summary>Files an inbox message to a target project (label + DB + optional workflow event).</summary>
    Task<EmailFilingResult> FileToProjectAsync(
        FileEmailToProjectCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Removes project filing for an inbox message.</summary>
    Task<EmailFilingResult> UnfileFromProjectAsync(
        UnfileEmailCommand command,
        CancellationToken cancellationToken = default);
}
