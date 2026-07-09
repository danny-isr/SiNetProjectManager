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
public sealed class EmailListViewModelFilingTests
{
    [Fact]
    public void File_command_works_without_inbox_message_id_when_project_selected()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var projectContext = new EmailListViewModelTestFixtures.StubCurrentProjectContext(EmailListViewModelTestFixtures.CreateProject());
        var sut = new EmailListViewModel(
            new EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new EmailListViewModelTestFixtures.StubAuthService(),
            filing,
            statusService: null,
            projectContext,
            new EmailListViewModelTestFixtures.StubCurrentUser(7));

        var rowWithoutInbox = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);
        var rowReady = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: 42, isFiledToProject: false);

        Assert.True(sut.FileEmailToProjectCommand.CanExecute(rowWithoutInbox));
        Assert.True(sut.FileEmailToProjectCommand.CanExecute(rowReady));
        Assert.False(sut.UnfileEmailCommand.CanExecute(rowReady));
    }

    [Fact]
    public async Task File_command_calls_filing_service_without_requiring_inbox_id()
    {
        var gateway = new EmailListViewModelTestFixtures.PagingEmailGateway();
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var projectContext = new EmailListViewModelTestFixtures.StubCurrentProjectContext(EmailListViewModelTestFixtures.CreateProject());
        var sut = new EmailListViewModel(
            gateway,
            threadLinkQuery: null,
            new EmailListViewModelTestFixtures.StubAuthService(),
            filing,
            statusService: null,
            projectContext,
            new EmailListViewModelTestFixtures.StubCurrentUser(7));

        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);
        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.True(filing.FileCalled);
        Assert.Null(filing.LastFileCommand?.InboxMessageId);
        Assert.Equal(1042, filing.LastFileCommand?.TargetProjectId);
        Assert.Equal("msg-1", filing.LastFileCommand?.GmailMessageId);
    }

    [Fact]
    public void Unfile_command_works_when_filed_without_inbox_message_id()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var sut = new EmailListViewModel(
            new EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new EmailListViewModelTestFixtures.StubAuthService(),
            filing,
            statusService: null,
            new EmailListViewModelTestFixtures.StubCurrentProjectContext(EmailListViewModelTestFixtures.CreateProject()),
            new EmailListViewModelTestFixtures.StubCurrentUser(7));

        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: true);

        Assert.True(sut.UnfileEmailCommand.CanExecute(row));
    }

    [Fact]
    public void Email_context_menu_commands_receive_email_row_parameter()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(filing: filing);
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.True(sut.FileEmailToProjectCommand.CanExecute(row));
        sut.FileEmailToProjectCommand.Execute(row);
        Assert.True(filing.FileCalled);
        Assert.Equal("msg-1", filing.LastFileCommand?.GmailMessageId);
    }

    [Fact]
    public void Email_context_menu_link_project_disabled_without_current_user()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(
            filing: new EmailListViewModelTestFixtures.RecordingFilingService(),
            user: new EmailListViewModelTestFixtures.StubCurrentUser(0));
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.False(sut.FileEmailToProjectCommand.CanExecute(row));
        var reason = sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.FileToProject);
        Assert.NotNull(reason);
        Assert.Contains("משתמש", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_context_menu_link_project_disabled_without_current_project()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(
            filing: new EmailListViewModelTestFixtures.RecordingFilingService(),
            project: new EmailListViewModelTestFixtures.StubCurrentProjectContext(project: null));
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.False(sut.FileEmailToProjectCommand.CanExecute(row));
        var reason = sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.FileToProject);
        Assert.NotNull(reason);
        Assert.Contains("פרויקט", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_context_menu_link_project_disabled_without_filing_service()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(filing: null);
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.False(sut.FileEmailToProjectCommand.CanExecute(row));
        var reason = sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.FileToProject);
        Assert.NotNull(reason);
        Assert.Contains("שירות", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_context_menu_link_project_enabled_with_user_project_and_service()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(filing: new EmailListViewModelTestFixtures.RecordingFilingService());
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.True(sut.FileEmailToProjectCommand.CanExecute(row));
        Assert.Null(sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.FileToProject));
    }

    [Fact]
    public void Email_context_menu_unfile_enabled_for_filed_email()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(filing: new EmailListViewModelTestFixtures.RecordingFilingService());
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: true);

        Assert.True(sut.UnfileEmailCommand.CanExecute(row));
    }

    [Fact]
    public void Email_context_menu_unfile_disabled_for_unfiled_email()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(filing: new EmailListViewModelTestFixtures.RecordingFilingService());
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.False(sut.UnfileEmailCommand.CanExecute(row));
        Assert.NotNull(sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.Unfile));
    }

    [Fact]
    public void Email_context_menu_personal_disabled_without_status_service()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(status: null);
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.False(sut.MarkAsPersonalCommand.CanExecute(row));
        var reason = sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.MarkPersonal);
        Assert.NotNull(reason);
        Assert.Contains("סטטוס", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_context_menu_personal_disabled_without_current_user()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(
            status: new EmailListViewModelTestFixtures.RecordingStatusService(),
            user: new EmailListViewModelTestFixtures.StubCurrentUser(0));
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.False(sut.MarkAsPersonalCommand.CanExecute(row));
        Assert.NotNull(sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.MarkPersonal));
    }

    [Fact]
    public void Email_context_menu_personal_enabled_with_user_and_status_service()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(status: new EmailListViewModelTestFixtures.RecordingStatusService());
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.True(sut.MarkAsPersonalCommand.CanExecute(row));
    }

    [Fact]
    public void Disabled_context_menu_actions_show_reason_or_status_message()
    {
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(
            filing: null,
            status: null,
            user: new EmailListViewModelTestFixtures.StubCurrentUser(0));
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);

        Assert.NotNull(sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.FileToProject));
        Assert.NotNull(sut.GetContextMenuDisabledReason(row, EmailContextMenuAction.MarkPersonal));
    }

    [Fact]
    public async Task Unfile_email_calls_IEmailFilingService()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(filing: filing);
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: true);

        await sut.UnfileEmailForTestsAsync(row);

        Assert.True(filing.UnfileCalled);
    }

    [Fact]
    public async Task Mark_personal_calls_IEmailStatusService()
    {
        var status = new EmailListViewModelTestFixtures.RecordingStatusService();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(status: status);
        var row = EmailListViewModelTestFixtures.CreateRow(inboxMessageId: 5, isFiledToProject: false);

        await sut.MarkAsPersonalForTestsAsync(row);

        Assert.True(status.StatusCalled);
        Assert.Equal(EmailTriageStatus.Personal, status.LastStatus);
    }

    [Fact]
    public async Task Email_action_sets_status_message_immediately()
    {
        var filing = new EmailListViewModelTestFixtures.DelayingFilingService();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(filing: filing);
        var actionTask = sut.FileEmailToProjectForTestsAsync(row);

        await EmailListViewModelTestFixtures.WaitUntilAsync(() => sut.StatusMessage.Contains("משייך", StringComparison.Ordinal));

        filing.Release();
        await actionTask;

        Assert.Contains("הצלחה", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task File_email_sets_row_busy_while_running()
    {
        var filing = new EmailListViewModelTestFixtures.DelayingFilingService();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(filing: filing);
        var actionTask = sut.FileEmailToProjectForTestsAsync(row);

        await EmailListViewModelTestFixtures.WaitUntilAsync(() => sut.FindRowForTests(row.Id)?.IsActionBusy == true);

        filing.Release();
        await actionTask;

        Assert.False(sut.FindRowForTests(row.Id)?.IsActionBusy);
    }

    [Fact]
    public async Task Unfile_email_sets_row_busy_while_running()
    {
        var filing = new EmailListViewModelTestFixtures.DelayingFilingService();
        var gateway = new EmailListViewModelTestFixtures.ActionTestEmailGateway();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(gateway, filing);
        await sut.RefreshPageAsync();
        var row = sut.Emails.Single() with { IsFiledToProject = true };
        sut.Emails[0] = row;

        var actionTask = sut.UnfileEmailForTestsAsync(row);

        await EmailListViewModelTestFixtures.WaitUntilAsync(() => sut.FindRowForTests(row.Id)?.IsActionBusy == true);

        filing.ReleaseUnfile();
        await actionTask;

        Assert.False(sut.FindRowForTests(row.Id)?.IsActionBusy);
    }

    [Fact]
    public async Task Mark_personal_sets_row_busy_while_running()
    {
        var status = new EmailListViewModelTestFixtures.DelayingStatusService();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(status: status);
        var actionTask = sut.MarkAsPersonalForTestsAsync(row);

        await EmailListViewModelTestFixtures.WaitUntilAsync(() => sut.FindRowForTests(row.Id)?.IsActionBusy == true);

        status.Release();
        await actionTask;
    }

    [Fact]
    public async Task Email_action_disables_duplicate_execution_for_same_row()
    {
        var filing = new EmailListViewModelTestFixtures.DelayingFilingService();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(filing: filing);
        var first = sut.FileEmailToProjectForTestsAsync(row);

        await EmailListViewModelTestFixtures.WaitUntilAsync(() => sut.IsRowActionBusy(row.Id));

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.Equal(1, filing.FileCallCount);

        filing.Release();
        await first;
    }

    [Fact]
    public async Task File_email_success_updates_row_project_state_without_full_reload_when_possible()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.ActionTestEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(gateway, filing);
        var pageCallsAfterLoad = gateway.MailboxPageCalls;

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.Equal(pageCallsAfterLoad, gateway.MailboxPageCalls);
        var updated = sut.FindRowForTests(row.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsFiledToProject);
        Assert.Equal(EmailProjectLinkState.Linked, updated.ProjectLinkState);
    }

    [Fact]
    public async Task Unfile_email_success_updates_row_project_state_without_full_reload_when_possible()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.ActionTestEmailGateway();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(gateway, filing);
        await sut.RefreshPageAsync();
        var row = sut.Emails.Single() with { IsFiledToProject = true, FiledProjectLabelPath = "פרויקטים_משרד/Tel Aviv/1042 — North" };
        sut.Emails[0] = row;
        var pageCallsAfterLoad = gateway.MailboxPageCalls;

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        Assert.Equal(pageCallsAfterLoad, gateway.MailboxPageCalls);
        var updated = sut.FindRowForTests(row.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsFiledToProject);
        Assert.Equal(EmailProjectLinkState.Unlinked, updated.ProjectLinkState);
    }

    [Fact]
    public async Task Mark_personal_removes_or_updates_only_target_row_when_possible()
    {
        var status = new EmailListViewModelTestFixtures.RecordingStatusService();
        var gateway = new EmailListViewModelTestFixtures.ActionTestEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(gateway, status: status);
        var pageCallsAfterLoad = gateway.MailboxPageCalls;

        await sut.MarkAsPersonalForTestsAsync(row);

        Assert.Equal(pageCallsAfterLoad, gateway.MailboxPageCalls);
        Assert.DoesNotContain(sut.Emails, email => email.Id == row.Id);
    }

    [Fact]
    public async Task Email_action_failure_clears_row_busy_and_shows_warning()
    {
        var filing = new EmailListViewModelTestFixtures.FailingFilingService("שגיאת בדיקה");
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(filing: filing);

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.False(sut.IsRowActionBusy(row.Id));
        Assert.NotNull(sut.LoadWarning);
        Assert.Contains("שגיאת בדיקה", sut.LoadWarning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_action_does_not_block_ui_thread()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(filing: filing);

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.True(filing.FileCalled);
    }

    [Fact]
    public async Task Email_action_does_not_run_duplicate_gmail_operations_for_same_row()
    {
        var filing = new EmailListViewModelTestFixtures.DelayingFilingService();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(filing: filing);
        var first = sut.FileEmailToProjectForTestsAsync(row);

        await EmailListViewModelTestFixtures.WaitUntilAsync(() => filing.FileCallCount == 1);
        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.Equal(1, filing.FileCallCount);

        filing.Release();
        await first;
    }

    [Fact]
    public async Task Email_action_logs_or_reports_duration_for_diagnostics()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(filing: filing);

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.NotNull(sut.LastActionDiagnostics);
        Assert.Contains("service=", sut.LastActionDiagnostics, StringComparison.Ordinal);
        Assert.Contains("localUpdate=", sut.LastActionDiagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unfile_email_updates_row_to_unlinked_without_manual_refresh()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);
        var pageCallsAfterLoad = gateway.MailboxPageCalls;

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        Assert.Equal(pageCallsAfterLoad, gateway.MailboxPageCalls);
        var updated = sut.FindRowForTests(row.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsFiledToProject);
        Assert.Equal(EmailProjectLinkState.Unlinked, updated.ProjectLinkState);
    }

    [Fact]
    public async Task Unfile_email_removes_project_label_from_local_row()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        var updated = sut.FindRowForTests(row.Id);
        Assert.NotNull(updated);
        Assert.DoesNotContain(updated.LabelChipNames ?? [], label => EmailGmailLabelNames.IsProjectLabel(label));
    }

    [Fact]
    public async Task Unfile_email_removes_row_from_project_group_when_grouped_by_label()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);

        Assert.Contains(
            sut.DisplayGroups,
            group => group.Emails.Any(email => email.Id == row.Id)
                && group.LabelDisplayName.Contains("1042", StringComparison.Ordinal));

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        Assert.DoesNotContain(
            sut.DisplayGroups,
            group => group.Emails.Any(email => email.Id == row.Id)
                && group.LabelDisplayName.Contains("1042", StringComparison.Ordinal));
        Assert.Contains(
            sut.DisplayGroups,
            group => group.Emails.Any(email => email.Id == row.Id));
    }

    [Fact]
    public async Task Unfile_email_removes_row_when_linked_filter_is_active()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);
        sut.SelectedProjectLinkFilter = EmailProjectLinkFilter.Linked;

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        Assert.DoesNotContain(sut.Emails, email => email.Id == row.Id);
    }

    [Fact]
    public async Task Unfile_email_keeps_row_when_all_filter_is_active_and_marks_unlinked()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);
        sut.SelectedProjectLinkFilter = EmailProjectLinkFilter.All;

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        var updated = sut.FindRowForTests(row.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsLinked);
    }

    [Fact]
    public async Task File_email_updates_row_to_linked_without_manual_refresh()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.ActionTestEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(gateway, filing);
        var pageCallsAfterLoad = gateway.MailboxPageCalls;

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.Equal(pageCallsAfterLoad, gateway.MailboxPageCalls);
        var updated = sut.FindRowForTests(row.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsFiledToProject);
        Assert.Equal(EmailProjectLinkState.Linked, updated.ProjectLinkState);
    }

    [Fact]
    public async Task File_email_moves_row_to_project_group_when_grouped_by_label()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway(loadFiledInitially: false);
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);

        Assert.DoesNotContain(
            sut.DisplayGroups,
            group => group.Emails.Any(email => email.Id == row.Id)
                && group.LabelDisplayName.Contains("1042", StringComparison.Ordinal));

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.Contains(
            sut.DisplayGroups,
            group => group.Emails.Any(email => email.Id == row.Id)
                && group.LabelDisplayName.Contains("1042", StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_email_removes_row_when_unlinked_filter_is_active()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway(loadFiledInitially: false);
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);
        sut.SelectedProjectLinkFilter = EmailProjectLinkFilter.Unlinked;

        await sut.FileEmailToProjectForTestsAsync(row);

        Assert.DoesNotContain(sut.Emails, email => email.Id == row.Id);
    }

    [Fact]
    public async Task Mark_personal_removes_or_moves_row_without_manual_refresh()
    {
        var status = new EmailListViewModelTestFixtures.RecordingStatusService();
        var gateway = new EmailListViewModelTestFixtures.ActionTestEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateLoadedWriteSutAsync(gateway, status: status);
        var pageCallsAfterLoad = gateway.MailboxPageCalls;

        await sut.MarkAsPersonalForTestsAsync(row);

        Assert.Equal(pageCallsAfterLoad, gateway.MailboxPageCalls);
        Assert.DoesNotContain(sut.Emails, email => email.Id == row.Id);
    }

    [Fact]
    public async Task Mark_pending_moves_row_to_pending_group_without_manual_refresh_if_grouped()
    {
        var status = new EmailListViewModelTestFixtures.RecordingStatusService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway(loadFiledInitially: false);
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, status: status);

        gateway.ConfigurePendingSummary(row.Id);
        await sut.MarkAsPendingForTestsAsync(row);

        Assert.Contains(
            sut.DisplayGroups,
            group => group.Emails.Any(email => email.Id == row.Id)
                && string.Equals(group.LabelDisplayName, EmailGmailLabelNames.Pending, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Local_email_mutation_rebuilds_display_groups()
    {
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(gateway);
        await sut.RefreshPageAsync();
        var row = sut.Emails.Single();
        var groupsBefore = sut.DisplayGroups
            .Where(group => group.Emails.Any(email => email.Id == row.Id))
            .Select(group => group.LabelDisplayName)
            .ToList();

        var unfiled = row with
        {
            IsFiledToProject = false,
            IsFiledToSameProject = false,
            IsAssigned = false,
            ProjectLinkState = EmailProjectLinkState.Unlinked,
            LabelChipNames = [],
            PrimaryLabel = "ללא label",
            GroupName = "ללא label",
        };
        sut.ApplyLocalEmailMutationForTests(unfiled);

        var groupsAfter = sut.DisplayGroups
            .Where(group => group.Emails.Any(email => email.Id == row.Id))
            .Select(group => group.LabelDisplayName)
            .ToList();
        Assert.NotEqual(groupsBefore, groupsAfter);
    }

    [Fact]
    public async Task Local_email_mutation_preserves_selection_when_row_still_visible()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);
        sut.SelectedEmail = row;

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        Assert.NotNull(sut.SelectedEmail);
        Assert.Equal(row.Id, sut.SelectedEmail.Id);
        Assert.False(sut.SelectedEmail.IsLinked);
    }

    [Fact]
    public async Task Local_email_mutation_clears_selection_when_row_removed()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);
        sut.SelectedEmail = row;
        sut.SelectedProjectLinkFilter = EmailProjectLinkFilter.Linked;

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        Assert.Null(sut.SelectedEmail);
    }

    [Fact]
    public async Task No_full_refresh_required_for_basic_ui_state_update()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.RegroupingActionEmailGateway();
        var (sut, row, _) = await EmailListViewModelTestFixtures.CreateRegroupingWriteSutAsync(gateway, filing);
        var pageCallsAfterLoad = gateway.MailboxPageCalls;

        gateway.ConfigureUnfiledSummary(row.Id);
        await sut.UnfileEmailForTestsAsync(row);

        Assert.Equal(pageCallsAfterLoad, gateway.MailboxPageCalls);
    }

    [Fact]
    public async Task FileEmailToThreadProject_uses_thread_project_id_not_selected_project()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(filing: filing);
        var row = new EmailListRow(
            Id: "msg-thread-1",
            Sender: "sender@example.com",
            Subject: "Thread subject",
            Preview: "preview",
            ReceivedOn: DateTime.Now,
            GroupName: "INBOX",
            IsUnread: true,
            IsAssigned: false,
            AssignedProjectName: null,
            AttachmentCount: 0,
            ThreadId: "gmail-thread-42",
            ThreadProjectId: 777,
            ThreadProjectName: "777 — Thread Project",
            HasThreadHistory: true,
            ShowLinkToThreadButton: true);

        Assert.True(sut.FileEmailToThreadProjectCommand.CanExecute(row));
        await sut.FileEmailToThreadProjectForTestsAsync(row);

        Assert.True(filing.FileCalled);
        Assert.Equal(777, filing.LastFileCommand!.TargetProjectId);
        Assert.Equal("gmail-thread-42", filing.LastFileCommand.GmailThreadId);
    }

    [Fact]
    public async Task FileEmailToThreadProject_updates_peer_rows_in_same_thread()
    {
        var filing = new EmailListViewModelTestFixtures.RecordingFilingService();
        var gateway = new EmailListViewModelTestFixtures.TwoRowActionTestEmailGateway();
        var sut = EmailListViewModelTestFixtures.CreateWriteCapableSut(
            gateway: gateway,
            filing: filing);
        await sut.ConnectGmailForTestsAsync();
        await sut.LoadMailboxAndProjectForTestsAsync(resetStack: true);

        var peerA = sut.Emails[0] with
        {
            ThreadId = "shared-thread",
            ShowLinkToThreadButton = true,
            ThreadProjectId = 777,
            ThreadProjectName = "777 — Thread Project",
            HasThreadHistory = true,
        };
        var peerB = sut.Emails[1] with
        {
            ThreadId = "shared-thread",
            ShowLinkToThreadButton = true,
            ThreadProjectId = 777,
            ThreadProjectName = "777 — Thread Project",
            HasThreadHistory = true,
        };
        sut.ApplyLocalEmailMutationForTests(peerA);
        sut.ApplyLocalEmailMutationForTests(peerB);

        gateway.ConfigureFiledSummary(peerA.Id);
        await sut.FileEmailToThreadProjectForTestsAsync(peerA);

        var updatedPeerB = sut.FindRowForTests(peerB.Id);
        Assert.NotNull(updatedPeerB);
        Assert.True(updatedPeerB!.IsFiledToProject);
        Assert.Equal(777, updatedPeerB.LabelProjectId);
    }
}

