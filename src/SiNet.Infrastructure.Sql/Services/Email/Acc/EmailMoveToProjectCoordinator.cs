using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

public sealed class EmailMoveToProjectCoordinator(IEmailMoveToProjectExecutor? executor = null)
    : IEmailMoveToProjectCoordinator
{
    private readonly IEmailMoveToProjectExecutor? _executor = executor;

    public bool IsAvailable => _executor is not null;

    public Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_executor is null)
        {
            return Task.FromResult(new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.BackendNotAvailable,
                "MoveToProject backend is not configured."));
        }

        return _executor.MoveAsync(command, cancellationToken);
    }
}
