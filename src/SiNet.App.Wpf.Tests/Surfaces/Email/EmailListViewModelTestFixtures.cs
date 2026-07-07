using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

internal static partial class EmailListViewModelTestFixtures
{
    internal static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    internal static async Task<(EmailListViewModel Sut, EmailListRow Row, RegroupingActionEmailGateway Gateway)> CreateRegroupingWriteSutAsync(
        RegroupingActionEmailGateway gateway,
        IEmailFilingService? filing = null,
        IEmailStatusService? status = null)
    {
        var sut = CreateWriteCapableSut(gateway, filing, status);
        await sut.RefreshPageAsync();
        return (sut, sut.Emails.Single(), gateway);
    }

    internal static async Task<(EmailListViewModel Sut, EmailListRow Row, ActionTestEmailGateway Gateway)> CreateLoadedWriteSutAsync(
        ActionTestEmailGateway? gateway = null,
        IEmailFilingService? filing = null,
        IEmailStatusService? status = null)
    {
        gateway ??= new ActionTestEmailGateway();
        var sut = CreateWriteCapableSut(gateway, filing, status);
        await sut.RefreshPageAsync();
        return (sut, sut.Emails.Single(), gateway);
    }

    internal static EmailListViewModel CreateWriteCapableSut(
        IEmailGateway gateway,
        IEmailFilingService? filing = null,
        IEmailStatusService? status = null,
        ICurrentProjectContext? project = null,
        ICurrentUserContext? user = null,
        IConnectorAuthService? auth = null) =>
        new(
            gateway,
            threadLinkQuery: null,
            auth ?? new StubAuthService(),
            filing,
            status,
            project ?? new StubCurrentProjectContext(CreateProject()),
            user ?? new StubCurrentUser(7));

    internal static EmailListViewModel CreateWriteCapableSut(
        IEmailFilingService? filing = null,
        IEmailStatusService? status = null,
        ICurrentProjectContext? project = null,
        ICurrentUserContext? user = null,
        IConnectorAuthService? auth = null) =>
        new(
            new PagingEmailGateway(),
            threadLinkQuery: null,
            auth ?? new StubAuthService(),
            filing,
            status,
            project ?? new StubCurrentProjectContext(CreateProject()),
            user ?? new StubCurrentUser(7));

    internal static EmailListRow CreateRow(int? inboxMessageId, bool isFiledToProject) =>
        new(
            Id: "msg-1",
            Sender: "a@example.com",
            Subject: "Subject",
            Preview: "Preview",
            ReceivedOn: DateTime.UtcNow,
            GroupName: "INBOX",
            IsUnread: false,
            IsAssigned: isFiledToProject,
            AssignedProjectName: null,
            AttachmentCount: 0,
            InboxMessageId: inboxMessageId,
            IsFiledToProject: isFiledToProject);

    internal static async Task<EmailListViewModel> CreateLabelGroupingSutAsync(LabelGroupingEmailGateway gateway)
    {
        var sut = new EmailListViewModel(gateway, threadLinkQuery: null, new StubAuthService());
        await sut.InitializeAsync();
        await sut.RefreshPageAsync();
        return sut;
    }
    internal static ProjectSummaryDto CreateProject() =>
        new(1042, "1042", "North", "Tel Aviv", null, null, null, null, true);
}

