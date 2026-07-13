using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

public sealed record ProjectCreateDialogResult(
    bool Confirmed,
    int? ProjectId = null,
    string? ProjectTitle = null,
    string? PlaceTitle = null);

public interface IProjectCreateDialogFactory
{
    ProjectCreateDialogResult ShowDialog(Window? owner);
}

public sealed class ProjectCreateDialogFactory(IServiceProvider services) : IProjectCreateDialogFactory
{
    public ProjectCreateDialogResult ShowDialog(Window? owner)
    {
        var viewModel = services.GetRequiredService<ProjectCreateDialogViewModel>();
        var places = services.GetRequiredService<IPlaceCatalogService>();
        var companies = services.GetRequiredService<ICompanyCatalogService>();
        var window = new ProjectCreateDialogWindow(viewModel, places, companies) { Owner = owner };
        var dialogResult = window.ShowDialog();
        return new ProjectCreateDialogResult(
            dialogResult == true,
            viewModel.CreatedProjectId,
            viewModel.CreatedProjectTitle,
            viewModel.CreatedPlaceTitle);
    }
}
