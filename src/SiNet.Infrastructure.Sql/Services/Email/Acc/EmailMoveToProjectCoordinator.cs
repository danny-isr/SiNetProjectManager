using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email.Acc;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

public sealed class EmailMoveToProjectCoordinator(
    IEmailMoveToProjectExecutor? executor = null,
    IAppLogger? logger = null,
    IIdentityOperationGuard? identityGuard = null)
    : IEmailMoveToProjectCoordinator
{
    private readonly IEmailMoveToProjectExecutor? _executor = executor;
    private readonly IAppLogger? _logger = logger;
    private readonly IIdentityOperationGuard? _identityGuard = identityGuard;

    public bool IsAvailable => _executor is not null;

    public async Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_executor is null)
        {
            _logger?.Error(
                $"[MoveToProject] outcome=Failed kind=BackendNotAvailable inbox={command.InboxMessageId} project={command.ProjectId}");
            return new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.BackendNotAvailable,
                "MoveToProject backend is not configured.");
        }

        if (_identityGuard is not null)
        {
            var decision = await _identityGuard
                .EvaluateAsync(IdentityOperationKind.AccFileWrite, cancellationToken)
                .ConfigureAwait(false);
            if (!decision.Allowed)
            {
                _logger?.Warn(
                    $"[MoveToProject] blocked by identity guard: {decision.Reason}");
                return new EmailMoveToProjectCoordinatorResult(
                    EmailMoveToProjectOutcome.Failed,
                    decision.Reason ?? "Identity operation denied.");
            }
        }

        return await _executor.MoveAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
