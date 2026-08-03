namespace SiNet.Application.Email;

/// <summary>
/// Builds a Gmail web URL that opens an existing message so the operator can Reply / Forward
/// inside Gmail. Single place for the URL form — if Google ever breaks <c>#all/{id}</c>, switch here.
/// </summary>
public static class GmailMessageUrlBuilder
{
    /// <param name="gmailMessageId">Gmail API message id (the app's <c>EmailListRow.Id</c>).</param>
    /// <param name="accountEmail">
    /// Connected Google account. When known, used as the <c>u/</c> segment so a multi-account browser
    /// opens the right mailbox; otherwise falls back to <c>u/0</c> (first signed-in account).
    /// </param>
    public static string Build(string gmailMessageId, string? accountEmail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gmailMessageId);

        var accountSegment = string.IsNullOrWhiteSpace(accountEmail)
            ? "0"
            : Uri.EscapeDataString(accountEmail.Trim());
        var messageSegment = Uri.EscapeDataString(gmailMessageId.Trim());

        return $"https://mail.google.com/mail/u/{accountSegment}/#all/{messageSegment}";
    }
}
