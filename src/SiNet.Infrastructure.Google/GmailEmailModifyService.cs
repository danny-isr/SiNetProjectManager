using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

/// <summary>Native Gmail label modify implementation for project filing and triage labels.</summary>
public sealed class GmailEmailModifyService(GmailClientProvider provider, IAppLogger logger) : IEmailGmailModifyService
{
    internal const string PendingLabelName = "OfficeSystem_Pending";
    internal const string PersonalLabelName = "OfficeSystem_Personal";
    internal const string IrrelevantLabelName = "OfficeSystem_Irrelevant";

    private readonly GmailClientProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string RootLabel => _provider.RootLabel;

    public async Task<string> GetOrCreateProjectLabelAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default)
    {
        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var fullPath = $"{_provider.RootLabel}/{location}/{projectDisplayName}";
        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

        var existing = labels.Labels?.FirstOrDefault(
            label => string.Equals(label.Name, fullPath, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            return existing.Id;
        }

        await EnsureParentLabelExistsAsync(gmail, labels, _provider.RootLabel, cancellationToken).ConfigureAwait(false);
        await EnsureParentLabelExistsAsync(gmail, labels, $"{_provider.RootLabel}/{location}", cancellationToken).ConfigureAwait(false);

        var created = await gmail.Users.Labels.Create(new Label
        {
            Name = fullPath,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show",
        }, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

        return created.Id ?? throw new InvalidOperationException($"Failed to create Gmail label '{fullPath}'.");
    }

    public async Task<string?> GetProjectLabelIdAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default)
    {
        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var fullPath = $"{_provider.RootLabel}/{location}/{projectDisplayName}";
        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return labels.Labels?
            .FirstOrDefault(label => string.Equals(label.Name, fullPath, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    public async Task<string?> GetProjectLabelIdByFullPathAsync(
        string fullPath,
        CancellationToken cancellationToken = default)
    {
        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return labels.Labels?
            .FirstOrDefault(label => string.Equals(label.Name, fullPath, StringComparison.OrdinalIgnoreCase))
            ?.Id;
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

        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var labelMap = labels.Labels?
            .Where(static label => !string.IsNullOrWhiteSpace(label.Id) && !string.IsNullOrWhiteSpace(label.Name))
            .ToDictionary(static label => label.Id!, static label => label.Name!, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var rootPrefix = $"{RootLabel}/";
        return message.LabelIds
            .Where(labelMap.ContainsKey)
            .Select(id => labelMap[id])
            .Where(name => name.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                && name.Count(static ch => ch == '/') >= 2)
            .Select(name => labels.Labels!.First(label => string.Equals(label.Name, name, StringComparison.OrdinalIgnoreCase)).Id!)
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
        await ModifyMessageLabelsAsync(
            gmailMessageId,
            addLabelIds: [labelId],
            removeLabelIds: [],
            cancellationToken).ConfigureAwait(false);
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
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

        var gmail = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var existing = labels.Labels?.FirstOrDefault(
            label => string.Equals(label.Name, labelName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            return existing.Id;
        }

        var created = await gmail.Users.Labels.Create(new Label
        {
            Name = labelName,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show",
        }, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

        return created.Id ?? throw new InvalidOperationException($"Failed to create Gmail status label '{labelName}'.");
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

    private static async Task EnsureParentLabelExistsAsync(
        GmailService gmail,
        ListLabelsResponse existingLabels,
        string labelName,
        CancellationToken cancellationToken)
    {
        var existing = existingLabels.Labels?.FirstOrDefault(
            label => string.Equals(label.Name, labelName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            return;
        }

        var created = await gmail.Users.Labels.Create(new Label
        {
            Name = labelName,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show",
        }, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);

        existingLabels.Labels ??= [];
        existingLabels.Labels.Add(created);
    }

    private async Task<GmailService> RequireServiceAsync(CancellationToken cancellationToken)
    {
        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail is null)
        {
            _logger.Warn("[GmailModify] Gmail session unavailable.");
            throw new InvalidOperationException("Gmail session unavailable.");
        }

        return gmail;
    }
}
