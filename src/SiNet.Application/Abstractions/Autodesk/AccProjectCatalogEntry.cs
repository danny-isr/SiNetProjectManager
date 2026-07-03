namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Read-only ACC project catalog entry for operator selection.
/// Display names may come from cached SQL metadata rather than a live ACC lookup.
/// </summary>
public sealed record AccProjectCatalogEntry(
    string ProjectId,
    string DisplayName,
    string SourceLabel)
{
    public string DisplayText =>
        string.IsNullOrWhiteSpace(DisplayName)
        || string.Equals(DisplayName.Trim(), ProjectId.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ProjectId.Trim()
            : $"{DisplayName.Trim()} ({ProjectId.Trim()})";
}
