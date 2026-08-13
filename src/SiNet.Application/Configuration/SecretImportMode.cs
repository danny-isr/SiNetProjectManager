namespace SiNet.Application.Configuration;

/// <summary>
/// How catalog keys in the local vault are updated from a <c>.secrets</c> package.
/// Unknown (non-catalog) keys are never written or deleted.
/// </summary>
public enum SecretImportMode
{
    /// <summary>
    /// Write every catalog key present in the file (create or replace). Keys not in the file stay.
    /// </summary>
    UpsertFromFile = 0,

    /// <summary>
    /// Upsert keys present in the file, then delete catalog keys that exist in this Windows user's
    /// vault but are absent from the file (empty file values do not count as present).
    /// </summary>
    ReplaceCatalogWithFile = 1,
}
