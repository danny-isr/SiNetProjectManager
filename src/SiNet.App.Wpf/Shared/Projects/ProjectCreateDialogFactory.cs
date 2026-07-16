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

    /// <summary>
    /// Same as <see cref="ShowDialog(Window?)"/>, optionally linking the new project to an inbox email.
    /// </summary>
    ProjectCreateDialogResult ShowDialog(Window? owner, int? emailMessageId);
}

public sealed class ProjectCreateDialogFactory(IServiceProvider services) : IProjectCreateDialogFactory
{
    public ProjectCreateDialogResult ShowDialog(Window? owner) =>
        ShowDialog(owner, emailMessageId: null);

    public ProjectCreateDialogResult ShowDialog(Window? owner, int? emailMessageId)
    {
        var viewModel = services.GetRequiredService<ProjectCreateDialogViewModel>();
        viewModel.EmailMessageId = emailMessageId is > 0 ? emailMessageId : null;
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
