namespace SiNet.Application.Settings;

/// <summary>Personal status color row (user override UI).</summary>
public sealed record UserStatusColorEntryDto(
    int StatusId,
    string StatusName,
    bool IsOpen,
    string DefaultColorHex,
    string? OverrideColorHex,
    string ResolvedColorHex,
    bool HasOverride);

/// <summary>Global default color row (admin UI).</summary>
public sealed record GlobalStatusColorEntryDto(
    int StatusId,
    string StatusName,
    bool IsOpen,
    string ColorHex);
