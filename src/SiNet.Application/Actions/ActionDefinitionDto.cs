namespace SiNet.Application.Actions;

/// <summary>Read-only action catalog entry for foundation documentation and UI gating.</summary>
public sealed record ActionDefinitionDto(
    string Code,
    string Category,
    bool IsFoundationReady,
    string? Notes = null);
