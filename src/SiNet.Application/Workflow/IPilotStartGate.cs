namespace SiNet.Application.Workflow;

/// <summary>
/// Defense-in-depth gate for new root workflow starts. Loads Pilot SystemSettings and evaluates
/// <see cref="PilotStartPolicy"/> for the acting user and workflow definition/code.
/// </summary>
public interface IPilotStartGate
{
    /// <summary>
    /// Throws when the acting user may not start the given workflow definition as a root instance.
    /// </summary>
    Task EnsureRootStartAllowedAsync(
        int actingUserId,
        int workflowDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates Pilot for a known workflow code (no DB lookup). Used by QuoteApproved pre-validation
    /// with the real <c>command.UserId</c>.
    /// </summary>
    Task<(bool Allowed, string? DenyReasonHebrew)> EvaluateAsync(
        int actingUserId,
        string workflowCode,
        CancellationToken cancellationToken = default);
}
