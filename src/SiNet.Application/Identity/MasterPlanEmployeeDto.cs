namespace SiNet.Application.Identity;

/// <summary>
/// MasterPlan employee row for user-admin ComboBox lookup.
/// <see cref="Id"/> is <see langword="null"/> for the "no mapping" placeholder item.
/// </summary>
public sealed record MasterPlanEmployeeDto(
    int? Id,
    string Name,
    string? Email = null,
    string? SourceDatabase = null);
