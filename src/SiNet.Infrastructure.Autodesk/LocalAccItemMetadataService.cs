using System.Diagnostics;
using SiNet.Application.Abstractions.Autodesk.Metadata;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// In-process (SDK) implementation of <see cref="IAccItemMetadataService"/> — the privileged
/// "Local" side of the ACC control-plane separation. Runs on hosts that hold Autodesk credentials
/// (e.g. <c>SiOffice.AccService</c>); on the WPF client it is only used when the app is configured
/// for <see cref="SiNet.Application.Abstractions.Autodesk.AccServiceMode.Local"/>.
/// <para>
/// Translates the ACC-SDK <c>AccMetadataResult</c> returned by <see cref="IAccTransferConnector"/>
/// into the SDK-free Application result records. Metadata-only: read/write failures are surfaced as
/// failed results (never thrown for ordinary ACC errors) so callers must not interpret a failure as
/// proof the ACC file is missing.
/// </para>
/// </summary>
internal sealed class LocalAccItemMetadataService(IAccTransferConnector connector) : IAccItemMetadataService
{
    private readonly IAccTransferConnector _connector = connector;

    public async ValueTask<AccItemMetadataReadResult> ReadAttributesAsync(
        string accProjectId,
        string itemId,
        string? fileNameForLogging,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            return ReportReadFailure(itemId, null, "accProjectId is required.");
        if (string.IsNullOrWhiteSpace(itemId))
            return ReportReadFailure(itemId, null, "itemId is required.");

        try
        {
            var result = await _connector
                .GetItemCustomAttributesAsync(accProjectId, itemId, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                return AccItemMetadataReadResult.Ok(
                    result.Value ?? new Dictionary<string, string?>(StringComparer.Ordinal));
            }

            return ReportReadFailure(
                itemId,
                result.HttpStatus,
                result.ErrorMessage ?? "Unknown metadata read error.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ReportReadFailure(itemId, null, $"Unexpected: {ex.Message}");
        }
    }

    public async ValueTask<AccItemMetadataResult> WriteAttributesAsync(
        string accProjectId,
        string accFolderId,
        string versionId,
        string itemId,
        IReadOnlyDictionary<string, string?> attributes,
        string? fileNameForLogging,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            return ReportWriteFailure(itemId, null, "accProjectId is required.");
        if (string.IsNullOrWhiteSpace(accFolderId))
            return ReportWriteFailure(itemId, null, "accFolderId is required.");
        if (string.IsNullOrWhiteSpace(versionId))
            return ReportWriteFailure(itemId, null, "AccVersionId is required for ACC custom attribute writes.");
        if (attributes is null || attributes.Count == 0)
            return AccItemMetadataResult.Ok();

        try
        {
            var result = await _connector
                .SetItemCustomAttributesAsync(accProjectId, accFolderId, versionId, attributes, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                Trace.TraceInformation(
                    $"[AccItemMetadata][Local] WriteAttributes OK itemId='{itemId}' versionId='{versionId}' attrs={attributes.Count}");
                return AccItemMetadataResult.Ok();
            }

            return ReportWriteFailure(
                itemId,
                result.HttpStatus,
                result.ErrorMessage ?? "Unknown metadata write error.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ReportWriteFailure(itemId, null, $"Unexpected: {ex.Message}");
        }
    }

    private static AccItemMetadataReadResult ReportReadFailure(string? itemId, int? httpStatus, string errorMessage)
    {
        Trace.TraceWarning($"[AccItemMetadata][Local] ReadAttributes FAILED itemId='{itemId}' http={httpStatus}: {errorMessage}");
        return AccItemMetadataReadResult.Fail(httpStatus, errorMessage);
    }

    private static AccItemMetadataResult ReportWriteFailure(string? itemId, int? httpStatus, string errorMessage)
    {
        Trace.TraceWarning($"[AccItemMetadata][Local] WriteAttributes FAILED itemId='{itemId}' http={httpStatus}: {errorMessage}");
        return AccItemMetadataResult.Fail(httpStatus, errorMessage);
    }
}
