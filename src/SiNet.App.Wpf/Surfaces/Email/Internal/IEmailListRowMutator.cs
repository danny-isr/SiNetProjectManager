namespace SiNet.App.Wpf.Surfaces.Email.Internal;

/// <summary>
/// Row mutation surface for email list coordinators (display + filing).
/// </summary>
internal interface IEmailListRowMutator
{
    void ReplaceRowInDisplay(EmailListRow updated);

    EmailListRow? FindRowById(string rowId);

    void ApplyLocalEmailMutation(EmailListRow updated);

    void RefreshRowBackgrounds();

    bool TrySelectByInboxCorrelation(
        string? messageUniqueId,
        string? internetMessageId,
        string? subject,
        string? fromAddress);

    IReadOnlyList<EmailListRow> ApplyClientRowFilters(IReadOnlyList<EmailListRow> rows);

    void ReplaceRows(
        IReadOnlyList<EmailListRow> rows,
        string? preserveSelectionId = null,
        bool skipDisplayRebuild = false);

    void RemoveRowFromDisplay(EmailListRow row);

    void RebindSelectedEmail(EmailListRow updated);
}
