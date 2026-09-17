namespace SiNet.Application.Email;

/// <summary>Operator-facing filing messages. Logs keep the technical exception.</summary>
public static class EmailFilingUserMessages
{
    public const string GmailFailed = "שיוך Gmail נכשל";
    public const string SqlFailed = "שמירת שיוך הפרויקט נכשלה";
    public const string AccFailed = "העלאת הצרופות ל-ACC נכשלה";
    public const string Queued = "ממתין לתיוק";
    public const string Filing = "משייך לפרויקט...";
    public const string AccUploading = "מעלה ל-ACC...";

    public static string FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is GmailDuplicateProjectLabelException duplicate)
        {
            return duplicate.Message;
        }

        var text = exception.ToString();
        if (text.Contains("GoogleApiException", StringComparison.Ordinal)
            || text.Contains("Gmail", StringComparison.OrdinalIgnoreCase)
            || text.Contains("HttpStatusCode", StringComparison.Ordinal))
        {
            return GmailFailed;
        }

        if (text.Contains("DbUpdate", StringComparison.Ordinal)
            || text.Contains("SqlException", StringComparison.Ordinal)
            || text.Contains("Entity Framework", StringComparison.OrdinalIgnoreCase))
        {
            return SqlFailed;
        }

        return GmailFailed;
    }

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return GmailFailed;
        }

        if (message.Contains("Exception", StringComparison.Ordinal)
            || message.Contains("See inner", StringComparison.OrdinalIgnoreCase))
        {
            return FromException(new InvalidOperationException(message));
        }

        return message;
    }
}
