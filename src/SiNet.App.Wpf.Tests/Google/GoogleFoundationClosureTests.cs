using System.ComponentModel;
using System.IO;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Google;

public sealed class GoogleFoundationClosureTests
{
    [Fact]
    public void InboxViewModel_source_consumes_connector_auth_port_not_gmail_provider()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Inbox/InboxViewModel.cs");

        Assert.Contains("IConnectorAuthService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GmailClientProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Gmail:ClientSecretsPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_xaml_uses_shared_connector_auth_restore_not_concrete_gmail_provider()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/App.xaml.cs");

        Assert.Contains("GetServices<IConnectorAuthService>()", source, StringComparison.Ordinal);
        Assert.Contains("TryRestoreSessionAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<GmailClientProvider>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void V2_new_system_startup_restores_connector_auth_via_shared_port()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/App.xaml.cs");

        Assert.Contains("StartNewSystemConnectorAuthRestore", source, StringComparison.Ordinal);
        Assert.Contains("GetServices<IConnectorAuthService>()", source, StringComparison.Ordinal);
        Assert.Contains("TryRestoreSessionAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<GmailClientProvider>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginAsync", source.Substring(source.IndexOf("StartNewSystemConnectorAuthRestore", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void V2_run_new_system_startup_triggers_connector_auth_silent_restore()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/App.xaml.cs");

        var runNewSystemStart = source.IndexOf("private void RunNewSystemStartup", StringComparison.Ordinal);
        var runNewSystemEnd = source.IndexOf("private static void LaunchNewSystemShell", StringComparison.Ordinal);
        Assert.True(runNewSystemStart >= 0);
        Assert.True(runNewSystemEnd > runNewSystemStart);

        var runNewSystemBody = source.Substring(runNewSystemStart, runNewSystemEnd - runNewSystemStart);
        Assert.Contains("StartNewSystemConnectorAuthRestore()", runNewSystemBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Google_boundary_doc_records_g_startup_closure_without_broad_migration_approval()
    {
        var source = ReadRepoFile("docs/GOOGLE_BOUNDARY.md");

        Assert.Contains("G-Startup closure", source, StringComparison.Ordinal);
        Assert.Contains("StartNewSystemConnectorAuthRestore", source, StringComparison.Ordinal);
        Assert.Contains("GmailSend** still requires a separate **G-Policy** decision", source, StringComparison.Ordinal);
        Assert.Contains("Drive / Sheets / Reports** remain legacy/deferred", source, StringComparison.Ordinal);
        Assert.Contains("Broad legacy window migration** remains blocked", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewSystem_graph_registers_native_google_module_with_legacy_host_settings()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs");

        Assert.Contains("services.AddSiNetGoogle(ConfigureNewSystemGmail);", source, StringComparison.Ordinal);
        Assert.Contains("options.TokenStorePath = AppConfiguration.GoogleTokenStorePath;", source, StringComparison.Ordinal);
        Assert.Contains("options.ApplicationName = AppConfiguration.GoogleApplicationName;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InboxViewModel_reflects_external_auth_state_changes()
    {
        var auth = new StubConnectorAuthService(isAuthenticated: false);
        using var sut = new InboxViewModel(new StubEmailGateway(), auth);

        var changed = new List<string>();
        sut.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                changed.Add(e.PropertyName!);
            }
        };

        auth.SetAuthenticated(true);

        Assert.True(sut.IsConnected);
        Assert.Contains(nameof(InboxViewModel.IsConnected), changed);
    }

    [Fact]
    public void Email_window_view_model_consumes_shared_google_ports_not_concrete_runtime()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.DoesNotContain("GmailClientProvider", source, StringComparison.Ordinal);
        Assert.Contains("IConnectorAuthService", source, StringComparison.Ordinal);
        Assert.Contains("IEmailGateway", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmailSender", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_window_view_model_keeps_send_and_modify_deferred_pending_policy_decision()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("ShowDeferredWriteActions => false", source, StringComparison.Ordinal);
        Assert.Contains("DeferredProductionPilotAction", source, StringComparison.Ordinal);
        Assert.Contains("G-Policy", source, StringComparison.Ordinal);
        Assert.Contains("ITaskCompletionCoordinator", source, StringComparison.Ordinal);
        Assert.Contains("GetDetailsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_window_view_model_loads_mailbox_page_when_no_project_selected()
    {
        var auth = new StubConnectorAuthService(isAuthenticated: true);
        var gateway = new RecordingEmailGateway();
        var context = new InMemoryCurrentProjectContext();

        using var sut = new EmailWindowViewModel(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            context,
            gateway,
            auth);

        await sut.RefreshAsync();

        Assert.True(gateway.LastMailboxPageRequested);
        Assert.Equal(EmailMailboxQuery.DefaultPageSize, gateway.LastPageSize);
        Assert.False(gateway.LastProjectLabelFilterUsed);
        Assert.True(sut.EmailList.IsAllEmailsMode);
        Assert.Single(sut.Emails);
        Assert.Equal("msg-42", sut.Emails[0].Id);
    }

    [Fact]
    public async Task Email_window_view_model_loads_project_group_when_project_selected()
    {
        var auth = new StubConnectorAuthService(isAuthenticated: true);
        var gateway = new RecordingEmailGateway();
        var context = new InMemoryCurrentProjectContext();
        await context.SetCurrentProjectAsync(new ProjectSummaryDto(
            ProjectId: 1042,
            ProjectNumber: "1042",
            ProjectName: "North Towers",
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true,
            ProjectLabelName: "(1042) North Towers"));

        using var sut = new EmailWindowViewModel(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            context,
            gateway,
            auth);

        await sut.RefreshAsync();

        Assert.False(gateway.LastMailboxPageRequested);
        Assert.Equal("(1042) North Towers", gateway.LastProjectLabelName);
        Assert.True(sut.EmailList.IsProjectMode);
        Assert.Single(sut.Emails);
        Assert.Equal("msg-42", sut.Emails[0].Id);
    }

    [Fact]
    public async Task Email_window_view_model_loads_selected_email_details()
    {
        var auth = new StubConnectorAuthService(isAuthenticated: true);
        var gateway = new RecordingEmailGateway();
        var context = new InMemoryCurrentProjectContext();
        await context.SetCurrentProjectAsync(new ProjectSummaryDto(
            ProjectId: 1042,
            ProjectNumber: "1042",
            ProjectName: "North Towers",
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true,
            ProjectLabelName: "(1042) North Towers"));

        using var sut = new EmailWindowViewModel(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            context,
            gateway,
            auth);

        await sut.RefreshAsync();
        await sut.OpenSelectedEmailAsync();

        Assert.Equal("msg-42", gateway.LastDetailsMessageId);
        Assert.Contains("Detailed body for North update", sut.SelectedEmailBody, StringComparison.Ordinal);
        Assert.Single(sut.Attachments);
        Assert.Contains("quote.pdf", sut.Attachments[0].DisplayLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Google_boundary_doc_records_drive_sheets_reports_defer_policy()
    {
        var source = ReadRepoFile("docs/GOOGLE_BOUNDARY.md");

        Assert.Contains("Drive / Sheets / Reports defer decision", source, StringComparison.Ordinal);
        Assert.Contains("Requires an approved `ProjectFiles` / storage-destination slice", source, StringComparison.Ordinal);
        Assert.Contains("No runtime movement of Drive, Sheets, or report/export code until a real consumer slice is named.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Google_boundary_doc_records_first_real_email_window_read_slice()
    {
        var source = ReadRepoFile("docs/GOOGLE_BOUNDARY.md");

        Assert.Contains("First real email window (read-only content/details)", source, StringComparison.Ordinal);
        Assert.Contains("now consumes the same shared", source, StringComparison.Ordinal);
        Assert.Contains("not consume `GmailClientProvider` or `IEmailSender` directly", source, StringComparison.Ordinal);
        Assert.Contains("full body/attachment metadata by the project's canonical label leaf", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class StubEmailGateway : IEmailGateway
    {
        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>(
            [
                new EmailSummary(
                    "msg-1",
                    "thread-1",
                    EmailAddress.CreateOrFallback("sender@example.com"),
                    "Subject",
                    DateTimeOffset.UtcNow,
                    true),
            ]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            GetProjectEmailsAsync(string.Empty, projectLabelName, cancellationToken);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(new EmailMessageDetails(
                messageId,
                "thread-1",
                EmailAddress.CreateOrFallback("sender@example.com"),
                "Subject",
                DateTimeOffset.UtcNow,
                "Body",
                []));

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxPage([], query.PageSize, null, false));

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);
    }

    private sealed class RecordingEmailGateway : IEmailGateway
    {
        public string? LastProjectLabelName { get; private set; }
        public string? LastDetailsMessageId { get; private set; }
        public bool LastMailboxPageRequested { get; private set; }
        public int LastPageSize { get; private set; }
        public bool LastProjectLabelFilterUsed { get; private set; }
        public string? LastPageToken { get; private set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default)
        {
            LastProjectLabelName = projectLabelName;
            return Task.FromResult<IReadOnlyList<EmailSummary>>(
            [
                new EmailSummary(
                    "msg-42",
                    "thread-42",
                    EmailAddress.CreateOrFallback("north@example.com"),
                    "North update",
                    DateTimeOffset.UtcNow,
                    true),
            ]);
        }

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default)
        {
            LastDetailsMessageId = messageId;
            return Task.FromResult<EmailMessageDetails?>(new EmailMessageDetails(
                messageId,
                "thread-42",
                EmailAddress.CreateOrFallback("north@example.com"),
                "North update",
                DateTimeOffset.UtcNow,
                "Detailed body for North update",
                [
                    new EmailMessageAttachmentDetails("att-1", "quote.pdf", "application/pdf", 2048),
                ]));
        }

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            LastMailboxPageRequested = true;
            LastPageSize = query.PageSize;
            LastProjectLabelFilterUsed = !string.IsNullOrWhiteSpace(query.OptionalProjectLabel);
            LastPageToken = pageToken;
            LastProjectLabelName = query.OptionalProjectLabel;

            return Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-42",
                    "thread-42",
                    EmailAddress.CreateOrFallback("north@example.com"),
                    "North update",
                    DateTimeOffset.UtcNow,
                    true),
            ],
            query.PageSize,
            "next-token",
            true));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([
                new GmailLabelInfo("INBOX", "INBOX"),
            ]);
    }

    private sealed class StubConnectorAuthService(bool isAuthenticated) : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; } = isAuthenticated;

        public string? ConnectedAccountEmail { get; private set; }

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public void Logout() => SetAuthenticated(false);

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void SetAuthenticated(bool isAuthenticated)
        {
            IsAuthenticated = isAuthenticated;
            AuthStateChanged?.Invoke(isAuthenticated);
        }
    }
}
