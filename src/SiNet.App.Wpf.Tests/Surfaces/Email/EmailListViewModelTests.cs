using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailListViewModelTests
{
    [Fact]
    public async Task Load_first_page_uses_page_size_50()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, () => true);

        await sut.RefreshPageAsync();

        Assert.Equal(EmailMailboxQuery.DefaultPageSize, gateway.LastQuery?.PageSize);
        Assert.Equal(1, sut.CurrentPageNumber);
    }

    [Fact]
    public async Task Next_page_passes_next_token_and_increments_page_number()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, () => true);

        await sut.RefreshPageAsync();
        Assert.Equal("page-2", gateway.LastNextToken);

        await sut.LoadNextPageAsync();

        Assert.Equal(2, sut.CurrentPageNumber);
        Assert.Equal("page-2", gateway.LastPageToken);
    }

    [Fact]
    public async Task Previous_page_restores_prior_token()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, () => true);

        await sut.RefreshPageAsync();
        await sut.LoadNextPageAsync();
        await sut.LoadPreviousPagePublicAsync();

        Assert.Equal(1, sut.CurrentPageNumber);
        Assert.Null(gateway.LastPageToken);
    }

    [Fact]
    public async Task Clear_filters_resets_to_all_emails()
    {
        var gateway = new PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, () => true)
        {
            SearchText = "foo",
            AddressFilter = "bar@x.com",
            SubjectFilter = "subject",
            SelectedProjectLinkFilter = EmailProjectLinkFilter.Linked,
            FilterByCurrentProject = true,
        };

        await sut.ClearFiltersAndReloadAsync();

        Assert.Equal(string.Empty, sut.SearchText);
        Assert.Equal(EmailProjectLinkFilter.All, sut.SelectedProjectLinkFilter);
        Assert.False(sut.FilterByCurrentProject);
        Assert.Null(gateway.LastQuery?.FreeText);
    }

    [Fact]
    public async Task Link_enrichment_maps_internet_message_id_to_project_display()
    {
        var gateway = new PagingEmailGateway();
        var linkQuery = new StubThreadLinkQuery();
        var sut = new EmailListViewModel(gateway, linkQuery, () => true);

        await sut.RefreshPageAsync();

        Assert.Equal("1042 — North", sut.Emails[0].ProjectDisplay);
        Assert.Equal(EmailProjectLinkState.Linked, sut.Emails[0].ProjectLinkState);
    }

    private sealed class PagingEmailGateway : IEmailGateway
    {
        public EmailMailboxQuery? LastQuery { get; private set; }
        public string? LastPageToken { get; private set; }
        public string? LastNextToken { get; private set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            LastPageToken = pageToken;
            return Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-1",
                    "thread-1",
                    EmailAddress.CreateOrFallback("a@example.com"),
                    "Hello",
                    DateTimeOffset.UtcNow,
                    false,
                    InternetMessageId: "<abc@mail.com>"),
            ],
            query.PageSize,
            LastNextToken = "page-2",
            true));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);
    }

    private sealed class StubThreadLinkQuery : IEmailThreadLinkQueryService
    {
        public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
            IReadOnlyList<string> internetMessageIds,
            CancellationToken cancellationToken = default)
        {
            var map = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["abc@mail.com"] = new EmailProjectLinkInfo(
                    IsLinked: true,
                    ProjectId: 1042,
                    ProjectNumber: "1042",
                    ProjectName: "North",
                    DisplayName: "1042 — North"),
            };

            return Task.FromResult<IReadOnlyDictionary<string, EmailProjectLinkInfo>>(map);
        }
    }
}
