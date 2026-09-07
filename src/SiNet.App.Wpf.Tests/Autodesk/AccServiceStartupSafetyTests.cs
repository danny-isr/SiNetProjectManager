using System.IO;
using MyOffice.AutodeskConnector;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

/// <summary>
/// AccService must reach Kestrel / SCM without Autodesk OAuth, and must never
/// open interactive browser auth on server-side 3-legged paths.
/// </summary>
public sealed class AccServiceStartupSafetyTests
{
    [Fact]
    public void AccService_Program_does_not_call_Autodesk_profile_or_token_before_app_Run()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "SiOffice.AccService", "Program.cs"));

        Assert.Contains("app.Run()", program, StringComparison.Ordinal);
        Assert.Contains("NonInteractiveThreeLeggedTokenProvider", program, StringComparison.Ordinal);
        Assert.Contains("refreshTokenFileExists", program, StringComparison.Ordinal);
        Assert.Contains("deferred-until-admin-identity-endpoint", program, StringComparison.Ordinal);

        Assert.DoesNotContain("AccServiceAdminTokenProfile", program, StringComparison.Ordinal);
        Assert.DoesNotContain("GetThreeLeggedAdminTokenAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("GetThreeLeggedAdminToken", program, StringComparison.Ordinal);
        Assert.DoesNotContain("PerformBrowserAuthorization", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AccService_health_endpoint_has_no_token_or_oauth_dependency()
    {
        var endpoints = File.ReadAllText(Path.Combine(FindRepoRoot(), "SiOffice.AccService", "Endpoints", "AccEndpoints.cs"));
        var healthIdx = endpoints.IndexOf("MapGet(\"/health\"", StringComparison.Ordinal);
        var adminIdx = endpoints.IndexOf("MapGet(\"/admin-identity\"", StringComparison.Ordinal);
        Assert.True(healthIdx >= 0);
        Assert.True(adminIdx > healthIdx);

        var healthBlock = endpoints.Substring(healthIdx, adminIdx - healthIdx);
        Assert.Contains("status = \"ok\"", healthBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("GetThreeLeggedAdminTokenAsync", healthBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ITokenProvider", healthBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", healthBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("autodesk.com", healthBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminTokenProfile_ResolveAsync_sets_interactive_browser_auth_suppressed()
    {
        var spy = new SuppressionSpyTokenProvider(hasRefresh: true);
        var result = await AccServiceAdminTokenProfile.ResolveAsync(spy);

        Assert.True(spy.ObservedSuppressed);
        Assert.Equal(1, spy.GetThreeLeggedCallCount);
        // Spy returns a fake access token; userinfo HTTP will fail closed without network success.
        Assert.False(result.ProfileResolved);
    }

    [Fact]
    public async Task AdminTokenProfile_missing_token_does_not_invoke_GetThreeLegged()
    {
        var spy = new SuppressionSpyTokenProvider(hasRefresh: false);
        await AccServiceAdminTokenProfile.ResolveAsync(spy);
        Assert.Equal(0, spy.GetThreeLeggedCallCount);
    }

    [Fact]
    public async Task NonInteractive_decorator_suppresses_browser_auth_for_inner_provider()
    {
        var inner = new SuppressionSpyTokenProvider(hasRefresh: true);
        var wrapped = new NonInteractiveThreeLeggedTokenProvider(inner);

        await wrapped.GetThreeLeggedAdminTokenAsync();

        Assert.True(inner.ObservedSuppressed);
        Assert.Equal(1, inner.GetThreeLeggedCallCount);
    }

    [Fact]
    public async Task NonInteractive_decorator_propagates_InteractiveAuthSuppressedException()
    {
        var inner = new ThrowingInteractiveAuthTokenProvider();
        var wrapped = new NonInteractiveThreeLeggedTokenProvider(inner);

        await Assert.ThrowsAsync<TokenProvider.InteractiveAuthSuppressedException>(
            () => wrapped.GetThreeLeggedAdminTokenAsync());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "SiOffice.AccService", "Program.cs")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    private sealed class SuppressionSpyTokenProvider : ITokenProvider
    {
        private readonly bool _hasRefresh;

        public SuppressionSpyTokenProvider(bool hasRefresh) => _hasRefresh = hasRefresh;

        public bool ObservedSuppressed { get; private set; }
        public int GetThreeLeggedCallCount { get; private set; }

        public Task<string> GetTwoLeggedTokenAsync(CancellationToken ct = default) =>
            Task.FromResult("two");

        public Task<string> GetThreeLeggedAdminTokenAsync(CancellationToken ct = default)
        {
            GetThreeLeggedCallCount++;
            ObservedSuppressed = TokenProvider.IsInteractiveBrowserAuthSuppressed;
            if (!_hasRefresh)
            {
                throw new TokenProvider.InteractiveAuthSuppressedException("suppressed");
            }

            return Task.FromResult("access-token");
        }

        public bool HasThreeLeggedRefreshToken => _hasRefresh;
        public string ClientId => "client";
        public string ThreeLeggedRefreshTokenStoragePath =>
            Path.Combine(Path.GetTempPath(), "SiNet", "Autodesk", "AccService", "refresh_token.json");
        public AutodeskTokenStorePurpose TokenStorePurpose => AutodeskTokenStorePurpose.AccServiceAdmin;
    }

    private sealed class ThrowingInteractiveAuthTokenProvider : ITokenProvider
    {
        public Task<string> GetTwoLeggedTokenAsync(CancellationToken ct = default) =>
            Task.FromResult("two");

        public Task<string> GetThreeLeggedAdminTokenAsync(CancellationToken ct = default)
        {
            if (!TokenProvider.IsInteractiveBrowserAuthSuppressed)
            {
                throw new InvalidOperationException("expected suppress scope");
            }

            throw new TokenProvider.InteractiveAuthSuppressedException("AuthUnavailable");
        }

        public bool HasThreeLeggedRefreshToken => true;
        public string ClientId => "client";
        public string ThreeLeggedRefreshTokenStoragePath => @"C:\tmp\refresh_token.json";
        public AutodeskTokenStorePurpose TokenStorePurpose => AutodeskTokenStorePurpose.AccServiceAdmin;
    }
}
