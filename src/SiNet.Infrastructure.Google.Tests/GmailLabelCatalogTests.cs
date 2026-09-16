using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Logging;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailLabelCatalogTests
{
    private const string ProjectPath = "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון";

    [Fact]
    public void Legacy_resolve_silently_drops_unknown_label_ids()
    {
        var map = new Dictionary<string, Label>(StringComparer.Ordinal)
        {
            ["INBOX"] = new Label { Id = "INBOX", Name = "INBOX" }
        };
        var message = new Message
        {
            Id = "msg-A",
            LabelIds = ["INBOX", "Label_X"]
        };

        var names = GmailEmailGateway.ResolveLabelNames(message, map);

        Assert.Equal(["INBOX"], names);
        Assert.DoesNotContain(ProjectPath, names);
    }

    [Fact]
    public async Task New_label_after_cache_prime_is_resolved_after_single_refresh()
    {
        var directory = new FakeDirectory("acct-1");
        directory.Add("INBOX", "INBOX");
        var catalog = new GmailLabelCatalog(directory, new NullLogger());
        await catalog.GetMapAsync();
        Assert.Equal(1, catalog.ListCallCount);

        directory.Add("Label_X", ProjectPath);

        var map = await catalog.ResolveForMessageAsync("msg-A", ["INBOX", "Label_X"]);

        Assert.Equal(2, catalog.ListCallCount);
        Assert.Equal(ProjectPath, map["Label_X"].Name);
        var names = GmailEmailGateway.ResolveLabelNames(
            new Message { Id = "msg-A", LabelIds = ["INBOX", "Label_X"] },
            GmailEmailGateway.ToGoogleLabelMap(map));
        Assert.Contains(ProjectPath, names);
    }

    [Fact]
    public async Task NotifyCreated_makes_new_label_visible_without_another_list()
    {
        var directory = new FakeDirectory("acct-1");
        directory.Add("INBOX", "INBOX");
        var catalog = new GmailLabelCatalog(directory, new NullLogger());
        await catalog.GetMapAsync();

        catalog.NotifyCreated("Label_X", ProjectPath);
        var map = await catalog.GetMapAsync();

        Assert.Equal(1, catalog.ListCallCount);
        Assert.Equal(ProjectPath, map["Label_X"].Name);
    }

    [Fact]
    public async Task Rename_and_delete_update_the_cached_map()
    {
        var directory = new FakeDirectory("acct-1");
        directory.Add("Label_X", ProjectPath);
        var catalog = new GmailLabelCatalog(directory, new NullLogger());
        await catalog.GetMapAsync();

        catalog.NotifyRenamed("Label_X", "פרויקטים_משרד/תל אביב/(1042)חדש");
        var renamed = await catalog.GetMapAsync();
        Assert.Equal("פרויקטים_משרד/תל אביב/(1042)חדש", renamed["Label_X"].Name);
        Assert.Equal(1, catalog.ListCallCount);

        catalog.NotifyDeleted("Label_X");
        var deleted = await catalog.GetMapAsync();
        Assert.False(deleted.ContainsKey("Label_X"));
        Assert.Equal(1, catalog.ListCallCount);
    }

    [Fact]
    public async Task Parallel_unknown_ids_share_one_labels_list()
    {
        var directory = new FakeDirectory("acct-1");
        directory.Add("INBOX", "INBOX");
        directory.ListDelay = TimeSpan.FromMilliseconds(80);
        var catalog = new GmailLabelCatalog(directory, new NullLogger());
        await catalog.GetMapAsync();
        directory.Add("Label_X", ProjectPath);

        var tasks = Enumerable.Range(0, 10)
            .Select(i => catalog.ResolveForMessageAsync("msg-" + i, ["INBOX", "Label_X"]))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(2, catalog.ListCallCount);
        Assert.All(tasks, task => Assert.Equal(ProjectPath, task.Result["Label_X"].Name));
    }

    [Fact]
    public async Task Session_switch_does_not_reuse_previous_account_map()
    {
        var directory = new FakeDirectory("acct-1");
        directory.Add("Label_A", "AccountOne/A");
        var catalog = new GmailLabelCatalog(directory, new NullLogger());
        var first = await catalog.GetMapAsync();
        Assert.True(first.ContainsKey("Label_A"));

        directory.SessionKey = "acct-2";
        directory.Clear();
        directory.Add("Label_B", "AccountTwo/B");

        var second = await catalog.GetMapAsync();
        Assert.False(second.ContainsKey("Label_A"));
        Assert.True(second.ContainsKey("Label_B"));
        Assert.Equal("acct-2", catalog.CachedSessionKey);
    }

    [Fact]
    public async Task Existing_label_present_before_prime_does_not_refresh()
    {
        var directory = new FakeDirectory("acct-1");
        directory.Add("INBOX", "INBOX");
        directory.Add("Label_X", ProjectPath);
        var catalog = new GmailLabelCatalog(directory, new NullLogger());

        var map = await catalog.ResolveForMessageAsync("msg-A", ["INBOX", "Label_X"]);

        Assert.Equal(1, catalog.ListCallCount);
        Assert.Equal(ProjectPath, map["Label_X"].Name);
    }

    [Fact]
    public async Task Filing_sequence_keeps_same_session_and_does_not_require_logout()
    {
        var directory = new FakeDirectory("s0");
        directory.Add("INBOX", "INBOX");
        var catalog = new GmailLabelCatalog(directory, new NullLogger());
        await catalog.GetMapAsync();
        var sessionBefore = catalog.CachedSessionKey;

        catalog.NotifyCreated("Label_X", ProjectPath);
        var map = await catalog.ResolveForMessageAsync("msg-A", ["INBOX", "Label_X"]);

        Assert.Equal(sessionBefore, catalog.CachedSessionKey);
        Assert.Equal("s0", directory.SessionKey);
        Assert.Equal(ProjectPath, map["Label_X"].Name);
        Assert.Equal(1, catalog.ListCallCount);
    }

    [Fact]
    public void Session_identity_is_generation_only_not_live_auth()
    {
        var providerSource = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "SiNet.Infrastructure.Google", "GmailClientProvider.cs"));
        var guardSource = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "SiNet.Infrastructure.Sql", "Services", "Identity", "IdentityOperationGuard.cs"));

        Assert.DoesNotContain("signed-out", providerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DisconnectGoogleOnMismatch: kind is IdentityOperationKind.GmailWrite",
            guardSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DisconnectGoogleOnMismatch: kind is IdentityOperationKind.CrossSystemWorkflow",
            guardSource,
            StringComparison.Ordinal);
        Assert.Contains("DisconnectGoogleOnMismatch: false", guardSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Genuinely_unknown_id_stays_unresolved_after_refresh()
    {
        var directory = new FakeDirectory("acct-1");
        directory.Add("INBOX", "INBOX");
        var catalog = new GmailLabelCatalog(directory, new NullLogger());
        await catalog.GetMapAsync();

        var map = await catalog.ResolveForMessageAsync("msg-A", ["INBOX", "Label_ghost"]);

        Assert.Equal(2, catalog.ListCallCount);
        Assert.False(map.ContainsKey("Label_ghost"));
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

    private sealed class FakeDirectory(string sessionKey) : IGmailLabelDirectory
    {
        private readonly Dictionary<string, GmailLabelRecord> _labels = new(StringComparer.Ordinal);

        public string SessionKey { get; set; } = sessionKey;

        public TimeSpan ListDelay { get; set; }

        public void Add(string id, string name) =>
            _labels[id] = new GmailLabelRecord(id, name);

        public void Clear() => _labels.Clear();

        public async Task<IReadOnlyList<GmailLabelRecord>> ListAsync(CancellationToken cancellationToken)
        {
            if (ListDelay > TimeSpan.Zero)
                await Task.Delay(ListDelay, cancellationToken).ConfigureAwait(false);
            return _labels.Values.ToList();
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
