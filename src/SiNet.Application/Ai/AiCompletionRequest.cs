namespace SiNet.Application.Ai;

/// <summary>Provider-agnostic completion request. No Email/Inspection types.</summary>
public sealed record AiCompletionRequest(
    string Prompt,
    AiCompletionLevel Level = AiCompletionLevel.Simple,
    string? SystemInstructions = null,
    bool ExpectJson = false,
    TimeSpan? Timeout = null);
