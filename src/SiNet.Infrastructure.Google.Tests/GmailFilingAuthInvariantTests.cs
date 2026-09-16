using SiNet.Application.Abstractions.Logging;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

/// <summary>
/// File/label operations must keep the Gmail session authenticated.
/// Only explicit logout / account-switch / auth failure may disconnect.
/// </summary>
public sealed class GmailFilingAuthInvariantTests
{
    private const string ProjectPath = "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון";

    [Fact]
    public async Task Existing_label_file_refresh_keeps_session_authenticated()
    {
        var session = new TrackingSession("s0");
        session.Add("INBOX", "INBOX");
        session.Add("Label_X", ProjectPath);
        var catalog = new GmailLabelCatalog(session, new NullLogger());
        await catalog.GetMapAsync();

        catalog.NotifyCreated("Label_X", ProjectPath);
        var map = await catalog.ResolveForMessageAsync("msg-A", ["INBOX", "Label_X"]);
        catalog.Invalidate("post-write-refresh");
        session.Add("Label_X", ProjectPath);
        var refreshed = await catalog.GetMapAsync();
        var again = await catalog.ResolveForMessageAsync("msg-A", ["INBOX", "Label_X"]);

        Assert.True(session.Authenticated);
        Assert.Equal(0, session.LogoutCount);
        Assert.Empty(session.AuthFalseEvents);
        Assert.Equal("s0", session.SessionKey);
        Assert.Equal(ProjectPath, map["Label_X"].Name);
        Assert.Equal(ProjectPath, refreshed["Label_X"].Name);
        Assert.Equal(ProjectPath, again["Label_X"].Name);
    }

    [Fact]
    public async Task New_label_create_attach_catalog_refresh_keeps_session_authenticated()
    {
        var session = new TrackingSession("s0");
        session.Add("INBOX", "INBOX");
        var catalog = new GmailLabelCatalog(session, new NullLogger());
        await catalog.GetMapAsync();
        var generationBefore = catalog.Generation;

        catalog.NotifyCreated("Label_new", ProjectPath);
        var attached = await catalog.ResolveForMessageAsync("msg-B", ["INBOX", "Label_new"]);
        catalog.Invalidate("forced-label-catalog-refresh");
        session.Add("Label_new", ProjectPath);
        var afterReload = await catalog.ResolveForMessageAsync("msg-B", ["INBOX", "Label_new"]);

        Assert.True(session.Authenticated);
        Assert.Equal(0, session.LogoutCount);
        Assert.Empty(session.AuthFalseEvents);
        Assert.Equal("s0", catalog.CachedSessionKey);
        Assert.Equal("s0", session.SessionKey);
        Assert.True(catalog.Generation > generationBefore);
        Assert.Equal(ProjectPath, attached["Label_new"].Name);
        Assert.Equal(ProjectPath, afterReload["Label_new"].Name);
        var names = GmailEmailGateway.ResolveLabelNames(
            new global::Google.Apis.Gmail.v1.Data.Message
            {
                Id = "msg-B",
                LabelIds = ["INBOX", "Label_new"]
            },
            GmailEmailGateway.ToGoogleLabelMap(afterReload));
        Assert.Contains(ProjectPath, names);
    }

    [Fact]
    public async Task Label_list_create_modify_get_and_reload_never_logout()
    {
        var session = new TrackingSession("s0");
        session.Add("INBOX", "INBOX");
        var catalog = new GmailLabelCatalog(session, new NullLogger());

        await catalog.GetMapAsync();
        catalog.NotifyCreated("Label_parent", "פרויקטים_משרד");
        catalog.NotifyCreated("Label_new", ProjectPath);
        catalog.Invalidate("unknown-id-self-heal");
        session.Add("Label_parent", "פרויקטים_משרד");
        session.Add("Label_new", ProjectPath);
        await catalog.ResolveForMessageAsync("msg-C", ["INBOX", "Label_new"]);
        await catalog.GetMapAsync();

        Assert.True(session.Authenticated);
        Assert.Equal(0, session.LogoutCount);
        Assert.Empty(session.AuthFalseEvents);
        Assert.Equal(session.SessionKey, catalog.CachedSessionKey);
    }

    [Fact]
    public void Label_catalog_and_modify_paths_do_not_reset_auth()
    {
        var catalog = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.Infrastructure.Google", "GmailLabelCatalog.cs"));
        var directory = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.Infrastructure.Google", "GmailApiLabelDirectory.cs"));
        var modify = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.Infrastructure.Google", "GmailEmailModifyService.cs"));
        var gateway = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.Infrastructure.Google", "GmailEmailGateway.cs"));
        var provider = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.Infrastructure.Google", "GmailClientProvider.cs"));

        Assert.DoesNotContain("LogoutAsync", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("LogoutAsync", directory, StringComparison.Ordinal);
        Assert.DoesNotContain("LogoutAsync", modify, StringComparison.Ordinal);
        Assert.DoesNotContain("LogoutAsync", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("SignInInteractive", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("SignInInteractive", directory, StringComparison.Ordinal);
        Assert.DoesNotContain("SignInInteractive", modify, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletePersistedTokenStore", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("_credential is null", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-out", provider, StringComparison.Ordinal);
        Assert.Contains("\"s\" + _sessionGeneration", provider, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("SiNet.sln not found.");
    }

    private sealed class TrackingSession(string sessionKey) : IGmailLabelDirectory
    {
        private readonly Dictionary<string, GmailLabelRecord> _labels = new(StringComparer.Ordinal);

        public string SessionKey { get; set; } = sessionKey;

        public bool Authenticated { get; private set; } = true;

        public int LogoutCount { get; private set; }

        public List<bool> AuthFalseEvents { get; } = [];

        public void Add(string id, string name) =>
            _labels[id] = new GmailLabelRecord(id, name);

        public void Logout()
        {
            LogoutCount++;
            Authenticated = false;
            AuthFalseEvents.Add(false);
        }

        public Task<IReadOnlyList<GmailLabelRecord>> ListAsync(CancellationToken cancellationToken)
        {
            if (!Authenticated)
                throw new InvalidOperationException("Gmail session unavailable.");
            return Task.FromResult<IReadOnlyList<GmailLabelRecord>>(_labels.Values.ToList());
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
