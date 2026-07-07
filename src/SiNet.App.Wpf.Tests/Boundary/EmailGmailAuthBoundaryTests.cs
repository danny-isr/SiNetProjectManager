using System.IO;
using Xunit;
using SiNet.App.Wpf.Tests.Surfaces.Email;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Boundary guards for Gmail disconnect/reconnect in Email Workbench.</summary>
public sealed class EmailGmailAuthBoundaryTests
{
    [Fact]
    public void Gmail_disconnect_clears_connected_account_status()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var authSource = ReadRepoFile("src/SiNet.Application/Common/IConnectorAuthService.cs");

        Assert.Contains("AuthService.Logout()", listVmSource, StringComparison.Ordinal);
        Assert.Contains("void Logout()", authSource, StringComparison.Ordinal);
        Assert.Contains("AccountStatusDisplay", listVmSource, StringComparison.Ordinal);
        Assert.Contains("לא מחובר ל-Gmail", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gmail_disconnect_clears_email_list()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("ClearEmailState", listVmSource, StringComparison.Ordinal);
        Assert.Contains("ReplaceRows([])", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gmail_disconnect_clears_selected_email_and_preview()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var windowVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("SelectedEmail = null", listVmSource, StringComparison.Ordinal);
        Assert.Contains("ClearSelectedEmailDetails()", windowVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gmail_disconnect_clears_paging_tokens()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("PageTokenStack.Clear()", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SetNextPageToken(null)", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SetCurrentPageNumber(1)", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gmail_disconnect_invalidates_cached_gmail_client()
    {
        var providerSource = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailClientProvider.cs");

        Assert.Contains("DeletePersistedTokenStore", providerSource, StringComparison.Ordinal);
        Assert.Contains("_gmailService = null", providerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gmail_reconnect_loads_first_page_from_new_account()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("ConnectorLoginOptions", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SkipSilentRestore: true", listVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadMailboxAndProjectAsync(resetStack: true)", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gmail_account_status_displays_connected_email()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("מחובר כ:", listVmSource, StringComparison.Ordinal);
        Assert.Contains("AccountStatusDisplay", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Connect_button_visible_when_disconnected()
    {
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("ConnectCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ShowConnectButton", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnect_button_visible_when_connected()
    {
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("DisconnectCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("IsConnected", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_disabled_when_not_connected()
    {
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("CanRefreshEmails", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("CanRefreshEmails", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnect_does_not_use_legacy_window()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.DoesNotContain("EmailManagementView", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void No_Gmail_write_operations_added()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.DoesNotContain("SendAsync", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ModifyLabels", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Connect_gmail_refreshes_account_status_after_success()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("RefreshGmailAccountStatusAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("NotifyAuthProperties", listVmSource, StringComparison.Ordinal);
        Assert.Contains("RefreshGmailAccountStatusAsync().ConfigureAwait(true)", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Account_status_uses_same_credential_source_as_gmail_gateway()
    {
        var extensionsSource = ReadRepoFile("src/SiNet.Infrastructure.Google/GoogleServiceCollectionExtensions.cs");

        Assert.Contains("AddSingleton<GmailClientProvider>", extensionsSource, StringComparison.Ordinal);
        Assert.Contains("IConnectorAuthService", extensionsSource, StringComparison.Ordinal);
        Assert.Contains("IEmailGateway", extensionsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void No_stale_connected_email_after_account_switch()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var windowVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("TryRestoreSessionAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("AccountStatusChanged", listVmSource, StringComparison.Ordinal);
        Assert.Contains("RefreshAuthDisplay", windowVmSource, StringComparison.Ordinal);
        Assert.Contains("UiThread.Run", listVmSource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_LIST_MIGRATION.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
