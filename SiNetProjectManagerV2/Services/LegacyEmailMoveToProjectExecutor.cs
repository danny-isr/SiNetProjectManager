using SiNet.Application.Email.Acc;
using SiNetSQL.Domain.Actions;
using SiNetSQL.Domain.Actions.Handlers;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host bridge: delegates MoveToProject to the legacy process action dispatcher + handler.
/// </summary>
internal sealed class LegacyEmailMoveToProjectExecutor(IProcessActionDispatcher dispatcher)
    : IEmailMoveToProjectExecutor
{
    private readonly IProcessActionDispatcher _dispatcher =
        dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
        EmailMoveToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = new ActionExecutionContext
        {
            ActionCode = ActionCodes.MoveToProject,
            EmailMessageId = command.InboxMessageId,
            ProjectId = command.ProjectId,
            UserId = command.UserId,
            TaskId = command.TaskId,
        };

        var result = await _dispatcher.DispatchAsync(context, cancellationToken).ConfigureAwait(false);

        var movedCount = TryReadInt(result.Data, "MovedCount");
        var failedCount = TryReadInt(result.Data, "FailedCount");

        return result.Status switch
        {
            ActionExecutionStatus.Completed => new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.Succeeded,
                result.Message ?? "MoveToProject completed.",
                movedCount,
                failedCount),
            ActionExecutionStatus.Deferred => new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.DeferredRequiresUi,
                result.Message ?? "MoveToProject requires UI."),
            _ => new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.Failed,
                result.Message ?? "MoveToProject failed.",
                movedCount,
                failedCount),
        };
    }

    private static int TryReadInt(IReadOnlyDictionary<string, object?>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : 0,
        };
    }
}
