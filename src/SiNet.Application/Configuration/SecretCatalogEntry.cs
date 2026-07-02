namespace SiNet.Application.Configuration;

public sealed record SecretCatalogEntry(
    string Key,
    string DisplayName,
    SecretKind Kind,
    bool IsSensitive,
    bool CanPrefill,
    SecretValidationGroup ValidationGroup,
    string? PairKey = null);
