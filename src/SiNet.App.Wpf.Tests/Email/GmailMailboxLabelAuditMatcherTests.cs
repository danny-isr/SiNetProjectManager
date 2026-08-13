using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Tests.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailMailboxLabelAuditMatcherTests
{
    private static readonly ProjectSummaryDto Tower = new(
        11,
        "1042",
        "North Towers",
        "Tel Aviv",
        CompanyName: null,
        JobType: null,
        Status: null,
        AssignedUserName: null,
        IsActive: true,
        ProjectLabelName: "(1042)North Towers");

    [Fact]
    public void Label_without_number_has_empty_project_and_no_note()
    {
        var rows = GmailMailboxLabelAuditMatcher.BuildRows(
            [new GmailLabelInfo("p", EmailGmailLabelNames.Personal)],
            [Tower]);

        var row = Assert.Single(rows);
        Assert.Equal(EmailGmailLabelNames.Personal, row.LabelName);
        Assert.Null(row.ParsedProjectNumber);
        Assert.Null(row.ProjectDisplayName);
        Assert.False(row.IsDuplicate);
        Assert.Equal(string.Empty, row.Note);
    }

    [Fact]
    public void Numbered_leaf_maps_to_matching_project()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/Tel Aviv/(1042)North Towers";
        var rows = GmailMailboxLabelAuditMatcher.BuildRows(
            [new GmailLabelInfo("l1", path)],
            [Tower]);

        var row = Assert.Single(rows);
        Assert.Equal(1042, row.ParsedProjectNumber);
        Assert.Equal("(1042)North Towers", row.ProjectDisplayName);
        Assert.Equal("Tel Aviv", row.PlaceName);
        Assert.False(row.IsDuplicate);
        Assert.Equal(string.Empty, row.Note);
    }

    [Fact]
    public void Two_labels_for_same_project_are_duplicates_on_both_rows()
    {
        var a = $"{EmailGmailLabelNames.RootLabel}/Tel Aviv/(1042)A";
        var b = $"{EmailGmailLabelNames.RootLabel}/Haifa/(1042)B";
        var rows = GmailMailboxLabelAuditMatcher.BuildRows(
            [
                new GmailLabelInfo("a", a),
                new GmailLabelInfo("b", b),
            ],
            [Tower]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsDuplicate));
        Assert.All(rows, r => Assert.Contains("כפילות", r.Note, StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Note.Contains(a, StringComparison.Ordinal) || r.LabelName == a);
        Assert.Contains(rows, r => r.Note.Contains(b, StringComparison.Ordinal) || r.LabelName == b);
    }

    [Fact]
    public void Unknown_number_is_not_a_duplicate()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/X/(9999)Ghost";
        var rows = GmailMailboxLabelAuditMatcher.BuildRows(
            [
                new GmailLabelInfo("g1", path),
                new GmailLabelInfo("g2", $"{EmailGmailLabelNames.RootLabel}/Y/(9999)Other"),
            ],
            [Tower]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.False(r.IsDuplicate));
        Assert.All(rows, r => Assert.Contains("מספר לא במערכת", r.Note, StringComparison.Ordinal));
        Assert.All(rows, r => Assert.DoesNotContain("כפילות", r.Note, StringComparison.Ordinal));
    }

    [Fact]
    public void System_labels_are_excluded()
    {
        var rows = GmailMailboxLabelAuditMatcher.BuildRows(
            [
                new GmailLabelInfo("INBOX", "INBOX"),
                new GmailLabelInfo("SENT", "SENT"),
                new GmailLabelInfo("cat", "CATEGORY_PERSONAL"),
                new GmailLabelInfo("user", "Work"),
            ],
            [Tower]);

        var row = Assert.Single(rows);
        Assert.Equal("Work", row.LabelName);
    }

    [Fact]
    public void Number_outside_root_maps_and_notes()
    {
        var rows = GmailMailboxLabelAuditMatcher.BuildRows(
            [new GmailLabelInfo("o", "(1042)Orphan")],
            [Tower]);

        var row = Assert.Single(rows);
        Assert.Equal(1042, row.ParsedProjectNumber);
        Assert.Equal("(1042)North Towers", row.ProjectDisplayName);
        Assert.Contains("(מספר) מחוץ לשורש", row.Note, StringComparison.Ordinal);
        Assert.False(row.IsDuplicate);
    }

    [Fact]
    public void Close_place_folder_gets_optional_note()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/רעננא";
        var rows = GmailMailboxLabelAuditMatcher.BuildRows(
            [new GmailLabelInfo("p", path)],
            [Tower],
            ["רעננה"]);

        var row = Assert.Single(rows);
        Assert.Equal("רעננא", row.PlaceName);
        Assert.Contains("רעננה", row.Note, StringComparison.Ordinal);
        Assert.False(row.IsDuplicate);
    }
}

public sealed class GmailSystemLabelNamesTests
{
    [Theory]
    [InlineData("INBOX", null, true)]
    [InlineData("Work", null, false)]
    [InlineData("OfficeSystem_Personal", null, false)]
    [InlineData("פרויקטים_משרד", null, false)]
    [InlineData("Anything", "system", true)]
    [InlineData("CATEGORY_PROMOTIONS", null, true)]
    public void Classifies_system_vs_user(string name, string? type, bool expected) =>
        Assert.Equal(expected, GmailSystemLabelNames.IsSystemLabel(name, type));
}

public sealed class GmailMailboxLabelAuditServiceTests
{
    [Fact]
    public async Task AuditAsync_uses_all_user_labels_and_include_closed_projects()
    {
        var gateway = new RecordingAllLabelsGateway(
        [
            new GmailLabelInfo("1", $"{EmailGmailLabelNames.RootLabel}/Tel Aviv/(1042)North Towers"),
        ]);
        var projects = new StubProjectQuery(
        [
            new ProjectSummaryDto(
                11,
                "1042",
                "North Towers",
                "Tel Aviv",
                null,
                null,
                null,
                null,
                true,
                ProjectLabelName: "(1042)North Towers"),
        ]);
        var sut = new GmailMailboxLabelAuditService(gateway, projects);

        var rows = await sut.AuditAsync();

        Assert.Equal(1, gateway.AllUserLabelCalls);
        Assert.True(projects.LastQuery?.IncludeClosed);
        Assert.Equal(1042, Assert.Single(rows).ParsedProjectNumber);
    }
}

public sealed class GmailMailboxLabelAuditViewModelTests
{
    [Fact]
    public void Search_filters_label_name()
    {
        var vm = new GmailMailboxLabelAuditViewModel(
        [
            new GmailMailboxLabelAuditRow("1", "Work", null, null, null, string.Empty, false),
            new GmailMailboxLabelAuditRow("2", "OfficeSystem_Personal", null, null, null, string.Empty, false),
        ]);

        Assert.Equal(2, vm.FilteredRows.Count);
        vm.SearchText = "personal";
        Assert.Single(vm.FilteredRows);
        Assert.Equal("OfficeSystem_Personal", vm.FilteredRows[0].LabelName);
    }
}

public sealed class EmailListLabelAuditGateTests
{
    [Fact]
    public async Task Audit_when_disconnected_does_not_call_service()
    {
        var audit = new RecordingAuditService();
        var auth = new EmailListViewModelTestFixtures.StubAuthService { IsAuthenticated = false };
        var sut = new EmailListViewModel(
            new EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            auth,
            labelAudit: audit);

        await sut.AuditMailboxLabelsAsync();

        Assert.Equal(0, audit.Calls);
        Assert.Equal("Gmail לא מחובר. התחבר ונסה שוב.", sut.LoadError);
    }
}

file sealed class RecordingAuditService : IGmailMailboxLabelAuditService
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<GmailMailboxLabelAuditRow>> AuditAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<GmailMailboxLabelAuditRow>>([]);
    }
}

file sealed class RecordingAllLabelsGateway : IEmailGateway
{
    private readonly IReadOnlyList<GmailLabelInfo> _labels;

    public RecordingAllLabelsGateway(IReadOnlyList<GmailLabelInfo> labels) => _labels = labels;

    public int AllUserLabelCalls { get; private set; }

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
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailMailboxPage([], query.PageSize, null, false));

    public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);

    public Task<IReadOnlyList<GmailLabelInfo>> GetAllUserLabelsAsync(CancellationToken cancellationToken = default)
    {
        AllUserLabelCalls++;
        return Task.FromResult(_labels);
    }

    public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
        EmailMailboxQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailMailboxUnreadCount(0, IsExact: true));
}

file sealed class StubProjectQuery(IReadOnlyList<ProjectSummaryDto> projects) : IProjectQueryService
{
    public ProjectSearchQuery? LastQuery { get; private set; }

    public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
        ProjectSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(projects);
    }

    public Task<ProjectSummaryDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(projects.FirstOrDefault(p => p.ProjectId == projectId));
}
