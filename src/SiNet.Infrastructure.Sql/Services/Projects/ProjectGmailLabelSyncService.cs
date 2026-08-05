using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.Application.Settings;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Projects;

/// <summary>
/// Per-mailbox sync of Gmail project leaf labels to current <c>NameAndNumber</c>.
/// </summary>
internal sealed class ProjectGmailLabelSyncService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    ISystemSettingsQueryService settings,
    IEmailGateway emailGateway,
    IEmailGmailModifyService gmailModify,
    IConnectorAuthService? auth = null,
    IGmailLabelChangeJournal? labelJournal = null,
    ILogger<ProjectGmailLabelSyncService>? logger = null) : IProjectGmailLabelSyncService
{
    private static readonly Regex NumberLeafRegex = new(@"^\((\d+)\)", RegexOptions.Compiled);

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IEmailGateway _emailGateway =
        emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
    private readonly IEmailGmailModifyService _gmailModify =
        gmailModify ?? throw new ArgumentNullException(nameof(gmailModify));
    private readonly IConnectorAuthService? _auth = auth;
    private readonly IGmailLabelChangeJournal? _labelJournal = labelJournal;
    private readonly ILogger<ProjectGmailLabelSyncService>? _logger = logger;

    public async Task<ProjectGmailLabelSyncResult> SyncAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var system = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var enabled = system.EmailOffice.AutoSyncProjectLabelNames;
        if (!enabled && !force)
        {
            return new ProjectGmailLabelSyncResult(
                SettingEnabled: false,
                ExaminedCount: 0,
                RenamedCount: 0,
                Items: [],
                NeedsUserDecision: []);
        }

        var source = force
            ? GmailLabelJournalSource.ManualSync
            : GmailLabelJournalSource.AutoSync;

        var projectLeaves = await LoadNumberedProjectLeavesAsync(cancellationToken).ConfigureAwait(false);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var numbers = projectLeaves.Select(x => x.Number).Distinct().ToList();
        var projects = await db.Projects
            .AsNoTracking()
            .Where(p => p.Number != null && numbers.Contains((int)p.Number.Value))
            .Select(p => new { Number = (int)p.Number!.Value, p.NameAndNumber, p.Title, p.Id })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byNumber = projects
            .GroupBy(p => p.Number)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = new List<ProjectGmailLabelSyncItem>();
        var needsDecision = new List<ProjectGmailLabelSyncItem>();
        var renamed = 0;

        foreach (var group in projectLeaves.GroupBy(x => x.Number))
        {
            var number = group.Key;
            var groupList = group.ToList();
            if (groupList.Count > 1)
            {
                foreach (var dup in groupList)
                {
                    var item = new ProjectGmailLabelSyncItem(
                        dup.LabelId,
                        dup.FullPath,
                        dup.Leaf,
                        number,
                        ExpectedLeafName: null,
                        ProjectGmailLabelSyncAction.NeedsUserDecision,
                        $"נמצאו {groupList.Count} לייבלים עם מספר ({number}) — נדרשת החלטת משתמש.");
                    items.Add(item);
                    needsDecision.Add(item);
                }

                continue;
            }

            var entry = groupList[0];
            if (!byNumber.TryGetValue(number, out var matchedProjects) || matchedProjects.Count == 0)
            {
                items.Add(new ProjectGmailLabelSyncItem(
                    entry.LabelId,
                    entry.FullPath,
                    entry.Leaf,
                    number,
                    null,
                    ProjectGmailLabelSyncAction.Unchanged,
                    "לא נמצא פרויקט תואם במסד הנתונים."));
                continue;
            }

            if (matchedProjects.Count > 1)
            {
                var item = new ProjectGmailLabelSyncItem(
                    entry.LabelId,
                    entry.FullPath,
                    entry.Leaf,
                    number,
                    null,
                    ProjectGmailLabelSyncAction.NeedsUserDecision,
                    $"מספר פרויקט ({number}) ממופה ליותר משורה אחת ב-DB — נדרשת החלטת משתמש.");
                items.Add(item);
                needsDecision.Add(item);
                continue;
            }

            var project = matchedProjects[0];
            var expected = string.IsNullOrWhiteSpace(project.NameAndNumber)
                ? $"({number}){project.Title}"
                : project.NameAndNumber!;

            if (string.Equals(entry.Leaf.Trim(), expected.Trim(), StringComparison.Ordinal))
            {
                items.Add(new ProjectGmailLabelSyncItem(
                    entry.LabelId,
                    entry.FullPath,
                    entry.Leaf,
                    number,
                    expected,
                    ProjectGmailLabelSyncAction.Unchanged,
                    null));
                continue;
            }

            var parentPrefix = entry.FullPath[..^entry.Leaf.Length];
            var newFullPath = parentPrefix + expected;
            try
            {
                await _gmailModify
                    .RenameLabelAsync(entry.LabelId, newFullPath, cancellationToken)
                    .ConfigureAwait(false);
                renamed++;
                await TryAppendRenameJournalAsync(
                        entry.LabelId,
                        entry.FullPath,
                        newFullPath,
                        number,
                        source,
                        cancellationToken)
                    .ConfigureAwait(false);
                items.Add(new ProjectGmailLabelSyncItem(
                    entry.LabelId,
                    newFullPath,
                    expected,
                    number,
                    expected,
                    ProjectGmailLabelSyncAction.Renamed,
                    $"שונה: '{entry.Leaf}' → '{expected}'"));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[GmailLabelSync] Rename failed for label {LabelId}", entry.LabelId);
                items.Add(new ProjectGmailLabelSyncItem(
                    entry.LabelId,
                    entry.FullPath,
                    entry.Leaf,
                    number,
                    expected,
                    ProjectGmailLabelSyncAction.Failed,
                    ex.Message));
            }
        }

        return new ProjectGmailLabelSyncResult(
            SettingEnabled: enabled,
            ExaminedCount: items.Count,
            RenamedCount: renamed,
            Items: items,
            NeedsUserDecision: needsDecision);
    }

    public async Task<ProjectGmailLabelDuplicateResolveResult> ResolveDuplicateLeavesAsync(
        int projectNumber,
        string keepLabelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keepLabelId);
        if (projectNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectNumber));

        var projectLeaves = await LoadNumberedProjectLeavesAsync(cancellationToken).ConfigureAwait(false);
        var group = projectLeaves.Where(x => x.Number == projectNumber).ToList();
        if (group.Count < 2)
        {
            return new ProjectGmailLabelDuplicateResolveResult(
                projectNumber,
                keepLabelId.Trim(),
                DeletedCount: 0,
                Errors: ["לא נמצאו לייבלים כפולים למספר זה — אין מה למחוק."]);
        }

        if (!group.Any(x => string.Equals(x.LabelId, keepLabelId.Trim(), StringComparison.Ordinal)))
        {
            return new ProjectGmailLabelDuplicateResolveResult(
                projectNumber,
                keepLabelId.Trim(),
                DeletedCount: 0,
                Errors: ["הלייבל שנבחר לשמירה לא נמצא בקבוצת הכפילויות."]);
        }

        var mailbox = _auth?.ConnectedAccountEmail?.Trim();
        if (string.IsNullOrWhiteSpace(mailbox))
        {
            return new ProjectGmailLabelDuplicateResolveResult(
                projectNumber,
                keepLabelId.Trim(),
                DeletedCount: 0,
                Errors: ["לא ניתן למחוק לייבל כפול בלי חשבון Gmail מחובר (נדרש ליומן השינויים)."]);
        }

        if (_labelJournal is null)
        {
            return new ProjectGmailLabelDuplicateResolveResult(
                projectNumber,
                keepLabelId.Trim(),
                DeletedCount: 0,
                Errors: ["יומן שינויי לייבלים אינו זמין — מחיקה בוטלה (fail closed)."]);
        }

        var deleted = 0;
        var errors = new List<string>();
        foreach (var leaf in group)
        {
            if (string.Equals(leaf.LabelId, keepLabelId.Trim(), StringComparison.Ordinal))
                continue;

            try
            {
                await DeleteLabelWithJournalAsync(
                        mailbox,
                        leaf.LabelId,
                        leaf.FullPath,
                        projectNumber,
                        GmailLabelJournalSource.DuplicateResolve,
                        cancellationToken)
                    .ConfigureAwait(false);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "[GmailLabelSync] Delete duplicate failed for label {LabelId} number {Number}",
                    leaf.LabelId,
                    projectNumber);
                errors.Add($"{leaf.FullPath}: {ex.Message}");
            }
        }

        return new ProjectGmailLabelDuplicateResolveResult(
            projectNumber,
            keepLabelId.Trim(),
            deleted,
            errors);
    }

    private async Task DeleteLabelWithJournalAsync(
        string mailboxEmail,
        string labelId,
        string fullPath,
        int projectNumber,
        GmailLabelJournalSource source,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> messageIds;
        try
        {
            messageIds = await _gmailModify
                .ListMessageIdsByLabelAsync(labelId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"שליפת הודעות ללייבל נכשלה — המחיקה בוטלה: {ex.Message}",
                ex);
        }

        var entry = new GmailLabelJournalEntry(
            labelId,
            GmailLabelJournalAction.Deleted,
            fullPath,
            NewFullPath: null,
            projectNumber,
            DateTime.UtcNow,
            source,
            messageIds);

        try
        {
            await _labelJournal!
                .AppendAsync(mailboxEmail, entry, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"כתיבת יומן המחיקה נכשלה — המחיקה בוטלה: {ex.Message}",
                ex);
        }

        await _gmailModify.DeleteLabelAsync(labelId, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryAppendRenameJournalAsync(
        string labelId,
        string oldFullPath,
        string newFullPath,
        int projectNumber,
        GmailLabelJournalSource source,
        CancellationToken cancellationToken)
    {
        if (_labelJournal is null)
            return;

        var mailbox = _auth?.ConnectedAccountEmail?.Trim();
        if (string.IsNullOrWhiteSpace(mailbox))
        {
            _logger?.LogWarning(
                "[GmailLabelSync] Rename journal skipped — ConnectedAccountEmail unknown for label {LabelId}",
                labelId);
            return;
        }

        try
        {
            await _labelJournal
                .AppendAsync(
                    mailbox,
                    new GmailLabelJournalEntry(
                        labelId,
                        GmailLabelJournalAction.Renamed,
                        oldFullPath,
                        newFullPath,
                        projectNumber,
                        DateTime.UtcNow,
                        source,
                        MessageIds: []),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "[GmailLabelSync] Rename journal append failed for label {LabelId}",
                labelId);
        }
    }

    private async Task<IReadOnlyList<NumberedProjectLeaf>> LoadNumberedProjectLeavesAsync(
        CancellationToken cancellationToken)
    {
        var labels = await _emailGateway.GetMailboxLabelsAsync(cancellationToken).ConfigureAwait(false);
        var root = _gmailModify.RootLabel;
        var result = new List<NumberedProjectLeaf>();
        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label.Id) || string.IsNullOrWhiteSpace(label.Name))
                continue;
            if (EmailProjectLabelParser.TryParseProjectFromLabelPath(label.Name, root) is null)
                continue;

            var leaf = EmailProjectLabelParser.TryExtractProjectDisplaySegment(label.Name) ?? string.Empty;
            var match = NumberLeafRegex.Match(leaf.Trim());
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number))
                continue;

            result.Add(new NumberedProjectLeaf(label.Id, label.Name, leaf, number));
        }

        return result;
    }

    private sealed record NumberedProjectLeaf(string LabelId, string FullPath, string Leaf, int Number);
}
