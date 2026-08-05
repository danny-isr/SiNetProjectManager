using System.Text.Json;
using System.Text.Json.Serialization;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// LocalAppData JSON journal of Gmail label renames/deletes per mailbox (DEV-009 §4.2).
/// </summary>
internal sealed class LocalGmailLabelChangeJournal(IAppLogger logger) : IGmailLabelChangeJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal static string ResolveDirectory(string? localAppDataOverride = null)
    {
        var root = string.IsNullOrWhiteSpace(localAppDataOverride)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataOverride;
        return Path.Combine(root, "SiNet", "GmailLabelJournal");
    }

    public async Task AppendAsync(
        string mailboxEmail,
        GmailLabelJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxEmail);
        ArgumentNullException.ThrowIfNull(entry);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = ResolveDirectory();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, GmailLabelJournalRetention.SanitizeMailboxFileName(mailboxEmail) + ".json");

            GmailLabelJournalDocument? document = null;
            if (File.Exists(path))
            {
                await using var read = File.OpenRead(path);
                document = await JsonSerializer
                    .DeserializeAsync<GmailLabelJournalDocument>(read, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            var existing = document?.Entries ?? [];
            var utcNow = DateTime.UtcNow;
            var merged = existing
                .Select(static e => e.ToEntry())
                .Concat([entry])
                .ToList();
            var pruned = GmailLabelJournalRetention.Prune(merged, utcNow);
            var next = new GmailLabelJournalDocument
            {
                MailboxEmail = mailboxEmail.Trim(),
                Entries = pruned.Select(GmailLabelJournalEntryDocument.FromEntry).ToList(),
            };

            var tmp = path + ".tmp";
            await using (var write = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(write, next, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
        }
        catch (Exception ex)
        {
            _logger.Error($"[GmailLabelJournal] Append failed for mailbox '{mailboxEmail}': {ex.Message}", ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class GmailLabelJournalDocument
    {
        public string MailboxEmail { get; set; } = string.Empty;

        public List<GmailLabelJournalEntryDocument> Entries { get; set; } = [];
    }

    private sealed class GmailLabelJournalEntryDocument
    {
        public string LabelId { get; set; } = string.Empty;
        public GmailLabelJournalAction Action { get; set; }
        public string OldFullPath { get; set; } = string.Empty;
        public string? NewFullPath { get; set; }
        public int? ProjectNumber { get; set; }
        public DateTime ChangedAtUtc { get; set; }
        public GmailLabelJournalSource Source { get; set; }
        public List<string> MessageIds { get; set; } = [];

        public GmailLabelJournalEntry ToEntry() =>
            new(
                LabelId,
                Action,
                OldFullPath,
                NewFullPath,
                ProjectNumber,
                ChangedAtUtc,
                Source,
                MessageIds);

        public static GmailLabelJournalEntryDocument FromEntry(GmailLabelJournalEntry entry) =>
            new()
            {
                LabelId = entry.LabelId,
                Action = entry.Action,
                OldFullPath = entry.OldFullPath,
                NewFullPath = entry.NewFullPath,
                ProjectNumber = entry.ProjectNumber,
                ChangedAtUtc = entry.ChangedAtUtc,
                Source = entry.Source,
                MessageIds = entry.MessageIds.ToList(),
            };
    }
}
