namespace SiNet.Application.Ai;

public enum AiCompletionFailureKind
{
    None,
    Cancelled,
    Unavailable,
    ProviderNotConfigured,
    Timeout,
    MalformedResponse,
    ProviderError,
}

/// <summary>Text completion outcome. Failures carry a Hebrew operator message, never raw exceptions.</summary>
public sealed record AiCompletionResult(
    bool Succeeded,
    string? Text,
    AiCompletionFailureKind Failure,
    string? UserMessageHe,
    string? ResolvedProvider,
    string? ResolvedModel)
{
    public static AiCompletionResult Ok(string text, string provider, string model) =>
        new(true, text, AiCompletionFailureKind.None, null, provider, model);

    public static AiCompletionResult Fail(
        AiCompletionFailureKind failure,
        string userMessageHe,
        string? provider = null,
        string? model = null) =>
        new(false, null, failure, userMessageHe, provider, model);
}
