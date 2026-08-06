using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;
public sealed class EmailListViewModelGroupingTests
{
    [Fact]
    public async Task Email_group_by_label_creates_collapsible_groups()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);

        Assert.NotEmpty(sut.DisplayGroups);
        Assert.Contains(sut.DisplayGroups, static g => g.LabelId == EmailListGroupBuilder.UnfiledGroupId);
    }

    [Fact]
    public async Task Email_label_group_can_expand_and_collapse()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == EmailListGroupBuilder.UnfiledGroupId);
        Assert.True(group.IsExpanded);
        group.CollapseCommand.Execute(null);
        Assert.False(group.IsExpanded);

        group.ExpandCommand.Execute(null);
        Assert.True(group.IsExpanded);
    }

    [Fact]
    public async Task Email_label_group_header_shows_loaded_count()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == EmailListGroupBuilder.UnfiledGroupId);
        Assert.Contains("נטענו", group.HeaderStatus, StringComparison.Ordinal);
        Assert.Contains(group.LoadedCount.ToString(), group.HeaderStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multi_label_email_appears_in_exactly_one_group()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var memberships = sut.DisplayGroups.Count(g => g.Emails.Any(r => r.Id == "msg-multi"));
        Assert.Equal(1, memberships);
        Assert.Contains(
            sut.DisplayGroups.First(static g => g.LabelId == EmailListGroupBuilder.UnfiledGroupId).Emails,
            static row => row.Id == "msg-multi");
    }

    [Fact]
    public async Task Load_all_for_label_uses_label_specific_gmail_query()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        await sut.LoadAllForLabelGroupForTestsAsync(group);

        Assert.Equal("Label_Proj", gateway.LastLabelGroupQuery?.LabelId);
        Assert.Equal(EmailMailboxScope.Label, gateway.LastLabelGroupQuery?.MailboxScope);
    }

    [Fact]
    public async Task Load_all_for_label_uses_label_id_not_display_name()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        await sut.LoadMoreForLabelGroupForTestsAsync(group);

        Assert.Equal("Label_Proj", gateway.LastLabelGroupQuery?.LabelId);
        Assert.NotEqual(group.LabelDisplayName, gateway.LastLabelGroupQuery?.LabelId);
    }

    [Fact]
    public async Task Load_all_for_label_loads_pages_until_no_next_token()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        await sut.LoadAllForLabelGroupForTestsAsync(group);

        Assert.True(gateway.LabelPageCalls.Count >= 2);
        Assert.True(group.HasLoadedAll);
    }

    [Fact]
    public async Task Load_all_for_label_does_not_duplicate_messages_in_same_group()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway { DuplicateSecondLabelPage = true };
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        await sut.LoadAllForLabelGroupForTestsAsync(group);

        Assert.Equal(1, group.Emails.Count(static row => row.Id == "label-work-page-1"));
    }

    [Fact]
    public async Task Load_more_for_label_uses_group_next_page_token()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        await sut.LoadMoreForLabelGroupForTestsAsync(group);
        await sut.LoadMoreForLabelGroupForTestsAsync(group);

        Assert.Equal(2, gateway.LabelPageCalls.Count);
        Assert.Null(gateway.LabelPageCalls[0].PageToken);
        Assert.Equal("label-Label_Work-page-2", gateway.LabelPageCalls[1].PageToken);
    }

    [Fact]
    public async Task General_paging_token_is_separate_from_label_group_tokens()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);

        await sut.LoadNextPageAsync();
        var globalMailboxCalls = gateway.MailboxPageCalls;

        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        await sut.LoadMoreForLabelGroupForTestsAsync(group);

        Assert.Equal(globalMailboxCalls, gateway.MailboxPageCalls);
        Assert.NotEmpty(gateway.LabelPageCalls);
    }

    [Fact]
    public async Task Changing_filters_resets_label_groups()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        await sut.LoadMoreForLabelGroupForTestsAsync(group);
        Assert.Equal("label-Label_Work-page-2", group.NextPageToken);

        sut.SearchText = "hello";
        await sut.ApplyFiltersAsync();

        var rebuilt = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        Assert.Null(rebuilt.NextPageToken);
        Assert.True(rebuilt.HasMore);
    }

    [Fact]
    public async Task Load_all_for_label_partial_failure_keeps_loaded_messages_and_shows_warning()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway
        {
            FailLabelPageOnToken = "label-Label_Work-page-2",
        };
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var group = sut.DisplayGroups.First(static g => g.LabelId == "Label_Proj");
        var countBeforeFailure = group.LoadedCount;
        await sut.LoadAllForLabelGroupForTestsAsync(group);

        Assert.True(group.LoadedCount >= countBeforeFailure);
        Assert.Contains("שגיאה", group.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Group_by_label_default_on_and_collapsed()
    {
        var sut = new EmailListViewModel(new EmailListViewModelTestFixtures.PagingEmailGateway(), threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());
        Assert.True(sut.GroupByLabel);
    }

    [Fact]
    public async Task Project_selected_loads_global_page_and_project_group()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));

        Assert.True(gateway.MailboxPageCalls >= 1);
        Assert.True(gateway.ProjectPageCalls >= 1);
        Assert.NotNull(sut.ActiveProjectGroup);
    }

    [Fact]
    public async Task Project_group_shows_first_10_only()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));

        Assert.Equal(10, sut.ActiveProjectGroup!.Emails.Count);
        Assert.Equal("1 — A", sut.ProjectGroupHeader);
        Assert.True(sut.ActiveProjectGroup.HasMore);
    }

    [Fact]
    public async Task Project_load_all_fetches_remaining_pages()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));
        await sut.LoadAllForLabelGroupForTestsAsync(sut.ActiveProjectGroup!);

        Assert.True(gateway.ProjectPageCalls >= 2);
        Assert.Equal(15, sut.ActiveProjectGroup!.Emails.Count);
    }

    [Fact]
    public async Task Clearing_project_removes_pinned_group()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));
        await sut.ApplyProjectContextAsync(null);

        Assert.Null(sut.ActiveProjectGroup);
        Assert.DoesNotContain(sut.DisplayGroups, static g => g.IsProjectGroup);
    }

    [Fact]
    public async Task Project_emails_excluded_from_mailbox_list()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectDedupeEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));

        var projectIds = sut.ActiveProjectGroup!.Emails.Select(static e => e.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("proj-1", projectIds);

        Assert.DoesNotContain(sut.FlatDisplayEmails, e => projectIds.Contains(e.Id));
        foreach (var group in sut.DisplayGroups.Where(static g => !g.IsProjectGroup))
        {
            Assert.DoesNotContain(group.Emails, e => projectIds.Contains(e.Id));
        }

        Assert.Contains(sut.FlatDisplayEmails, e => e.Id == "inbox-only");
    }

    [Fact]
    public async Task Project_label_merges_with_existing_label_group_at_top()
    {
        var gateway = new EmailListViewModelTestFixtures.ProjectMergeEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());
        await sut.InitializeAsync();
        await sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));

        var projectGroup = sut.ActiveProjectGroup;
        Assert.NotNull(projectGroup);
        Assert.True(projectGroup.IsProjectGroup);
        Assert.Equal("Label_1A", projectGroup.LabelId);
        Assert.Contains(projectGroup.Emails, e => e.Id == "mail-extra");
        Assert.DoesNotContain(sut.DisplayGroups, g => !g.IsProjectGroup && g.LabelId == "Label_1A");
        Assert.Equal(2, sut.DisplayGroups.Count);
        Assert.Same(projectGroup, sut.DisplayGroups[0]);
    }

    [Fact]
    public async Task Group_by_label_empty_fallback_shows_flat_list()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        sut.ToggleGroupByLabelCommand.Execute(null);

        Assert.False(sut.GroupByLabel);
        Assert.True(sut.ShowFlatEmailList);
        Assert.NotEmpty(sut.FlatDisplayEmails);
    }

    [Fact]
    public async Task Attachments_only_filter_adds_has_attachment_to_gmail_query()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        sut.ToggleAttachmentsOnlyCommand.Execute(null);
        await Task.Delay(250);

        Assert.NotNull(gateway.LastQuery);
        Assert.Contains("has:attachment", EmailMailboxQueryComposer.BuildSearchQuery(gateway.LastQuery), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unread_only_toggle_adds_is_unread_to_gmail_query()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        sut.ToggleUnreadOnlyCommand.Execute(null);
        await Task.Delay(250);

        Assert.NotNull(gateway.LastQuery);
        Assert.True(sut.ShowUnreadFilterActive);
        Assert.Contains("is:unread", EmailMailboxQueryComposer.BuildSearchQuery(gateway.LastQuery), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unread_only_toggle_off_returns_to_normal_inbox_query()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();
        sut.ToggleUnreadOnlyCommand.Execute(null);
        await Task.Delay(250);
        sut.ToggleUnreadOnlyCommand.Execute(null);
        await Task.Delay(250);

        Assert.NotNull(gateway.LastQuery);
        Assert.False(sut.ShowUnreadFilterActive);
        Assert.Equal(EmailMailboxScope.Inbox, gateway.LastQuery.MailboxScope);
        Assert.DoesNotContain("is:unread", EmailMailboxQueryComposer.BuildSearchQuery(gateway.LastQuery), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRowById_resolves_row_inside_label_group()
    {
        var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
        var sut = await EmailListViewModelTestFixtures.CreateLabelGroupingSutAsync(gateway);
        var groupRow = sut.DisplayGroups.First(static g => g.LabelId == EmailListGroupBuilder.UnfiledGroupId).Emails.First();

        var resolved = sut.ResolveSelectionRowForTests(groupRow.Id);

        Assert.NotNull(resolved);
        Assert.Equal(groupRow.Id, resolved.Id);
    }

    [Fact]
    public async Task Attachment_count_maps_from_email_summary()
    {
        var gateway = new EmailListViewModelTestFixtures.AttachmentCountEmailGateway();
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new EmailListViewModelTestFixtures.StubAuthService());

        await sut.RefreshPageAsync();

        var row = sut.Emails.Single();
        Assert.Equal(3, row.AttachmentCount);
        Assert.True(row.HasAttachments);
    }
}

