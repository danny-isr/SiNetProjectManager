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

        Assert.Contains("Reply/Send נשארים מחוץ לסלייס", source, StringComparison.Ordinal);
        Assert.Contains("Move-to-project / mark-handled עדיין מחוץ לסלייס", source, StringComparison.Ordinal);
        Assert.Contains("קריאת attachment details תגיע רק אם parity מלא יאושר", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_window_view_model_loads_project_emails_by_project_label()
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

        Assert.Equal("(1042) North Towers", gateway.LastProjectLabelName);
        Assert.Single(sut.Emails);
        Assert.Equal("msg-42", sut.Emails[0].Id);
        Assert.Contains("נטענו 1 מיילים", sut.StatusMessage, StringComparison.Ordinal);
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

        Assert.Contains("First real email window (read-only summaries)", source, StringComparison.Ordinal);
        Assert.Contains("now consumes the same shared", source, StringComparison.Ordinal);
        Assert.Contains("not consume `GmailClientProvider` or `IEmailSender` directly", source, StringComparison.Ordinal);
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
    }

    private sealed class RecordingEmailGateway : IEmailGateway
    {
        public string? LastProjectLabelName { get; private set; }

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
    }

    private sealed class StubConnectorAuthService(bool isAuthenticated) : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; } = isAuthenticated;

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public void Logout() => SetAuthenticated(false);

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public void SetAuthenticated(bool isAuthenticated)
        {
            IsAuthenticated = isAuthenticated;
            AuthStateChanged?.Invoke(isAuthenticated);
        }
    }
}
