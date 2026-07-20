using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Diagnostics;
using SiNet.Domain.ValueObjects;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Native Gmail implementation of <see cref="IEmailGateway"/>. Reads project-scoped email
/// summaries directly from the Gmail API via <see cref="GmailClientProvider"/>, with no WPF or
/// legacy <c>GoogleService</c> dependency.
/// <para>
/// Mailbox layout mirrors the existing system: project emails are filed under the Gmail label
/// <c>{root}/{location}/{projectName}</c>. When the mailbox is unavailable (not signed in),
/// reads return empty / <c>null</c> rather than throwing, per the <see cref="IEmailGateway"/> contract.
/// </para>
/// </summary>
public sealed class GmailEmailGateway : IEmailGateway
{
    private const int InternalDrainPageSize = 100;
    public const int MailboxPageSizeCap = 50;
    private const int UnreadCountPageSize = 500;
    private const int UnreadCountMaxPages = 3;
    internal const string InboxPrimaryQuery = EmailMailboxQueryComposer.InboxPrimaryQuery;
    internal const string AllMailQuery = EmailMailboxQueryComposer.AllMailQuery;
    internal const string InboxPrimaryUnreadQuery = EmailMailboxQueryComposer.InboxPrimaryUnreadQuery;
    internal const string AllMailUnreadQuery = EmailMailboxQueryComposer.AllMailUnreadQuery;
    private static readonly string[] MetadataHeaders = { "Subject", "From", "To", "Date", "Message-ID" };
    internal const string SummaryFieldsMask =
        "id,threadId,labelIds,snippet," +
        "payload(mimeType,headers,parts(mimeType,filename,headers,body(attachmentId),parts))";
    internal const string MetadataSummaryFieldsMask =
        "id,threadId,labelIds,snippet,internalDate,payload(headers)";
    private const int SummaryFetchConcurrency = 10;

    private IReadOnlyDictionary<string, Label>? _cachedLabelMap;
    private GmailService? _cachedLabelMapService;

    private readonly GmailClientProvider _provider;
    private readonly IAppLogger _logger;

    public GmailEmailGateway(GmailClientProvider provider, IAppLogger logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
        string location,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return Array.Empty<EmailSummary>();
        }

        var labelPath = $"{_provider.RootLabel}/{location}/{projectName}";

        var labelId = await ResolveLabelIdAsync(gmail, labelPath, _logger, cancellationToken).ConfigureAwait(false);
        if (labelId == null)
        {
            _logger.Warn($"[Gmail] Label not found: {labelPath}");
            return Array.Empty<EmailSummary>();
        }

        return await GetSummariesForLabelIdsAsync(gmail, [labelId], labelPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
        string projectLabelName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectLabelName))
        {
            return Array.Empty<EmailSummary>();
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return Array.Empty<EmailSummary>();
        }

        var labels = await GmailRetry.ExecuteAsync(
            ct => gmail.Users.Labels.List("me").ExecuteAsync(ct),
            _logger,
            "Labels.List(project label lookup)",
            cancellationToken).ConfigureAwait(false);
        var rootPrefix = _provider.RootLabel + "/";
        var labelIds = labels.Labels?
            .Where(static l => !string.IsNullOrWhiteSpace(l.Name) && !string.IsNullOrWhiteSpace(l.Id))
            .Where(l => l.Name!.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(l =>
            {
                var parts = l.Name!.Split('/');
                return parts.Length >= 2
                    && string.Equals(parts[^1], projectLabelName.Trim(), StringComparison.OrdinalIgnoreCase);
            })
            .Select(l => l.Id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (labelIds is null || labelIds.Length == 0)
        {
            _logger.Warn($"[Gmail] No project labels found for '{projectLabelName}'.");
            return Array.Empty<EmailSummary>();
        }

        return await GetSummariesForLabelIdsAsync(gmail, labelIds, projectLabelName.Trim(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmailMailboxPage> GetMailboxPageAsync(
        EmailMailboxQuery query,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = query.PageSize <= 0
            ? EmailMailboxQuery.DefaultPageSize
            : Math.Min(query.PageSize, MailboxPageSizeCap);

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return new EmailMailboxPage(Array.Empty<EmailSummary>(), pageSize, null, false);
        }

        var labelMap = await LoadLabelMapAsync(gmail, cancellationToken).ConfigureAwait(false);

        string? listQuery = null;
        IReadOnlyList<string>? labelIds = null;

        if (!string.IsNullOrWhiteSpace(query.LabelId))
        {
            labelIds = [query.LabelId.Trim()];
        }
        else if (!string.IsNullOrWhiteSpace(query.OptionalProjectLabel))
        {
            labelIds = ResolveProjectLabelIds(labelMap, query.OptionalProjectLabel.Trim());
            if (labelIds.Count == 0)
            {
                _logger.Warn($"[Gmail] Optional project label not found: '{query.OptionalProjectLabel}'.");
                return new EmailMailboxPage(Array.Empty<EmailSummary>(), pageSize, null, false);
            }
        }
        else
        {
            listQuery = EmailMailboxQueryComposer.BuildSearchQuery(query, _provider.DefaultMailboxQuery);
        }

        var listRequest = gmail.Users.Messages.List("me");
        listRequest.MaxResults = pageSize;
        listRequest.PageToken = pageToken;
        if (labelIds is { Count: > 0 })
        {
            listRequest.LabelIds = labelIds.ToArray();
        }
        else if (!string.IsNullOrWhiteSpace(listQuery))
        {
            listRequest.Q = listQuery;
        }

        ListMessagesResponse listResponse;
        try
        {
            listResponse = await GmailRetry.ExecuteAsync(
                ct => listRequest.ExecuteAsync(ct),
                _logger,
                "Messages.List(mailbox page)",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Gmail] Messages.List(mailbox page) failed: {ex.Message}", ex);
            return new EmailMailboxPage(Array.Empty<EmailSummary>(), pageSize, null, false);
        }

        if (listResponse.Messages is null || listResponse.Messages.Count == 0)
        {
            return new EmailMailboxPage(Array.Empty<EmailSummary>(), pageSize, listResponse.NextPageToken, false);
        }

        var summaries = await FetchSummariesParallelAsync(
            gmail,
            listResponse.Messages,
            labelMap,
            cancellationToken).ConfigureAwait(false);

        var hasNext = !string.IsNullOrEmpty(listResponse.NextPageToken);
        return new EmailMailboxPage(summaries, pageSize, listResponse.NextPageToken, hasNext);
    }

    public async Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
        EmailMailboxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var scopeDescription = EmailMailboxQueryComposer.DescribeMailboxScope(query);
        if (EmailMailboxQueryComposer.HasNonScopeListFilters(query))
        {
            return new EmailMailboxUnreadCount(0, IsExact: false, scopeDescription);
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return new EmailMailboxUnreadCount(0, IsExact: true, scopeDescription);
        }

        if (query.MailboxScope == EmailMailboxScope.Label)
        {
            return await GetLabelScopeUnreadCountAsync(gmail, query, scopeDescription, cancellationToken)
                .ConfigureAwait(false);
        }

        var unreadQuery = EmailMailboxQueryComposer.BuildUnreadCountQuery(query, _provider.DefaultMailboxQuery);
        var count = await CountMessagesByQueryAsync(gmail, unreadQuery, _logger, cancellationToken).ConfigureAwait(false);
        return new EmailMailboxUnreadCount(count, IsExact: true, scopeDescription);
    }

    public async Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default)
    {
        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return Array.Empty<GmailLabelInfo>();
        }

        var labelMap = await LoadLabelMapAsync(gmail, cancellationToken).ConfigureAwait(false);
        var rootPrefix = _provider.RootLabel + "/";

        return labelMap.Values
            .Where(label => !string.IsNullOrWhiteSpace(label.Name))
            .Where(label =>
                string.Equals(label.Name, "INBOX", StringComparison.OrdinalIgnoreCase)
                || label.Name!.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static label => label.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static label => new GmailLabelInfo(
                label.Id ?? string.Empty,
                label.Name ?? string.Empty,
                label.Color?.BackgroundColor,
                label.Color?.TextColor))
            .ToList();
    }

    private async Task<IReadOnlyList<EmailSummary>> GetSummariesForLabelIdsAsync(
        GmailService gmail,
        IReadOnlyCollection<string> labelIds,
        string logLabel,
        CancellationToken cancellationToken)
    {
        var summaries = new List<EmailSummary>();
        var seenMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var labelMap = await LoadLabelMapAsync(gmail, cancellationToken).ConfigureAwait(false);

        foreach (var labelId in labelIds)
        {
            string? pageToken = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var listRequest = gmail.Users.Messages.List("me");
                listRequest.LabelIds = new[] { labelId };
                listRequest.MaxResults = InternalDrainPageSize;
                listRequest.PageToken = pageToken;

                ListMessagesResponse listResponse;
                try
                {
                    listResponse = await GmailRetry.ExecuteAsync(
                        ct => listRequest.ExecuteAsync(ct),
                        _logger,
                        $"Messages.List(label '{logLabel}')",
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Gmail] Messages.List failed for label '{logLabel}': {ex.Message}", ex);
                    break;
                }

                if (listResponse.Messages == null || listResponse.Messages.Count == 0)
                {
                    break;
                }

                var newMessages = listResponse.Messages
                    .Where(message => !string.IsNullOrWhiteSpace(message.Id) && seenMessageIds.Add(message.Id!))
                    .ToList();
                if (newMessages.Count == 0)
                {
                    pageToken = listResponse.NextPageToken;
                    continue;
                }

                var pageSummaries = await FetchSummariesParallelAsync(
                    gmail,
                    newMessages,
                    labelMap,
                    cancellationToken).ConfigureAwait(false);
                summaries.AddRange(pageSummaries);

                pageToken = listResponse.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));
        }

        return summaries
            .OrderByDescending(e => e.ReceivedAt)
            .ToList();
    }

    public async Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return null;
        }

        return await TryGetSummaryAsync(gmail, messageId, labelMap: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail == null)
        {
            return null;
        }

        try
        {
            var message = await GmailRetry.ExecuteAsync(
                ct =>
                {
                    var getRequest = gmail.Users.Messages.Get("me", messageId);
                    getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                    return getRequest.ExecuteAsync(ct);
                },
                _logger,
                $"Messages.Get(full '{messageId}')",
                cancellationToken).ConfigureAwait(false);

            var details = MapDetails(message);
            var inlineImages = await ResolveInlineImagesAsync(
                gmail, messageId, message.Payload, details.HtmlBody, cancellationToken).ConfigureAwait(false);
            return inlineImages.Count == 0 ? details : details with { InlineImages = inlineImages };
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Gmail] Messages.Get(full) failed for id '{messageId}': {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> ResolveLabelIdAsync(
        GmailService gmail,
        string labelPath,
        IAppLogger logger,
        CancellationToken cancellationToken)
    {
        var labels = await GmailRetry.ExecuteAsync(
            ct => gmail.Users.Labels.List("me").ExecuteAsync(ct),
            logger,
            "Labels.List(resolve label id)",
            cancellationToken).ConfigureAwait(false);
        var match = labels.Labels?.FirstOrDefault(
            l => string.Equals(l.Name, labelPath, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    private async Task<EmailSummary?> TryGetSummaryAsync(
        GmailService gmail,
        string messageId,
        IReadOnlyDictionary<string, Label>? labelMap,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await GmailRetry.ExecuteAsync(
                ct =>
                {
                    var getRequest = gmail.Users.Messages.Get("me", messageId);
                    getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    getRequest.MetadataHeaders = MetadataHeaders;
                    getRequest.Fields = MetadataSummaryFieldsMask;
                    return getRequest.ExecuteAsync(ct);
                },
                _logger,
                $"Messages.Get(metadata '{messageId}')",
                cancellationToken).ConfigureAwait(false);
            if (labelMap is null)
            {
                labelMap = await LoadLabelMapAsync(gmail, cancellationToken).ConfigureAwait(false);
            }

            return Map(message, labelMap);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Gmail] Messages.Get failed for id '{messageId}': {ex.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<EmailSummary>> FetchSummariesParallelAsync(
        GmailService gmail,
        IList<Message> messages,
        IReadOnlyDictionary<string, Label> labelMap,
        CancellationToken cancellationToken)
    {
        var messageIds = messages
            .Select(static message => message.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .ToList();

        if (messageIds.Count == 0)
        {
            return Array.Empty<EmailSummary>();
        }

        using var concurrencyGate = new SemaphoreSlim(SummaryFetchConcurrency, SummaryFetchConcurrency);
        var fetchTasks = messageIds.Select(async messageId =>
        {
            await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await TryGetSummaryAsync(gmail, messageId, labelMap, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                concurrencyGate.Release();
            }
        });

        var results = await Task.WhenAll(fetchTasks).ConfigureAwait(false);
        return results
            .Where(static summary => summary is not null)
            .Select(static summary => summary!)
            .ToList();
    }

    internal static string BuildMailboxQueryString(EmailMailboxQuery query, string? inboxQueryOverride = null) =>
        EmailMailboxQueryComposer.BuildSearchQuery(query, inboxQueryOverride);

    internal static string ResolveScopeBaseQuery(EmailMailboxQuery query, string? inboxQueryOverride) =>
        EmailMailboxQueryComposer.ResolveScopeBaseQuery(query, inboxQueryOverride);

    internal static string BuildUnreadCountQuery(EmailMailboxQuery query, string? inboxQueryOverride) =>
        EmailMailboxQueryComposer.BuildUnreadCountQuery(query, inboxQueryOverride);

    internal static bool HasNonScopeListFilters(EmailMailboxQuery query) =>
        EmailMailboxQueryComposer.HasNonScopeListFilters(query);

    internal static string DescribeMailboxScope(EmailMailboxQuery query) =>
        EmailMailboxQueryComposer.DescribeMailboxScope(query);

    private async Task<EmailMailboxUnreadCount> GetLabelScopeUnreadCountAsync(
        GmailService gmail,
        EmailMailboxQuery query,
        string scopeDescription,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.LabelName))
        {
            return new EmailMailboxUnreadCount(0, IsExact: true, scopeDescription);
        }

        var labelMap = await LoadLabelMapAsync(gmail, cancellationToken).ConfigureAwait(false);
        var label = labelMap.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, query.LabelName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (label?.Id is not null && label.MessagesUnread is { } unreadFromLabel)
        {
            return new EmailMailboxUnreadCount((int)Math.Min(unreadFromLabel, int.MaxValue), IsExact: true, scopeDescription);
        }

        var unreadQuery = EmailMailboxQueryComposer.BuildUnreadCountQuery(query, _provider.DefaultMailboxQuery);
        var count = await CountMessagesByQueryAsync(gmail, unreadQuery, _logger, cancellationToken).ConfigureAwait(false);
        return new EmailMailboxUnreadCount(count, IsExact: true, scopeDescription);
    }

    private static async Task<int> CountMessagesByQueryAsync(
        GmailService gmail,
        string gmailQuery,
        IAppLogger logger,
        CancellationToken cancellationToken)
    {
        var totalCount = 0;
        string? pageToken = null;

        for (var page = 0; page < UnreadCountMaxPages; page++)
        {
            var token = pageToken;
            var listRequest = gmail.Users.Messages.List("me");
            listRequest.Q = gmailQuery;
            listRequest.MaxResults = UnreadCountPageSize;
            listRequest.PageToken = token;

            ListMessagesResponse listResponse;
            try
            {
                listResponse = await GmailRetry.ExecuteAsync(
                    ct => listRequest.ExecuteAsync(ct),
                    logger,
                    "Messages.List(unread count)",
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            totalCount += listResponse.Messages?.Count ?? 0;
            pageToken = listResponse.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                break;
            }
        }

        return totalCount;
    }

    private static string QuoteGmailTerm(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    internal static bool ResolveIsUnread(IList<string>? labelIds) =>
        labelIds is { Count: > 0 }
        && labelIds.Contains("UNREAD", StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<string, Label>> LoadLabelMapAsync(
        GmailService gmail,
        CancellationToken cancellationToken)
    {
        if (_cachedLabelMap is not null && ReferenceEquals(_cachedLabelMapService, gmail))
        {
            return _cachedLabelMap;
        }

        var labels = await GmailRetry.ExecuteAsync(
            ct => gmail.Users.Labels.List("me").ExecuteAsync(ct),
            _logger,
            "Labels.List(label map)",
            cancellationToken).ConfigureAwait(false);
        var labelMap = labels.Labels?
                   .Where(static label => !string.IsNullOrWhiteSpace(label.Id))
                   .ToDictionary(static label => label.Id!, static label => label, StringComparer.Ordinal)
               ?? new Dictionary<string, Label>(StringComparer.Ordinal);

        _cachedLabelMap = labelMap;
        _cachedLabelMapService = gmail;
        return labelMap;
    }

    private IReadOnlyList<string> ResolveProjectLabelIds(
        IReadOnlyDictionary<string, Label> labelMap,
        string projectLabelName)
    {
        var rootPrefix = _provider.RootLabel + "/";
        return labelMap.Values
            .Where(static label => !string.IsNullOrWhiteSpace(label.Name) && !string.IsNullOrWhiteSpace(label.Id))
            .Where(label => label.Name!.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(label =>
            {
                var parts = label.Name!.Split('/');
                return parts.Length >= 2
                    && string.Equals(parts[^1], projectLabelName, StringComparison.OrdinalIgnoreCase);
            })
            .Select(static label => label.Id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private EmailSummary Map(Message message, IReadOnlyDictionary<string, Label>? labelMap)
    {
        var headers = message.Payload?.Headers;

        var fromRaw = GetHeader(headers, "From");
        if (!EmailAddress.TryParse(fromRaw, out var from))
        {
            _logger.Warn($"[Gmail] Unparsable From header on message '{message.Id}': '{fromRaw}'");
            from = EmailAddress.CreateOrFallback(fromRaw);
        }

        var toRaw = GetHeader(headers, "To");
        EmailAddress? to = null;
        if (!string.IsNullOrWhiteSpace(toRaw))
        {
            if (!EmailAddress.TryParse(toRaw, out var parsedTo))
            {
                to = EmailAddress.CreateOrFallback(toRaw);
            }
            else
            {
                to = parsedTo;
            }
        }

        var subject = GetHeader(headers, "Subject") ?? string.Empty;
        var receivedAt = ResolveReceivedAt(message, GetHeader(headers, "Date"));
        var attachmentCount = CountAttachments(message.Payload);
        var labelNames = ResolveLabelNames(message, labelMap);
        var labelChips = ResolveLabelChips(message, labelMap);
        var primaryLabel = ResolvePrimaryLabel(labelNames);
        var isUnread = GmailEmailGateway.ResolveIsUnread(message.LabelIds);

        return new EmailSummary(
            message.Id ?? string.Empty,
            message.ThreadId ?? string.Empty,
            from,
            subject,
            receivedAt,
            attachmentCount,
            GetHeader(headers, "Message-ID"),
            to,
            message.Snippet ?? string.Empty,
            labelNames,
            labelChips,
            primaryLabel,
            isUnread);
    }

    internal EmailSummary MapForTests(Message message) => Map(message, labelMap: null);

    internal static IReadOnlyList<EmailLabelChip> ResolveLabelChips(
        Message message,
        IReadOnlyDictionary<string, Label>? labelMap)
    {
        if (message.LabelIds is null || message.LabelIds.Count == 0 || labelMap is null)
        {
            return [];
        }

        var chips = new List<EmailLabelChip>();
        foreach (var labelId in message.LabelIds)
        {
            if (!labelMap.TryGetValue(labelId, out var label) || string.IsNullOrWhiteSpace(label.Name))
            {
                continue;
            }

            chips.Add(new EmailLabelChip(
                label.Name,
                label.Color?.BackgroundColor,
                label.Color?.TextColor,
                label.Color?.BackgroundColor));
        }

        return chips
            .DistinctBy(static chip => chip.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static chip => chip.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ResolveLabelNames(
        Message message,
        IReadOnlyDictionary<string, Label>? labelMap)
    {
        if (message.LabelIds is null || message.LabelIds.Count == 0 || labelMap is null)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var labelId in message.LabelIds)
        {
            if (labelMap.TryGetValue(labelId, out var label) && !string.IsNullOrWhiteSpace(label.Name))
            {
                names.Add(label.Name);
            }
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string? ResolvePrimaryLabel(IReadOnlyList<string> labelNames)
    {
        if (labelNames.Count == 0)
        {
            return null;
        }

        var rootPrefix = _provider.RootLabel + "/";
        var projectLabel = labelNames.FirstOrDefault(
            label => label.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(projectLabel))
        {
            return projectLabel;
        }

        return labelNames.FirstOrDefault(
            static label => !string.Equals(label, "INBOX", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "UNREAD", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "CATEGORY_PERSONAL", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "CATEGORY_UPDATES", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "CATEGORY_PROMOTIONS", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "CATEGORY_SOCIAL", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "CATEGORY_FORUMS", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "SENT", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "DRAFT", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "SPAM", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "TRASH", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "STARRED", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "IMPORTANT", StringComparison.OrdinalIgnoreCase));
    }

    private EmailSummary Map(Message message)
        => Map(message, labelMap: null);

    private EmailMessageDetails MapDetails(Message message)
    {
        var summary = Map(message);
        var (bodyText, htmlBody) = ExtractBodies(message.Payload);
        var attachments = ExtractAttachmentDetails(message.Payload);

        return new EmailMessageDetails(
            summary.MessageId,
            summary.ThreadId,
            summary.From,
            summary.Subject,
            summary.ReceivedAt,
            bodyText,
            attachments,
            htmlBody);
    }

    private static string? GetHeader(IList<MessagePartHeader>? headers, string name)
        => headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static DateTimeOffset ResolveReceivedAt(Message message, string? dateHeader)
    {
        // Prefer the server-side internal date (epoch millis) when present; fall back to the
        // RFC 2822 Date header.
        if (message.InternalDate is long millis && millis > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis);
        }

        if (!string.IsNullOrWhiteSpace(dateHeader) &&
            DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.MinValue;
    }

    internal static int CountAttachments(MessagePart? payload)
    {
        if (payload == null)
        {
            return 0;
        }

        var count = 0;
        CountAttachmentsRecursive(payload, ref count);
        return count;
    }

    private static void CountAttachmentsRecursive(MessagePart part, ref int count)
    {
        var filename = ResolveFileName(part);
        if (!string.IsNullOrWhiteSpace(filename) && part.Body?.AttachmentId is { Length: > 0 })
        {
            if (!IsInlineAttachment(part))
            {
                count++;
            }
        }

        if (part.Parts == null)
        {
            return;
        }

        foreach (var nested in part.Parts)
        {
            CountAttachmentsRecursive(nested, ref count);
        }
    }

    private static (string BodyText, string? HtmlBody) ExtractBodies(MessagePart? payload)
    {
        if (payload == null)
        {
            return (string.Empty, null);
        }

        string? plainBody = null;
        string? htmlBody = null;
        ExtractBodiesRecursive(payload, ref plainBody, ref htmlBody);

        var bodyText = !string.IsNullOrWhiteSpace(plainBody)
            ? plainBody.Trim()
            : !string.IsNullOrWhiteSpace(htmlBody)
                ? StripHtml(htmlBody)
                : string.Empty;

        return (bodyText, string.IsNullOrWhiteSpace(htmlBody) ? null : htmlBody);
    }

    private static string ExtractBodyText(MessagePart? payload) => ExtractBodies(payload).BodyText;

    private static void ExtractBodiesRecursive(MessagePart part, ref string? plainBody, ref string? htmlBody)
    {
        var mimeType = part.MimeType?.ToLowerInvariant() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(part.Body?.Data))
        {
            if (mimeType == "text/plain" && string.IsNullOrWhiteSpace(plainBody))
            {
                plainBody = DecodeBase64UrlSafe(part.Body.Data);
            }
            else if (mimeType == "text/html" && string.IsNullOrWhiteSpace(htmlBody))
            {
                htmlBody = DecodeBase64UrlSafe(part.Body.Data);
            }
        }

        if (part.Parts == null)
        {
            return;
        }

        foreach (var nested in part.Parts)
        {
            ExtractBodiesRecursive(nested, ref plainBody, ref htmlBody);
        }
    }

    private static IReadOnlyList<EmailMessageAttachmentDetails> ExtractAttachmentDetails(MessagePart? payload)
    {
        if (payload == null)
        {
            return [];
        }

        var attachments = new List<EmailMessageAttachmentDetails>();
        CollectAttachmentsRecursive(payload, attachments);
        return attachments;
    }

    private static void CollectAttachmentsRecursive(
        MessagePart part,
        ICollection<EmailMessageAttachmentDetails> attachments)
    {
        var filename = ResolveFileName(part);
        if (!string.IsNullOrWhiteSpace(filename) && part.Body?.AttachmentId is { Length: > 0 } attachmentId)
        {
            if (!IsInlineAttachment(part))
            {
                attachments.Add(new EmailMessageAttachmentDetails(
                    attachmentId,
                    filename,
                    string.IsNullOrWhiteSpace(part.MimeType) ? "application/octet-stream" : part.MimeType!,
                    part.Body.Size));
            }
        }

        if (part.Parts == null)
        {
            return;
        }

        foreach (var nested in part.Parts)
        {
            CollectAttachmentsRecursive(nested, attachments);
        }
    }

    private static string ResolveFileName(MessagePart part)
    {
        if (!string.IsNullOrWhiteSpace(part.Filename))
        {
            return part.Filename;
        }

        var disposition = GetHeader(part.Headers, "Content-Disposition");
        if (string.IsNullOrWhiteSpace(disposition))
        {
            return string.Empty;
        }

        var match = Regex.Match(disposition, "filename=\"?([^\"]+)\"?", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    // Matches src="cid:CONTENT-ID" (single/double quotes) in the HTML body.
    private static readonly Regex InlineCidRegex = new(
        "src\\s*=\\s*[\"']cid:(?<cid>[^\"']+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Fetches bytes for inline images (<c>Content-ID</c> + <c>image/*</c>) that are actually
    /// referenced from the HTML body via <c>cid:</c>. Returns an empty list when there is no HTML,
    /// no referenced cids, or the fetch fails — the body still renders (images just stay broken).
    /// </summary>
    private async Task<IReadOnlyList<EmailInlineImage>> ResolveInlineImagesAsync(
        GmailService gmail,
        string messageId,
        MessagePart? payload,
        string? htmlBody,
        CancellationToken cancellationToken)
    {
        if (payload is null || string.IsNullOrWhiteSpace(htmlBody))
        {
            // #region agent log
            AgentDebugNdjson.Write("A", "GmailEmailGateway.ResolveInlineImagesAsync", "early-exit empty payload/html",
                new { messageId, hasPayload = payload is not null, htmlLen = htmlBody?.Length ?? 0 });
            // #endregion
            return [];
        }

        var rawCidHitCount = Regex.Matches(htmlBody, "cid:", RegexOptions.IgnoreCase).Count;
        var referencedCids = InlineCidRegex.Matches(htmlBody)
            .Select(m => NormalizeContentId(m.Groups["cid"].Value))
            .Where(cid => !string.IsNullOrWhiteSpace(cid))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // #region agent log
        AgentDebugNdjson.Write("A", "GmailEmailGateway.ResolveInlineImagesAsync", "cid-scan",
            new
            {
                messageId,
                htmlLen = htmlBody.Length,
                rawCidHitCount,
                regexCidCount = referencedCids.Count,
                sampleCids = referencedCids.Take(5).ToArray(),
                htmlHasImg = htmlBody.Contains("<img", StringComparison.OrdinalIgnoreCase),
            });
        // #endregion

        if (referencedCids.Count == 0)
        {
            return [];
        }

        var inlineParts = new List<(string ContentId, string AttachmentId, string MimeType, bool HasInlineData)>();
        CollectInlineImagePartsRecursive(payload, inlineParts);

        // #region agent log
        AgentDebugNdjson.Write("B", "GmailEmailGateway.ResolveInlineImagesAsync", "inline-parts",
            new
            {
                messageId,
                partCount = inlineParts.Count,
                parts = inlineParts.Take(8).Select(p => new { p.ContentId, p.MimeType, attLen = p.AttachmentId.Length, p.HasInlineData }).ToArray(),
                unmatchedCids = referencedCids.Except(inlineParts.Select(p => p.ContentId), StringComparer.OrdinalIgnoreCase).Take(5).ToArray(),
            });
        // #endregion

        var results = new List<EmailInlineImage>();
        foreach (var (contentId, attachmentId, mimeType, hasInlineData) in inlineParts)
        {
            if (!referencedCids.Contains(contentId) || results.Any(r =>
                    string.Equals(r.ContentId, contentId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                var attachment = await GmailRetry.ExecuteAsync(
                    ct => gmail.Users.Messages.Attachments.Get("me", messageId, attachmentId).ExecuteAsync(ct),
                    _logger,
                    $"Messages.Attachments.Get('{messageId}','{attachmentId}')",
                    cancellationToken).ConfigureAwait(false);

                var bytes = DecodeBase64UrlSafeToBytes(attachment?.Data);

                // #region agent log
                AgentDebugNdjson.Write("C", "GmailEmailGateway.ResolveInlineImagesAsync", "fetch-one",
                    new { messageId, contentId, mimeType, hasAttachmentId = true, hasInlineData, byteLen = bytes.Length });
                // #endregion

                if (bytes.Length > 0)
                {
                    results.Add(new EmailInlineImage(contentId, mimeType, bytes));
                }
            }
            catch (Exception ex)
            {
                // #region agent log
                AgentDebugNdjson.Write("C", "GmailEmailGateway.ResolveInlineImagesAsync", "fetch-failed",
                    new { messageId, contentId, err = ex.GetType().Name, ex.Message });
                // #endregion
                _logger.Warn($"[Gmail] Inline image fetch failed for cid '{contentId}' on '{messageId}': {ex.Message}");
            }
        }

        // #region agent log
        AgentDebugNdjson.Write("C", "GmailEmailGateway.ResolveInlineImagesAsync", "done",
            new { messageId, resultCount = results.Count, totalBytes = results.Sum(r => r.Data.Length) });
        // #endregion

        return results;
    }

    private static void CollectInlineImagePartsRecursive(
        MessagePart part,
        ICollection<(string ContentId, string AttachmentId, string MimeType, bool HasInlineData)> inlineParts)
    {
        var contentId = NormalizeContentId(GetHeader(part.Headers, "Content-ID"));
        var mimeType = part.MimeType ?? string.Empty;
        var hasInlineData = !string.IsNullOrWhiteSpace(part.Body?.Data);
        if (!string.IsNullOrWhiteSpace(contentId)
            && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && part.Body?.AttachmentId is { Length: > 0 } attachmentId)
        {
            inlineParts.Add((contentId, attachmentId, mimeType, hasInlineData));
        }

        if (part.Parts == null)
        {
            return;
        }

        foreach (var nested in part.Parts)
        {
            CollectInlineImagePartsRecursive(nested, inlineParts);
        }
    }

    private static string NormalizeContentId(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('<', '>').Trim();

    private static byte[] DecodeBase64UrlSafeToBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var padding = base64.Length % 4;
            if (padding > 0)
            {
                base64 += new string('=', 4 - padding);
            }

            return Convert.FromBase64String(base64);
        }
        catch
        {
            return [];
        }
    }

    private static bool IsInlineAttachment(MessagePart part)
    {
        var disposition = GetHeader(part.Headers, "Content-Disposition") ?? string.Empty;
        var contentId = GetHeader(part.Headers, "Content-ID");
        var mimeType = part.MimeType ?? string.Empty;

        if (disposition.Contains("inline", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(contentId)
            && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeBase64UrlSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var padding = base64.Length % 4;
            if (padding > 0)
            {
                base64 += new string('=', 4 - padding);
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var normalized = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase);

        normalized = Regex.Replace(normalized, "<[^>]+>", " ");
        normalized = WebUtility.HtmlDecode(normalized);
        normalized = Regex.Replace(normalized, @"[ \t]+\n", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
        return normalized.Trim();
    }
}
