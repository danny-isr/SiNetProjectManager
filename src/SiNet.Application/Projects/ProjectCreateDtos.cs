namespace SiNet.Application.Projects;

public sealed record PlaceDto(int Id, string Title, string? CityIcon = null, bool InUse = true)
{
    /// <summary>Combo/list label: title with optional place number.</summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(CityIcon) ? Title : $"{Title} ({CityIcon})";
}

public sealed record CompanyDto(int Id, string Title, bool IsActive = true);

public sealed record ContactDto(int Id, int CompanyId, string DisplayName);

public sealed record JobTypeDto(int Id, string Title);

/// <summary>Selected job type on create, with optional admin worker + contract value.</summary>
public sealed record CreateProjectJobTypeLine(
    int JobTypeId,
    int? AdminWorkerId = null,
    decimal BidValue = 0m);

public sealed record CreateProjectCommand(
    string Title,
    int PlaceId,
    int CompanyId,
    int ContactId,
    IReadOnlyList<int> JobTypeIds,
    int? ParentProjectId = null,
    int? EmailMessageId = null,
    string? ApproveDescription = null,
    IReadOnlyList<CreateProjectJobTypeLine>? JobTypeLines = null);

public sealed record CreateProjectResult(
    bool Succeeded,
    int? ProjectId = null,
    string? ProjectTitle = null,
    string? PlaceTitle = null,
    string? ErrorMessage = null,
    string? WarningMessage = null)
{
    public static CreateProjectResult Ok(int projectId, string title, string? placeTitle, string? warningMessage = null) =>
        new(true, projectId, title, placeTitle, WarningMessage: warningMessage);

    public static CreateProjectResult Fail(string errorMessage) =>
        new(false, ErrorMessage: errorMessage);
}
