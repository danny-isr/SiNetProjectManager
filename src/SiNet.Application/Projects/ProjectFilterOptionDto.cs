namespace SiNet.Application.Projects;

/// <summary>
/// A single selectable filter value for the shared Project Selector (Status / Job Type / User).
/// <see langword="null"/> <see cref="Id"/> represents the "all" option.
/// </summary>
/// <param name="Id">Stable identifier from the source table, or <see langword="null"/> for "all".</param>
/// <param name="DisplayName">Human-readable label shown in the filter ComboBox.</param>
public sealed record ProjectFilterOptionDto(int? Id, string DisplayName);
