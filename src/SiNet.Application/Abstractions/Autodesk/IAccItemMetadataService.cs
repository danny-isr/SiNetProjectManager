namespace SiNet.Application.Abstractions.Autodesk.Metadata;

/// <summary>
/// Result of an ACC item metadata write operation.
/// </summary>
public sealed record AccItemMetadataResult(
    bool Success,
    int? HttpStatus,
    string? ErrorMessage)
{
    public static AccItemMetadataResult Ok() => new(true, null, null);

    public static AccItemMetadataResult Fail(int? httpStatus, string errorMessage) =>
        new(false, httpStatus, errorMessage);
}

/// <summary>
/// Result of an ACC item metadata read operation.
/// </summary>
public sealed record AccItemMetadataReadResult(
    bool Success,
    IReadOnlyDictionary<string, string?> Attributes,
    int? HttpStatus,
    string? ErrorMessage)
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyAttributes =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    public static AccItemMetadataReadResult Ok(IReadOnlyDictionary<string, string?> attributes) =>
        new(true, attributes, null, null);

    public static AccItemMetadataReadResult Fail(int? httpStatus, string errorMessage) =>
        new(false, EmptyAttributes, httpStatus, errorMessage);
}

/// <summary>
/// Shared ACC Custom Attributes facade for project files and Office Inbox files.
/// This service is metadata-only: read/write failures are reported clearly and
/// must not be interpreted as proof that the ACC file itself is missing.
/// <para>
/// Native port of the legacy <c>SiNetSQL.FileIndex.IAccItemMetadataService</c>.
/// The SDK-facing implementation lives in <c>SiNet.Infrastructure.Autodesk</c>.
/// </para>
/// </summary>
public interface IAccItemMetadataService
{
    ValueTask<AccItemMetadataReadResult> ReadAttributesAsync(
        string accProjectId,
        string itemId,
        string? fileNameForLogging,
        CancellationToken cancellationToken);

    ValueTask<AccItemMetadataResult> WriteAttributesAsync(
        string accProjectId,
        string accFolderId,
        string versionId,
        string itemId,
        IReadOnlyDictionary<string, string?> attributes,
        string? fileNameForLogging,
        CancellationToken cancellationToken);
}
