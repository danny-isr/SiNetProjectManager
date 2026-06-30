using SiNet.Application.Tasks;
using SiNetSQL.Services.Tasks;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter that implements the new <see cref="ITaskCompletionMetadataResolver"/> Application port
/// by delegating to the legacy declarative <see cref="ReviewCompletionEventBehavior"/> mapping.
/// <para>
/// This is the single place that bridges the new clean port to the legacy completion-behavior table.
/// It owns no mapping of its own and makes no workflow decisions — it only translates a
/// <c>(task type, result)</c> pair into the unique completion event code the
/// <c>TaskCompletionCoordinator</c> already validates against. When the pair is unknown, unsupported,
/// or ambiguous the legacy helper resolves to <see langword="false"/> and this adapter returns
/// <see langword="null"/> so the caller blocks completion instead of guessing.
/// </para>
/// </summary>
internal sealed class TaskCompletionMetadataResolver : ITaskCompletionMetadataResolver
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
