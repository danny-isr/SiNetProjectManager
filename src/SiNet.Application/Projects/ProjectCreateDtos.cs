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

public sealed record CreateProjectCommand(
    string Title,
    int PlaceId,
    int CompanyId,
    int ContactId,
    IReadOnlyList<int> JobTypeIds,
    int? ParentProjectId = null,
    int? EmailMessageId = null);

public sealed record CreateProjectResult(
    bool Succeeded,
    int? ProjectId = null,
    string? ProjectTitle = null,
    string? PlaceTitle = null,
    string? ErrorMessage = null)
{
    public static CreateProjectResult Ok(int projectId, string title, string? placeTitle) =>
        new(true, projectId, title, placeTitle);

    public static CreateProjectResult Fail(string errorMessage) =>
        new(false, ErrorMessage: errorMessage);
}
