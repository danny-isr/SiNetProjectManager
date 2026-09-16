using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailFilingAiRecommendationsViewModelTests
{
    [Fact]
    public void Apply_ignores_stale_generation()
    {
        var session = new EmailProjectPickerAiSession();
        var first = session.Begin();
        session.Begin();
        var sut = new EmailFilingAiRecommendationsViewModel();
        var project = new ProjectSummaryDto(1, "1", "Tower", null, null, null, null, null, true);
        sut.BeginLoading();

        sut.Apply(
            new EmailProjectRecommendationResult(
                [new EmailProjectRecommendation(project, 0.9, "x")],
                null,
                1,
                1,
                false,
                10,
                TimeSpan.Zero),
            first,
            session);

        Assert.Empty(sut.Items);
        Assert.True(sut.IsBusy);
    }

    [Fact]
    public void ChooseCommand_raises_real_project_without_filing()
    {
        var session = new EmailProjectPickerAiSession();
        var generation = session.Begin();
        var sut = new EmailFilingAiRecommendationsViewModel();
        var project = new ProjectSummaryDto(8, "8", "Tower", "Haifa", null, null, null, null, true);
        ProjectSummaryDto? chosen = null;
        sut.ProjectChosen += p => chosen = p;

        sut.Apply(
            new EmailProjectRecommendationResult(
                [new EmailProjectRecommendation(project, 0.8, "match")],
                null,
                1,
                1,
                false,
                10,
                TimeSpan.Zero),
            generation,
            session);
        sut.ChooseCommand.Execute(sut.Items[0]);

        Assert.Same(project, chosen);
        Assert.False(sut.IsBusy);
        Assert.Null(sut.StatusMessageHe);
    }

    [Fact]
    public void ApplyLocal_selects_same_dto_and_does_not_file()
    {
        var sut = new EmailFilingAiRecommendationsViewModel();
        var project = new ProjectSummaryDto(8, "8", "Tower", "Haifa", "Acme", null, null, null, true);
        ProjectSummaryDto? chosen = null;
        sut.ProjectChosen += p => chosen = p;

        sut.ApplyLocal([new EmailProjectSuggestion(project, "Haifa · Acme")]);
        sut.ChooseCommand.Execute(sut.Items[0]);

        Assert.Same(project, chosen);
        Assert.False(sut.IsBusy);
        Assert.Null(sut.StatusMessageHe);
        Assert.True(sut.HasItems);
        Assert.Equal("Haifa · Acme", sut.Items[0].SecondaryText);
    }
}
