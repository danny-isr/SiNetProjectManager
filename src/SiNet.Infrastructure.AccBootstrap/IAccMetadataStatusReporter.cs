namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Operations that can be attempted against ACC Custom Attributes. Native copy of
/// <c>SiNetSQL.FileIndex.AccMetadataOperation</c> — kept as a separate type so this project
/// does not need a ProjectReference to SiNetSQL (see docs/ACC_SERVICE_DECOUPLING.md, slice B4).
/// </summary>
public enum AccMetadataOperation
{
    /// <summary>Ensuring an attribute definition exists at project scope.</summary>
    DefineAttribute,

    /// <summary>Reading attributes from an item / version.</summary>
    ReadAttributes,

    /// <summary>Writing attributes onto an item / version.</summary>
    WriteAttributes,
}

/// <summary>
/// A single failed ACC metadata interaction. Native copy of
/// <c>SiNetSQL.FileIndex.AccMetadataIssue</c> (same shape) — see <see cref="AccMetadataOperation"/>.
/// </summary>
public sealed record AccMetadataIssue(
    DateTime OccurredUtc,
    AccMetadataOperation Operation,
    int? ProjectId,
    string? AccProjectId,
    string? ItemId,
    string? FileName,
    int? HttpStatus,
    string Reason);

/// <summary>
/// Reports ACC Custom-Attribute failures observed during provisioning.
/// <para>
/// This is the AccBootstrap-owned contract consumed by <see cref="AccProjectProvisioningService"/>.
/// The legacy <c>SiNetSQL.FileIndex.AccMetadataStatusReporter</c> singleton implements this
/// interface too (in addition to its own <c>SiNetSQL.FileIndex.IAccMetadataStatusReporter</c>)
/// so both the legacy UI status badge and this decoupled provisioning service share one reporter
/// instance. See docs/ACC_SERVICE_DECOUPLING.md, slice B4.
/// </para>
/// </summary>
public interface IAccMetadataStatusReporter
{
    /// <summary>Records a failure. Never throws.</summary>
    void Report(AccMetadataIssue issue);
}
