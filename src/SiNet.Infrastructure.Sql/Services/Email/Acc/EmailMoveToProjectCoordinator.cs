using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

public sealed class EmailMoveToProjectCoordinator(
    IEmailMoveToProjectExecutor? executor = null,
    IAppLogger? logger = null)
    : IEmailMoveToProjectCoordinator
{
    private readonly IEmailMoveToProjectExecutor? _executor = executor;
    private readonly IAppLogger? _logger = logger;

    public bool IsAvailable => _executor is not null;

    public Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_executor is null)
        {
            _logger?.Error(
                $"[MoveToProject] outcome=Failed kind=BackendNotAvailable inbox={command.InboxMessageId} project={command.ProjectId}");
            return Task.FromResult(new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.BackendNotAvailable,
                "MoveToProject backend is not configured."));
        }

        return _executor.MoveAsync(command, cancellationToken);
    }
}
