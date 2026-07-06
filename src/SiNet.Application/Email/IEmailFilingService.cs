namespace SiNet.Application.Email;

/// <summary>
/// Application port for project filing side effects (Gmail labels, DB project assignment, workflow events).
/// <para>
/// <b>Design-only in V1.</b> No Infrastructure implementation is registered until write policy is
/// approved (<c>docs/GOOGLE_BOUNDARY.md</c>, <c>docs/NEW_SYSTEM_PRODUCTION_READINESS.md</c>).
/// The legacy seam is <c>EmailFilingService</c> / <c>EmailManagementService.FileToProjectAsync</c>
/// in SiNetSQL — a future slice wraps that logic here without exposing EF or Gmail types to WPF.
/// </para>
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
