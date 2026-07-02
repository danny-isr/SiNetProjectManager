namespace SiNet.Application.Configuration;

public sealed record SecretStatusDto(
    string Key,
    SecretStatusLevel Level,
    string? Detail,
    string? ToolTip);

public sealed record SecretSetupSnapshotDto(
    IReadOnlyDictionary<string, string?> PrefillValues,
    IReadOnlyDictionary<string, bool> ExistsInVault,
    string GoogleConfiguredDisplay);

public sealed record SecretSetupUpdateDto(
    IReadOnlyDictionary<string, string?> Updates);

public sealed record SecretValidationResultDto(
    string Key,
    string Label,
    bool Exists,
    bool Success,
    string? Detail,
    IReadOnlyList<string>? RelatedKeys = null);

public sealed record SecretSaveResultDto(
    int SavedCount,
    IReadOnlyList<SecretValidationResultDto> ValidationResults,
    bool AllPassed,
    IReadOnlyList<string> PassedSummaries,
    IReadOnlyList<string> FailedSummaries);
