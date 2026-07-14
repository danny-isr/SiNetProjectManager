using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailClientProviderLogoutTests
{
    [Fact]
    public void Gmail_disconnect_invalidates_cached_gmail_client()
    {
        var tokenPath = CreateTempTokenDirectory();
        var options = new GmailOptions { TokenStorePath = tokenPath };
        var provider = new GmailClientProvider(options, new TestAppLogger());

        provider.Logout();

        Assert.False(provider.IsSignedIn);
    }

    [Fact]
    public void Logout_deletes_persisted_token_store_directory()
    {
        var tokenPath = CreateTempTokenDirectory();
        File.WriteAllText(Path.Combine(tokenPath, "user"), "{}");

        var options = new GmailOptions { TokenStorePath = tokenPath };
        var provider = new GmailClientProvider(options, new TestAppLogger());

        provider.Logout();

        Assert.False(Directory.Exists(tokenPath));
    }

    [Fact]
    public async Task LogoutAsync_clears_state_and_deletes_token_store()
    {
        var tokenPath = CreateTempTokenDirectory();
        File.WriteAllText(Path.Combine(tokenPath, "user"), "{}");

        var options = new GmailOptions { TokenStorePath = tokenPath };
        var provider = new GmailClientProvider(options, new TestAppLogger());

        // Async logout must acquire the gate via WaitAsync and complete without blocking/deadlock.
        await provider.LogoutAsync();

        Assert.False(provider.IsSignedIn);
        Assert.False(Directory.Exists(tokenPath));
    }

    [Fact]
    public void DeleteTokenStoreDirectory_removes_expanded_path()
    {
        var tokenPath = CreateTempTokenDirectory();
        Directory.CreateDirectory(tokenPath);

        GmailClientProvider.DeleteTokenStoreDirectory(tokenPath);

        Assert.False(Directory.Exists(tokenPath));
    }

    private static string CreateTempTokenDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sinet-gmail-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestAppLogger : IAppLogger
    {
        public void Debug(string message) { }

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
