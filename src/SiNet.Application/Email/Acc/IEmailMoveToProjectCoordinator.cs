namespace SiNet.Application.Email.Acc;

/// <summary>
/// Application port for MoveToProject from the Email Workbench. Host registers the legacy handler bridge.
/// </summary>
public interface IEmailMoveToProjectCoordinator
{
    Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken = default);

    bool IsAvailable { get; }
}
