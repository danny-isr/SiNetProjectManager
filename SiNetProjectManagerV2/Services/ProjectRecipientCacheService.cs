using System.Diagnostics;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.DTOs.Email;
using SiNetSQL.Helpers;
using SiNetSQL.Services.EmailOutbound;
using SiOffice.GoogleConnector;
using SiOffice.GoogleConnector.Logging;

namespace SiNetProjectManagerV2.Services;

public sealed class ProjectRecipientCacheService
{
    private const int MaxGmailMessages = 200;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(20);
    private static readonly Regex EmailRegex = new(@"(?:(?<name>[^<,;]+)\s*)?<(?<email>[^<>\s,;]+@[^<>\s,;]+)>|(?<email>[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory;
    private readonly IOutboundMailService _mailService;
    private readonly GoogleService _googleService;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly Dictionary<int, CacheEntry> _cache = [];

    public ProjectRecipientCacheService(
        IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
        IOutboundMailService mailService,
        GoogleService googleService)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));
        _googleService = googleService ?? throw new ArgumentNullException(nameof(googleService));
    }

    public async Task<IReadOnlyList<EmailRecipientSuggestion>> LoadAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(projectId, out var entry) && DateTime.UtcNow - entry.LoadedAtUtc <= CacheTtl)
                return entry.Suggestions;
        }
        finally
        {
            _cacheGate.Release();
        }

        var loaded = await LoadCoreAsync(projectId, cancellationToken);

        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            _cache[projectId] = new CacheEntry(DateTime.UtcNow, loaded.ToList());
        }
        finally
        {
            _cacheGate.Release();
        }

        return loaded;
    }

    private async Task<List<EmailRecipientSuggestion>> LoadCoreAsync(
        int projectId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var suggestions = new Dictionary<string, EmailRecipientSuggestion>(StringComparer.OrdinalIgnoreCase);
        var gmailRecipientsFound = 0;
        var messagesScanned = 0;
        var reachedLimit = false;
        var gmailQuery = "newer_than:3m";
        var result = "Success";
        var reason = "(none)";

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Place)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        try
        {
            if (project != null && await _mailService.EnsureAuthenticatedAsync("EmailRecipientCacheLoad", cancellationToken))
            {
                var projectName = ProjectLabelFormatter.FormatProjectName(project.Id, project.NameAndNumber, project.Title);
                var location = ProjectLabelFormatter.GetLocation(project.Place?.Title);
                var gmailResult = await _googleService.LoadProjectRecipientHeadersAsync(
                    location,
                    projectName,
                    MaxGmailMessages,
                    cancellationToken);

                messagesScanned = gmailResult.MessagesScanned;
                reachedLimit = gmailResult.ReachedLimit;
                gmailQuery = gmailResult.GmailQuery;

                foreach (var header in gmailResult.Headers)
                {
                    foreach (var suggestion in ParseHeaderRecipients(header.HeaderValue, header.HeaderName))
                    {
                        gmailRecipientsFound++;
                        AddSuggestion(suggestions, suggestion.DisplayName, suggestion.Email, suggestion.Source ?? "GmailLabel");
                    }
                }
            }
            else if (project == null)
            {
                result = "Failed";
                reason = "ProjectNotFound";
            }
            else
            {
                result = "Failed";
                reason = "GmailNotAuthenticated";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = "Failed";
            reason = ex.Message;
        }

        await AddDbFallbackSuggestionsAsync(db, projectId, suggestions, cancellationToken);

        stopwatch.Stop();
        ReportLogger.Info(
            $"Operation=EmailRecipientCacheLoad ProjectId={projectId} Source=GmailLabel GmailQuery={gmailQuery} TimeWindow=3m MaxMessages={MaxGmailMessages} " +
            $"MessagesScanned={messagesScanned} RecipientsFound={gmailRecipientsFound} UniqueRecipientsFound={suggestions.Count} " +
            $"ReachedLimit={reachedLimit.ToString().ToLowerInvariant()} DurationMs={stopwatch.ElapsedMilliseconds} Result={result} Reason={reason}");

        return suggestions.Values
            .OrderBy(s => s.DisplayName ?? s.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<EmailRecipientSuggestion> ParseHeaderRecipients(string headerValue, string headerName)
    {
        foreach (Match match in EmailRegex.Matches(headerValue))
        {
            var email = match.Groups["email"].Value.Trim();
            if (!IsValidEmailAddress(email))
                continue;

            var displayName = match.Groups["name"].Success
                ? match.Groups["name"].Value.Trim().Trim('"')
                : null;

            yield return new EmailRecipientSuggestion
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                Email = email,
                Source = $"GmailLabel:{headerName}"
            };
        }
    }

    private static async Task AddDbFallbackSuggestionsAsync(
        SiNetSQLDbContext db,
        int projectId,
        Dictionary<string, EmailRecipientSuggestion> suggestions,
        CancellationToken cancellationToken)
    {
        var projectContacts = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId && p.Contacts != null && p.Contacts.Email != null && p.Contacts.Email != "")
            .Select(p => new { p.Contacts!.FullName, p.Contacts.FirstName, p.Contacts.Email })
            .ToListAsync(cancellationToken);

        foreach (var contact in projectContacts)
            AddSuggestion(suggestions, contact.FullName ?? contact.FirstName, contact.Email, "Project");

        var plannerContacts = await db.ProjectPlanners
            .AsNoTracking()
            .Where(pp => pp.ProjctId == projectId && pp.Contacts != null && pp.Contacts.Email != null && pp.Contacts.Email != "")
            .Select(pp => new { pp.Contacts!.FullName, pp.Contacts.FirstName, pp.Contacts.Email })
            .ToListAsync(cancellationToken);

        foreach (var contact in plannerContacts)
            AddSuggestion(suggestions, contact.FullName ?? contact.FirstName, contact.Email, "Planner");

        var contacts = await db.Contacts
            .AsNoTracking()
            .Where(c => c.Email != null && c.Email != "")
            .OrderBy(c => c.FullName ?? c.FirstName ?? c.Email)
            .Select(c => new { c.FullName, c.FirstName, c.Email })
            .Take(500)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
            AddSuggestion(suggestions, contact.FullName ?? contact.FirstName, contact.Email, "Contact");
    }

    private static void AddSuggestion(
        Dictionary<string, EmailRecipientSuggestion> suggestions,
        string? displayName,
        string? value,
        string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var email in value.Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsValidEmailAddress(email) || suggestions.ContainsKey(email))
                continue;

            suggestions[email] = new EmailRecipientSuggestion
            {
                DisplayName = displayName,
                Email = email,
                Source = source
            };
        }
    }

    private static bool IsValidEmailAddress(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record CacheEntry(DateTime LoadedAtUtc, List<EmailRecipientSuggestion> Suggestions);
}
