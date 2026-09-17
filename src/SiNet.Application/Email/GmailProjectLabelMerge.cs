namespace SiNet.Application.Email;

public sealed record GmailProjectLabelMergeResult(
    bool Succeeded,
    bool SourceDeleted,
    int TransferredCount,
    int AlreadyHadTargetCount,
    int FailedCount,
    string? ErrorMessage = null,
    bool IsPartial = false)
{
    public static GmailProjectLabelMergeResult Rejected(string errorMessage)
        => new(false, false, 0, 0, 0, errorMessage, IsPartial: false);
}

public interface IGmailProjectLabelMergeOperations
{
    Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
        string labelId,
        CancellationToken cancellationToken);

    Task AttachProjectLabelAsync(
        string gmailMessageId,
        string projectLabelId,
        CancellationToken cancellationToken);

    Task DeleteLabelAsync(string labelId, CancellationToken cancellationToken);
}

/// <summary>
/// Attach target → verify every source message is on target → delete source.
/// Never deletes the source on partial failure.
/// </summary>
public static class GmailProjectLabelMerge
{
    public static string? Validate(ProjectLabelEntry? source, ProjectLabelEntry? target)
    {
        if (source is null || target is null)
        {
            return "שתי התוויות חייבות להיות תוויות פרויקט קיימות.";
        }

        if (string.Equals(source.LabelId, target.LabelId, StringComparison.Ordinal))
        {
            return "לא ניתן למזג תווית אל עצמה.";
        }

        if (source.ProjectNumber != target.ProjectNumber)
        {
            return $"לא ניתן למזג תוויות של פרויקטים שונים ({source.ProjectNumber} ≠ {target.ProjectNumber}).";
        }

        return null;
    }

    public static async Task<GmailProjectLabelMergeResult> ExecuteAsync(
        IGmailProjectLabelMergeOperations operations,
        ProjectLabelEntry source,
        ProjectLabelEntry target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var validation = Validate(source, target);
        if (validation is not null)
        {
            return GmailProjectLabelMergeResult.Rejected(validation);
        }

        var sourceIds = await operations
            .ListMessageIdsByLabelAsync(source.LabelId, cancellationToken)
            .ConfigureAwait(false);
        var targetIds = await operations
            .ListMessageIdsByLabelAsync(target.LabelId, cancellationToken)
            .ConfigureAwait(false);
        var targetSet = new HashSet<string>(targetIds, StringComparer.Ordinal);

        var alreadyHad = 0;
        var transferred = 0;
        var failed = 0;

        foreach (var messageId in sourceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (targetSet.Contains(messageId))
            {
                alreadyHad++;
                continue;
            }

            try
            {
                await operations
                    .AttachProjectLabelAsync(messageId, target.LabelId, cancellationToken)
                    .ConfigureAwait(false);
                targetSet.Add(messageId);
                transferred++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }
        }

        if (failed > 0)
        {
            return new GmailProjectLabelMergeResult(
                Succeeded: false,
                SourceDeleted: false,
                transferred,
                alreadyHad,
                failed,
                $"מיזוג חלקי: {failed} הודעות לא הועברו. תווית המקור לא נמחקה.",
                IsPartial: true);
        }

        var verifySource = await operations
            .ListMessageIdsByLabelAsync(source.LabelId, cancellationToken)
            .ConfigureAwait(false);
        var verifyTarget = await operations
            .ListMessageIdsByLabelAsync(target.LabelId, cancellationToken)
            .ConfigureAwait(false);
        var verifiedTarget = new HashSet<string>(verifyTarget, StringComparer.Ordinal);
        var unverified = verifySource
            .Concat(sourceIds)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !verifiedTarget.Contains(id))
            .ToArray();

        if (unverified.Length > 0)
        {
            return new GmailProjectLabelMergeResult(
                Succeeded: false,
                SourceDeleted: false,
                transferred,
                alreadyHad,
                unverified.Length,
                $"אימות נכשל: {unverified.Length} הודעות עדיין לא על תווית היעד. תווית המקור לא נמחקה.",
                IsPartial: true);
        }

        await operations
            .DeleteLabelAsync(source.LabelId, cancellationToken)
            .ConfigureAwait(false);

        return new GmailProjectLabelMergeResult(
            Succeeded: true,
            SourceDeleted: true,
            transferred,
            alreadyHad,
            FailedCount: 0);
    }
}
