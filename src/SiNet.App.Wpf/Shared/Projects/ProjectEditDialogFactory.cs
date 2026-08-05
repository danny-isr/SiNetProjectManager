using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Identity;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

public sealed record ProjectEditDialogResult(bool Confirmed, int ProjectId);

public interface IProjectEditDialogFactory
{
    Task<ProjectEditDialogResult> ShowDialogAsync(Window? owner, int projectId, CancellationToken cancellationToken = default);
}

public sealed class ProjectEditDialogFactory(IServiceProvider services) : IProjectEditDialogFactory
{
    public async Task<ProjectEditDialogResult> ShowDialogAsync(
        Window? owner,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var auth = services.GetService<IAuthorizationQueryService>();
        if (auth is not null)
        {
            var allowed = await auth
                .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.ProjectUpdate, cancellationToken)
                .ConfigureAwait(true);
            if (!allowed)
            {
                MessageBox.Show(
                    "אין הרשאה לעדכון פרויקט (Project.Update).",
                    "עדכון פרויקט",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return new ProjectEditDialogResult(false, projectId);
            }
        }

        var viewModel = services.GetRequiredService<ProjectEditDialogViewModel>();
        var places = services.GetRequiredService<IPlaceCatalogService>();
        var companies = services.GetRequiredService<ICompanyCatalogService>();
        var window = new ProjectEditDialogWindow(viewModel, places, companies) { Owner = owner };
        await window.InitializeForProjectAsync(projectId, cancellationToken).ConfigureAwait(true);
        var dialogResult = window.ShowDialog();
        return new ProjectEditDialogResult(dialogResult == true, projectId);
    }
}
