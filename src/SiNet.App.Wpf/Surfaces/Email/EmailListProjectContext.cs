namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Project context supplied by the Email Workbench host (not owned by <see cref="EmailListViewModel"/>).
/// Drives project-scoped email grouping via Gmail project labels.
/// </summary>
public sealed record EmailListProjectContext(
    int ProjectId,
    string? ProjectNumber,
    string? ProjectName,
    string? ProjectLabelName,
    string? LocationName = null)
{
    public string GroupHeaderDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ProjectNumber) && !string.IsNullOrWhiteSpace(ProjectName))
            {
                return $"{ProjectNumber} — {ProjectName}";
            }

            if (!string.IsNullOrWhiteSpace(ProjectLabelName))
            {
                return ProjectLabelName;
            }

            return ProjectId > 0 ? $"פרויקט #{ProjectId}" : "פרויקט";
        }
    }
}
