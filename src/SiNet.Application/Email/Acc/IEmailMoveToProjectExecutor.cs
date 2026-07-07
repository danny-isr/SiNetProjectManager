namespace SiNet.Application.Email.Acc;

/// <summary>Host-provided MoveToProject backend (legacy application service / process action).</summary>
public interface IEmailMoveToProjectExecutor
{
    Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken = default);
}
