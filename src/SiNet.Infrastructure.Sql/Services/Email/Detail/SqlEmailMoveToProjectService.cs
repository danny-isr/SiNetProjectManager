using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;

namespace SiNet.Infrastructure.Sql.Services.Email.Detail;

internal sealed class SqlEmailMoveToProjectService(IEmailMoveToProjectCoordinator coordinator)
    : IEmailMoveToProjectService
{
    private readonly IEmailMoveToProjectCoordinator _coordinator =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public bool IsAvailable => _coordinator.IsAvailable;

    public async Task<EmailMoveToProjectResult> MoveAsync(
        EmailMoveToProjectDetailCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _coordinator
            .MoveAsync(
                new EmailMoveToProjectCommand(
                    command.InboxMessageId,
                    command.ProjectId,
                    UserId: null,
                    command.TaskId),
                cancellationToken)
            .ConfigureAwait(false);

        return new EmailMoveToProjectResult(
            result.Outcome == EmailMoveToProjectOutcome.Succeeded,
            result.Message,
            result.MovedCount,
            result.AttachmentFailures,
            result.FailedCount,
            result.TotalCount,
            result.AlreadySameSourceCount);
    }
}
