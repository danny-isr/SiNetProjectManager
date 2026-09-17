using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email;

namespace SiNet.Infrastructure.Google;

/// <summary>Native Gmail label modify implementation for project filing and triage labels.</summary>
public sealed class GmailEmailModifyService : IEmailGmailModifyService
{
    internal const string PendingLabelName = "OfficeSystem_Pending";
    internal const string PersonalLabelName = "OfficeSystem_Personal";
    internal const string IrrelevantLabelName = "OfficeSystem_Irrelevant";
    internal const string FyiLabelName = "OfficeSystem_Fyi";

    private readonly GmailClientProvider _provider;
    private readonly IAppLogger _logger;
    private readonly IGmailLabelCatalog _catalog;

    public GmailEmailModifyService(GmailClientProvider provider, IAppLogger logger)
        : this(provider, logger, catalog: null)
    {
    }

    internal GmailEmailModifyService(
        GmailClientProvider provider,
        IAppLogger logger,
        IGmailLabelCatalog? catalog)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _catalog = catalog ?? new GmailLabelCatalog(new GmailApiLabelDirectory(provider, logger), logger);
    }

    public string RootLabel => _provider.RootLabel;

    public Task<string> GetOrCreateProjectLabelAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default)
    {
        var number = EmailProjectLabelParser.TryExtractProjectIdFromDisplaySegment(projectDisplayName)
            ?? throw new ArgumentException(
                "Project display name must start with (ProjectNumber).",
                nameof(projectDisplayName));
        return GetOrCreateProjectLabelAsync(location, projectDisplayName, number, cancellationToken);
    }

    public async Task<string> GetOrCreateProjectLabelAsync(
        string location,
        string projectDisplayName,
        int projectNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDisplayName);
        if (projectNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectNumber), projectNumber, "ProjectNumber must be positive.");
        }

        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var map = await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
        var decision = ResolveProjectLabel(projectNumber);
        if (decision.Kind == GmailProjectLabelResolveKind.ReuseExisting)
        {
            return decision.Existing!.LabelId;
        }

        if (decision.Kind == GmailProjectLabelResolveKind.DuplicateConflict)
        {
            throw new GmailDuplicateProjectLabelException(projectNumber, decision.Matches);
        }

        var fullPath = EmailProjectLabelParser.BuildCanonicalPath(_provider.RootLabel, location, projectDisplayName);
        var existingPath = FindByName(map, fullPath);
        if (!string.IsNullOrWhiteSpace(existingPath?.Id))
        {
            _catalog.NotifyCreated(existingPath.Id, existingPath.Name);
            return existingPath.Id;
        }

        await EnsureParentFromCatalogAsync(gmail, _provider.RootLabel, cancellationToken).ConfigureAwait(false);
        await EnsureParentFromCatalogAsync(gmail, $"{_provider.RootLabel}/{location.Trim()}", cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var created = await gmail.Users.Labels.Create(new Label
            {
                Name = fullPath,
                LabelListVisibility = "labelShow",
                MessageListVisibility = "show",
            }, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

            var createdId = created.Id ?? throw new InvalidOperationException($"Failed to create Gmail label '{fullPath}'.");
            _catalog.NotifyCreated(createdId, created.Name ?? fullPath);
            return createdId;
        }
        catch (Exception ex) when (GmailLabelIdempotency.IsLabelExistsOrConflicts(ex))
        {
            _catalog.Invalidate("create-conflict");
            map = await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
            var retry = ResolveProjectLabel(projectNumber);
            if (retry.Kind == GmailProjectLabelResolveKind.ReuseExisting)
            {
                return retry.Existing!.LabelId;
            }

            if (retry.Kind == GmailProjectLabelResolveKind.DuplicateConflict)
            {
                throw new GmailDuplicateProjectLabelException(projectNumber, retry.Matches);
            }

            var resolved = FindByName(map, fullPath);
            if (!string.IsNullOrWhiteSpace(resolved?.Id))
            {
                _catalog.NotifyCreated(resolved.Id, resolved.Name);
                return resolved.Id;
            }

            throw new InvalidOperationException($"Gmail label '{fullPath}' conflicted but could not be resolved.");
        }
    }

    public async Task EnsureProjectLocationAsync(
        string location,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
        await EnsureParentFromCatalogAsync(gmail, _provider.RootLabel, cancellationToken).ConfigureAwait(false);
        await EnsureParentFromCatalogAsync(gmail, $"{_provider.RootLabel}/{location.Trim()}", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GmailProjectLabelMergeResult> MergeProjectLabelsAsync(
        string sourceLabelId,
        string targetLabelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLabelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLabelId);

        var map = await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
        var index = _catalog.GetProjectLabelIndex(RootLabel);
        var source = ResolveProjectEntry(map, index, sourceLabelId.Trim());
        var target = ResolveProjectEntry(map, index, targetLabelId.Trim());
        var validation = GmailProjectLabelMerge.Validate(source, target);
        if (validation is not null)
        {
            return GmailProjectLabelMergeResult.Rejected(validation);
        }

        return await GmailProjectLabelMerge
            .ExecuteAsync(new ModifyMergeOperations(this), source!, target!, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetProjectLabelIdAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default)
    {
        await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
        var number = EmailProjectLabelParser.TryExtractProjectIdFromDisplaySegment(projectDisplayName);
        if (number is int projectNumber && projectNumber > 0)
        {
            var decision = ResolveProjectLabel(projectNumber);
            if (decision.Kind == GmailProjectLabelResolveKind.ReuseExisting)
            {
                return decision.Existing!.LabelId;
            }

            if (decision.Kind == GmailProjectLabelResolveKind.DuplicateConflict)
            {
                return null;
            }
        }

        var fullPath = EmailProjectLabelParser.BuildCanonicalPath(_provider.RootLabel, location, projectDisplayName);
        return FindByName(await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false), fullPath)?.Id;
    }

    public async Task<string?> GetProjectLabelIdByFullPathAsync(
        string fullPath,
        CancellationToken cancellationToken = default)
    {
        var map = await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
        return FindByName(map, fullPath)?.Id;
    }

    public async Task<IReadOnlyList<string>> GetProjectLabelIdsOnMessageAsync(
        string gmailMessageId,
        CancellationToken cancellationToken = default)
    {
        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var message = await gmail.Users.Messages.Get("me", gmailMessageId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (message.LabelIds is not { Count: > 0 })
        {
            return [];
        }

        var catalogMap = await _catalog
            .ResolveForMessageAsync(gmailMessageId, message.LabelIds, cancellationToken)
            .ConfigureAwait(false);

        return message.LabelIds
            .Where(id => catalogMap.TryGetValue(id, out var record)
                && EmailGmailLabelNames.IsProjectLabel(record.Name, RootLabel))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public Task RemoveProjectLabelsFromMessageAsync(
        string gmailMessageId,
        IReadOnlyList<string> labelIdsToRemove,
        bool moveToInbox = false,
        CancellationToken cancellationToken = default)
    {
        if (labelIdsToRemove.Count == 0)
        {
            return Task.CompletedTask;
        }

        return ModifyMessageLabelsAsync(
            gmailMessageId,
            addLabelIds: moveToInbox ? ["INBOX"] : [],
            removeLabelIds: labelIdsToRemove,
            cancellationToken);
    }

    public async Task AttachProjectLabelAsync(
        string gmailMessageId,
        string projectLabelId,
        CancellationToken cancellationToken = default)
    {
        await ModifyMessageLabelsAsync(
            gmailMessageId,
            addLabelIds: [projectLabelId],
            removeLabelIds: [],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveProjectLabelAsync(
        string gmailMessageId,
        string projectLabelId,
        bool moveToInbox = true,
        CancellationToken cancellationToken = default)
    {
        await ModifyMessageLabelsAsync(
            gmailMessageId,
            addLabelIds: moveToInbox ? ["INBOX"] : [],
            removeLabelIds: [projectLabelId],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyTriageStatusLabelAsync(
        string gmailMessageId,
        EmailTriageStatus status,
        CancellationToken cancellationToken = default)
    {
        var labelId = await GetOrCreateStatusLabelAsync(status, cancellationToken).ConfigureAwait(false);
        // DEV-016: Personal / Irrelevant / Fyi finish handling — also clear UNREAD in the same modify.
        var removeUnread = status is EmailTriageStatus.Personal
            or EmailTriageStatus.Irrelevant
            or EmailTriageStatus.Fyi;
        await ModifyMessageLabelsAsync(
            gmailMessageId,
            addLabelIds: [labelId],
            removeLabelIds: removeUnread ? ["UNREAD"] : [],
            cancellationToken).ConfigureAwait(false);
    }

    public Task MarkAsReadAsync(
        string gmailMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gmailMessageId);

        return ModifyMessageLabelsAsync(
            gmailMessageId,
            addLabelIds: [],
            removeLabelIds: ["UNREAD"],
            cancellationToken);
    }

    public async Task RenameLabelAsync(
        string labelId,
        string newFullPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFullPath);

        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var update = new Label { Id = labelId, Name = newFullPath.Trim() };
        await gmail.Users.Labels.Update(update, "me", labelId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        _catalog.NotifyRenamed(labelId, newFullPath.Trim());
    }

    public async Task DeleteLabelAsync(
        string labelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelId);

        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        await gmail.Users.Labels.Delete("me", labelId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        _catalog.NotifyDeleted(labelId);
    }

    public async Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
        string labelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelId);

        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var listRequest = gmail.Users.Messages.List("me");
            listRequest.LabelIds = new[] { labelId.Trim() };
            listRequest.MaxResults = 500;
            listRequest.PageToken = pageToken;

            var listResponse = await GmailRetry.ExecuteAsync(
                    ct => listRequest.ExecuteAsync(ct),
                    _logger,
                    $"Messages.List(labelId '{labelId}')",
                    cancellationToken)
                .ConfigureAwait(false);

            if (listResponse.Messages is { Count: > 0 })
            {
                foreach (var message in listResponse.Messages)
                {
                    if (!string.IsNullOrWhiteSpace(message.Id) && seen.Add(message.Id))
                        ids.Add(message.Id);
                }
            }

            pageToken = listResponse.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return ids;
    }

    private async Task<string> GetOrCreateStatusLabelAsync(
        EmailTriageStatus status,
        CancellationToken cancellationToken)
    {
        var labelName = status switch
        {
            EmailTriageStatus.Pending => PendingLabelName,
            EmailTriageStatus.Personal => PersonalLabelName,
            EmailTriageStatus.Irrelevant => IrrelevantLabelName,
            EmailTriageStatus.Fyi => FyiLabelName,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var existing = labels.Labels?.FirstOrDefault(
            label => string.Equals(label.Name, labelName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            _catalog.NotifyCreated(existing.Id, existing.Name ?? labelName);
            return existing.Id;
        }

        var created = await gmail.Users.Labels.Create(new Label
        {
            Name = labelName,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show",
        }, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

        var createdId = created.Id ?? throw new InvalidOperationException($"Failed to create Gmail status label '{labelName}'.");
        _catalog.NotifyCreated(createdId, created.Name ?? labelName);
        return createdId;
    }

    private async Task ModifyMessageLabelsAsync(
        string gmailMessageId,
        IReadOnlyList<string> addLabelIds,
        IReadOnlyList<string> removeLabelIds,
        CancellationToken cancellationToken)
    {
        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var request = new ModifyMessageRequest
        {
            AddLabelIds = addLabelIds.Count > 0 ? addLabelIds.ToList() : null,
            RemoveLabelIds = removeLabelIds.Count > 0 ? removeLabelIds.ToList() : null,
        };

        await gmail.Users.Messages.Modify(request, "me", gmailMessageId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureParentLabelExistsAsync(
        GmailService gmail,
        ListLabelsResponse existingLabels,
        string labelName,
        CancellationToken cancellationToken)
    {
        var existing = GmailLabelIdempotency.FindExactByName(existingLabels.Labels, labelName);
        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            return;
        }

        try
        {
            var created = await gmail.Users.Labels.Create(new Label
            {
                Name = labelName,
                LabelListVisibility = "labelShow",
                MessageListVisibility = "show",
            }, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

            existingLabels.Labels ??= [];
            existingLabels.Labels.Add(created);
            if (!string.IsNullOrWhiteSpace(created.Id))
                _catalog.NotifyCreated(created.Id, created.Name ?? labelName);
        }
        catch (Exception ex) when (GmailLabelIdempotency.IsLabelExistsOrConflicts(ex))
        {
            var relisted = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var resolved = GmailLabelIdempotency.ResolveIntendedAfterConflict(relisted.Labels, labelName);
            if (!string.IsNullOrWhiteSpace(resolved.Id))
                _catalog.NotifyCreated(resolved.Id, resolved.Name ?? labelName);
            existingLabels.Labels = relisted.Labels;
            if (existingLabels.Labels?.Any(l => l.Id == resolved.Id) != true)
            {
                existingLabels.Labels ??= [];
                existingLabels.Labels.Add(resolved);
            }
        }
    }

    private GmailProjectLabelResolveDecision ResolveProjectLabel(int projectNumber)
        => GmailProjectLabelResolver.Resolve(_catalog.GetProjectLabelIndex(RootLabel), projectNumber);

    private static ProjectLabelEntry? ResolveProjectEntry(
        IReadOnlyDictionary<string, GmailLabelRecord> map,
        GmailProjectLabelIndex index,
        string labelId)
    {
        if (map.TryGetValue(labelId, out var record))
        {
            return EmailProjectLabelParser.TryParseProjectLabel(record.Id, record.Name);
        }

        return index.FindByLabelId(labelId);
    }

    private static GmailLabelRecord? FindByName(
        IReadOnlyDictionary<string, GmailLabelRecord> map,
        string fullPath)
        => map.Values.FirstOrDefault(label =>
            string.Equals(label.Name, fullPath, StringComparison.OrdinalIgnoreCase));

    private async Task EnsureParentFromCatalogAsync(
        GmailService gmail,
        string labelName,
        CancellationToken cancellationToken)
    {
        var map = await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
        if (FindByName(map, labelName) is not null)
        {
            return;
        }

        try
        {
            var created = await gmail.Users.Labels.Create(new Label
            {
                Name = labelName,
                LabelListVisibility = "labelShow",
                MessageListVisibility = "show",
            }, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(created.Id))
                _catalog.NotifyCreated(created.Id, created.Name ?? labelName);
        }
        catch (Exception ex) when (GmailLabelIdempotency.IsLabelExistsOrConflicts(ex))
        {
            _catalog.Invalidate("parent-create-conflict");
            map = await _catalog.GetMapAsync(cancellationToken).ConfigureAwait(false);
            var resolved = FindByName(map, labelName);
            if (!string.IsNullOrWhiteSpace(resolved?.Id))
                _catalog.NotifyCreated(resolved.Id, resolved.Name);
        }
    }

    private sealed class ModifyMergeOperations(GmailEmailModifyService owner) : IGmailProjectLabelMergeOperations
    {
        public Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
            string labelId,
            CancellationToken cancellationToken)
            => owner.ListMessageIdsByLabelAsync(labelId, cancellationToken);

        public Task AttachProjectLabelAsync(
            string gmailMessageId,
            string projectLabelId,
            CancellationToken cancellationToken)
            => owner.AttachProjectLabelAsync(gmailMessageId, projectLabelId, cancellationToken);

        public Task DeleteLabelAsync(string labelId, CancellationToken cancellationToken)
            => owner.DeleteLabelAsync(labelId, cancellationToken);
    }

    private async Task<GmailService> RequireServiceAsync(CancellationToken cancellationToken)
    {
        var authenticatedBefore = _provider.IsSignedIn;
        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (_provider.IsSignedIn != authenticatedBefore)
        {
            _logger.Warn(
                $"[GmailAuth] GmailModify auth-changed before={authenticatedBefore} after={_provider.IsSignedIn} session={_provider.SessionIdentity}");
        }

        if (gmail is null)
        {
            _logger.Warn($"[GmailModify] Gmail session unavailable. session={_provider.SessionIdentity} authenticated={_provider.IsSignedIn}");
            throw new InvalidOperationException("Gmail session unavailable.");
        }

        return gmail;
    }
}
