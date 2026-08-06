using System.Windows;
using System.Windows.Controls;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// Modularity tests: the shared selector is embeddable, host-agnostic, and free of feature-window logic.
/// </summary>
public sealed class ProjectSelectorModularityTests
{
    [Fact]
    public void View_is_standalone_user_control()
    {
        Assert.True(typeof(UserControl).IsAssignableFrom(typeof(ProjectSelectorView)));
    }

    [Fact]
    public void ViewModel_constructor_accepts_only_project_ports()
    {
        // Primary ctor: project ports + optional debounce / settings persistence (DEV-017 widths).
        var ctor = typeof(ProjectSelectorViewModel).GetConstructors()
            .Single(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length >= 3
                    && parameters[0].ParameterType == typeof(IProjectQueryService)
                    && parameters[1].ParameterType == typeof(IProjectFilterOptionsService)
                    && parameters[2].ParameterType == typeof(ICurrentProjectContext);
            });

        var parameters = ctor.GetParameters();
        Assert.Equal(typeof(IProjectQueryService), parameters[0].ParameterType);
        Assert.Equal(typeof(IProjectFilterOptionsService), parameters[1].ParameterType);
        Assert.Equal(typeof(ICurrentProjectContext), parameters[2].ParameterType);
        Assert.True(parameters.Length >= 3);
    }

    [Fact]
    public void ViewModel_exposes_no_feature_window_types()
    {
        var memberTypes = typeof(ProjectSelectorViewModel)
            .GetProperties()
            .Select(p => p.PropertyType)
            .Concat(typeof(ProjectSelectorViewModel).GetFields().Select(f => f.FieldType));

        foreach (var type in memberTypes)
        {
            Assert.DoesNotContain("Email", type.FullName ?? type.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("Workflow", type.FullName ?? type.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("NewShell", type.FullName ?? type.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void View_layout_dependency_properties_have_compact_embed_defaults()
    {
        Assert.Equal(340d, ProjectSelectorView.SearchBoxWidthProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(true, ProjectSelectorView.CompactModeProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(true, ProjectSelectorView.ShowFiltersProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(false, ProjectSelectorView.ShowUserFilterProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(true, ProjectSelectorView.ShowIncludeClosedProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(true, ProjectSelectorView.ShowExpandedResultsToggleProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(true, ProjectSelectorView.ShowRefreshButtonProperty.DefaultMetadata.DefaultValue);
        Assert.Equal(true, ProjectSelectorView.ShowStatusMessageProperty.DefaultMetadata.DefaultValue);
    }

    [Fact]
    public async Task Selection_still_publishes_to_current_project_context()
    {
        var project = new ProjectSummaryDto(
            ProjectId: 9,
            ProjectNumber: "1009",
            ProjectName: "Shared",
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true);

        var context = new InMemoryCurrentProjectContext();
        var sut = new ProjectSelectorViewModel(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            context,
            TimeSpan.Zero);

        await sut.LoadAsync();
        sut.SelectProjectCommand.Execute(project);

        Assert.Equal(9, context.CurrentProject!.ProjectId);
    }
}
