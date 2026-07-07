using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

/// <summary>Smoke tests for EmailListViewModel wiring; detailed coverage lives in split test files.</summary>
public sealed class EmailListViewModelTests
{
    [Fact]
    public async Task Smoke_load_first_page_populates_rows()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        Assert.Single(sut.Emails);
        Assert.Equal(EmailListLoadState.Loaded, sut.LoadState);
    }

    [Fact]
    public async Task Smoke_project_context_creates_active_group()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));

        Assert.True(sut.HasActiveProject);
        Assert.NotNull(sut.ActiveProjectGroup);
    }

    [Fact]
    public void Smoke_context_menu_file_command_exists()
    {
        var sut = new EmailListViewModel(
            new EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new EmailListViewModelTestFixtures.StubAuthService(),
            new EmailListViewModelTestFixtures.RecordingFilingService(),
            new EmailListViewModelTestFixtures.RecordingStatusService(),
            new EmailListViewModelTestFixtures.StubCurrentProjectContext(EmailListViewModelTestFixtures.CreateProject()),
            new EmailListViewModelTestFixtures.StubCurrentUser(7));

        Assert.NotNull(sut.FileEmailToProjectCommand);
        Assert.NotNull(sut.GetContextMenuDisabledReason(null, EmailContextMenuAction.FileToProject));
    }

    [Fact]
    public void Smoke_group_by_label_defaults_on()
    {
        var sut = new EmailListViewModel(
            new EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new EmailListViewModelTestFixtures.StubAuthService());

        Assert.True(sut.GroupByLabel);
    }
}
