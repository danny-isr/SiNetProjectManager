using System.Windows;
using SiNet.Application.Email.Detail;
using SiNet.Application.Projects;
using SiNetProjectManagerV2.Dialogs;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Services.Email;

/// <summary>
/// Modal project picker for filing an email — does not change the global current project.
/// </summary>
internal sealed class EmailFilingProjectPickerHost : IEmailFilingProjectPickerHost
{
    public bool IsAvailable => true;

    public Task<ProjectSummaryDto?> PickProjectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<ProjectSummaryDto?>(null);
        }

        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowDialog());
        }

        return dispatcher.InvokeAsync(ShowDialog).Task;
    }

    private static ProjectSummaryDto? ShowDialog()
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new ProjectSelectorDialog
        {
            Title = "בחירת פרויקט לשיוך המייל",
        };
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }

        var ok = dialog.ShowDialog();
        if (ok != true || dialog.SelectedProject is not { } project || project.Id <= 0)
        {
            return null;
        }

        return MapProject(project);
    }

    private static ProjectSummaryDto MapProject(Project project)
    {
        var number = project.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                     ?? project.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var name = !string.IsNullOrWhiteSpace(project.Title)
            ? project.Title!
            : project.NameAndNumber ?? $"Project #{project.Id}";

        return new ProjectSummaryDto(
            ProjectId: project.Id,
            ProjectNumber: number,
            ProjectName: name,
            PlaceName: project.Place?.Title,
            CompanyName: project.Company?.Title,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: project.EndOfProject != true,
            StatusId: project.ProjectStatusId,
            ProjectLabelName: project.NameAndNumber);
    }
}
