namespace SiNet.Domain.Files;

/// <summary>
/// Decides which recover files belong in the ProjectWork tree and which are eligible for
/// bulk stale-delete (DEV-003 §4). Pure — callers supply size and timestamps from the scan.
/// </summary>
public static class RecoverFileRelevance
{
    /// <summary>
    /// Default age gap for bulk delete: any paired recover with
    /// <c>Primary.LastWriteTime - Recover.LastWriteTime &gt;= 0</c> (i.e. recover not newer than primary).
    /// </summary>
    public static readonly TimeSpan DefaultStaleDeleteThreshold = TimeSpan.Zero;

    public static bool IsZeroByte(long length) => length <= 0;

    /// <summary>
    /// Actionable recover: paired, non-empty, strictly newer than the primary.
    /// </summary>
    public static bool IsActionableNewerThanPrimary(
        long recoverLength,
        DateTime recoverLastWrite,
        DateTime primaryLastWrite) =>
        !IsZeroByte(recoverLength) && recoverLastWrite > primaryLastWrite;

    /// <summary>
    /// Eligible for «מחק recover ישנים»: must have a primary. Zero-byte paired recovers are always
    /// eligible. Otherwise recover must not be newer than primary by at least <paramref name="threshold"/>
    /// (default 0 ⇒ recover ≤ primary). Orphans are never eligible.
    /// </summary>
    public static bool IsEligibleForStaleDelete(
        bool hasPrimary,
        long recoverLength,
        DateTime recoverLastWrite,
        DateTime primaryLastWrite,
        TimeSpan? threshold = null)
    {
        if (!hasPrimary)
        {
            return false;
        }

        if (IsZeroByte(recoverLength))
        {
            return true;
        }

        var gap = threshold ?? DefaultStaleDeleteThreshold;
        return primaryLastWrite - recoverLastWrite >= gap;
    }
}
