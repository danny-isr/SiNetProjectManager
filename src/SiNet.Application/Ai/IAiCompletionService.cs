namespace SiNet.Application.Ai;

/// <summary>
/// Generic text completion. Settings resolve provider+model for the requested level.
/// </summary>
public interface IAiCompletionService
{
    Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(
        AiCompletionLevel level = AiCompletionLevel.Simple,
        CancellationToken cancellationToken = default);
}
