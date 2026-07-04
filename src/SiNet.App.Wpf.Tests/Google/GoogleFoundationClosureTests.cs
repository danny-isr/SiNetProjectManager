using System.ComponentModel;
using System.IO;
using SiNet.App.Wpf.Inbox;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
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
    public void Email_window_shell_does_not_consume_native_gmail_runtime_directly()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.DoesNotContain("GmailClientProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IConnectorAuthService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmailSender", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_window_shell_keeps_send_and_modify_commands_stub_only_pending_policy_decision()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("ReplyCommand = Stub();", source, StringComparison.Ordinal);
        Assert.Contains("ForwardCommand = Stub();", source, StringComparison.Ordinal);
        Assert.Contains("MarkHandledCommand = Stub();", source, StringComparison.Ordinal);
        Assert.Contains("ArchiveCommand = Stub();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Google_boundary_doc_records_drive_sheets_reports_defer_policy()
    {
        var source = ReadRepoFile("docs/GOOGLE_BOUNDARY.md");

        Assert.Contains("Drive / Sheets / Reports defer decision", source, StringComparison.Ordinal);
        Assert.Contains("Requires an approved `ProjectFiles` / storage-destination slice", source, StringComparison.Ordinal);
        Assert.Contains("No runtime movement of Drive, Sheets, or report/export code until a real consumer slice is named.", source, StringComparison.Ordinal);
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
