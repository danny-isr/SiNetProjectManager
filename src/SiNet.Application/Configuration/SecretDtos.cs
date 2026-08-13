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

public sealed record SecretExportResultDto(int ExportedCount, string Message);

public sealed record SecretImportPreviewItemDto(
    string Key,
    string DisplayName,
    bool ExistsInVault,
    bool IsKnown);

public sealed record SecretImportPreviewDto(
    IReadOnlyList<SecretImportPreviewItemDto> Items,
    int UnknownKeyCount,
    IReadOnlyList<string> UnknownKeys,
    int KeysToImportCount,
    IReadOnlyList<string> CatalogKeysAbsentFromFile);

public sealed record SecretImportResultDto(
    int ImportedCount,
    int SkippedCount,
    IReadOnlyList<string> SkippedSummaries,
    string Message,
    int AddedCount = 0,
    int UpdatedCount = 0,
    int DeletedCount = 0,
    IReadOnlyList<string>? DeletedKeys = null);

public sealed record AccServiceDiagnosticResultDto(
    bool Success,
    SecretStatusLevel StatusLevel,
    string Summary,
    bool IsNetworkTest,
    string? Detail);
