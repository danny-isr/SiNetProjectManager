using SiNet.Application.Tasks;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Native Infrastructure.Sql implementation of <see cref="ITaskCompletionMetadataResolver"/> backed
/// by the migrated declarative <see cref="ReviewCompletionEventBehavior"/> table.
/// </summary>
public sealed class SqlTaskCompletionMetadataResolver : ITaskCompletionMetadataResolver
{
    public string? ResolveCompletionEventCode(string taskTypeCode, string? taskResultCode)
    {
        return ReviewCompletionEventBehavior.TryResolveEventCodeForTaskTypeAndResult(
            taskTypeCode,
            taskResultCode,
            out var completionEventCode)
            ? completionEventCode
            : null;
    }
}
