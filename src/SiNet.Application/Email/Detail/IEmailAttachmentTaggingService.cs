namespace SiNet.Application.Email.Detail;

public interface IEmailAttachmentTaggingService
{
    Task<IReadOnlyList<EmailInboxAttachmentTagState>> LoadInboxAttachmentsAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailProjectAlternativeOption>> LoadAlternativesAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<EmailProjectAlternativeOption?> CreateAlternativeAsync(
        int projectId,
        string name,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailAttachmentTagTarget>> LoadTagTargetsAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hierarchical catalog for the shared FileTreePicker (OutSidData + folders + job-type filter).
    /// Mirrors legacy <c>AttachmentTaggingService.LoadStrictExternalAsync</c> scope.
    /// </summary>
    Task<EmailAttachmentTagPickerCatalog> LoadTagPickerCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<EmailAttachmentTagValidationResult> ValidateTagAsync(
        EmailAttachmentTagValidationQuery query,
        CancellationToken cancellationToken = default);

    Task<EmailAttachmentTagResult> SetTagAsync(
        EmailAttachmentTagCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Host prompt for naming a new project alternative (V2 dialog).</summary>
public interface IEmailAlternativeNamePromptHost
{
    bool IsAvailable { get; }

    Task<string?> PromptForNewAlternativeNameAsync(
        IReadOnlyList<string> existingNames,
        CancellationToken cancellationToken = default);
}

public sealed record EmailInboxAttachmentTagState(
    int InboxAttachmentId,
    string FileName,
    int AttachmentIndex,
    int? ProjectFileId,
    string? ProjectFileTitle,
    int? ProjectAlternativeId,
    bool IsTaggable,
    string? AccItemId = null)
{
    public bool CanOpenInAcc => !string.IsNullOrWhiteSpace(AccItemId);
}

public sealed record EmailProjectAlternativeOption(
    int Id,
    string Name,
    bool IsDefault,
    bool IsCreateNew = false)
{
    public const int CreateNewId = -1;

    public static EmailProjectAlternativeOption CreateNewSentinel { get; } =
        new(CreateNewId, "+ חדש...", IsDefault: false, IsCreateNew: true);

    /// <summary>
    /// Picks the default alternative for tagging: flagged default, else name "1", else first real option.
    /// </summary>
    public static int? ResolveDefaultId(IEnumerable<EmailProjectAlternativeOption>? options)
    {
        if (options is null)
        {
            return null;
        }

        var real = options.Where(static a => !a.IsCreateNew && a.Id > 0).ToList();
        if (real.Count == 0)
        {
            return null;
        }

        return real.FirstOrDefault(static a => a.IsDefault)?.Id
            ?? real.FirstOrDefault(static a => string.Equals(a.Name, "1", StringComparison.Ordinal))?.Id
            ?? real[0].Id;
    }
}

public sealed record EmailAttachmentTagTarget(
    int ProjectFileId,
    string DisplayName,
    bool HasAlternatives);

public sealed record EmailAttachmentTagPickerFile(
    int ProjectFileId,
    string Title,
    int? TypeProjId,
    string? TypeTitle,
    int? FolderId,
    float? Number,
    bool IsRequired = false,
    string? Code = null);

public sealed record EmailAttachmentTagPickerFolder(
    int FolderId,
    string Title,
    int? ParentFolderId);

public sealed record EmailAttachmentTagPickerJobType(int Id, string Title);

public sealed record EmailAttachmentTagPickerCatalog(
    IReadOnlyList<EmailAttachmentTagPickerFile> Files,
    IReadOnlyList<EmailAttachmentTagPickerFolder> Folders,
    IReadOnlyList<EmailAttachmentTagPickerJobType> JobTypes);

public sealed record EmailAttachmentTagValidationQuery(
    int InboxMessageId,
    int InboxAttachmentId,
    int ProjectFileId,
    int? ProjectAlternativeId);

public sealed record EmailAttachmentTagValidationResult(
    bool IsAllowed,
    string? BlockReason,
    bool WillCreateNewVersion);

public sealed record EmailAttachmentTagCommand(
    int InboxAttachmentId,
    int ProjectFileId,
    int? ProjectAlternativeId,
    int ActingUserId);

public sealed record EmailAttachmentTagResult(bool Succeeded, string? ErrorMessage);
