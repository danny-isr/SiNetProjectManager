namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable machine codes for curated <c>ProjectFile</c> catalog slots.
/// Display titles live in the DB and may change; bootstrap and runtime resolve by these codes.
/// </summary>
public static class ProjectFileCatalogCodes
{
    /// <summary>Required quote-estimate workbook («אומדן הצעה») under חומר כללי / תכתובת → ניהול כספי.</summary>
    public const string QuoteEstimate = "QuoteEstimate";

    /// <summary>Required quote document («הצעות מחיר») under חומר כללי / תכתובת → ניהול כספי.</summary>
    public const string QuoteDocument = "QuoteDocument";

    /// <summary>Client approval PDF («אישור לקוח להצעה») under חומר כללי / תכתובת → ניהול כספי.</summary>
    public const string QuoteClientApproval = "QuoteClientApproval";

    /// <summary>
    /// Required client/orderer quote-request PDF («דרישת המזמין להצעת מחיר»)
    /// under תכתובת → ניהול כספי → הצעת מחיר.
    /// </summary>
    public const string QuoteClientRequest = "QuoteClientRequest";

    /// <summary>
    /// Optional send-ready quote PDF («הצעת מחיר לשליחה») under תכתובת → ניהול כספי.
    /// Filled from SendQuote attach when the slot has no physical file yet.
    /// </summary>
    public const string QuoteSendDocument = "QuoteSendDocument";
}
