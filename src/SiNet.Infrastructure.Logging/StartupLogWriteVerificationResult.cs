namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Result of DEV-028 startup log write-back verify (local + optional central).
/// </summary>
public sealed record StartupLogWriteVerificationResult(
    string Marker,
    bool LocalOk,
    bool CentralConfigured,
    bool CentralOk,
    string? LocalPath,
    string? CentralPath,
    string? Detail,
    DateTimeOffset CheckedUtc)
{
    /// <summary>Hebrew splash line (meaning-locked by DEV-028 §3.2).</summary>
    public string SplashStatusHe
    {
        get
        {
            if (!LocalOk)
                return "לא ניתן לכתוב לוג מקומי — ראה מצב מערכת";
            if (CentralConfigured && !CentralOk)
                return "הלוג המרכזי לא נכתב — ראה מצב מערכת";
            if (CentralConfigured && CentralOk)
                return "הלוג נכתב (מקומי + מרכזי)";
            return "הלוג נכתב (מקומי)";
        }
    }

    public bool IsFullyOk => LocalOk && (!CentralConfigured || CentralOk);
}
