using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccFolderRenameService(IAccTransferConnector connector) : IAccFolderRenameService
{
    private readonly IAccTransferConnector _connector = connector;

    public async Task<AccFolderRenameOutcome> RenameFolderAsync(
        string accProjectId,
        string folderId,
        string newFolderName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accProjectId)
            || string.IsNullOrWhiteSpace(folderId)
            || string.IsNullOrWhiteSpace(newFolderName))
        {
            return new AccFolderRenameOutcome(
                AccFolderRenameStatus.Failed,
                "חסרים מזהה פרויקט ACC, מזהה תיקייה או שם חדש.");
        }

        try
        {
            await _connector
                .RenameFolderAsync(
                    NormalizeProjectId(accProjectId),
                    folderId.Trim(),
                    newFolderName.Trim(),
                    cancellationToken)
                .ConfigureAwait(false);

            return new AccFolderRenameOutcome(
                AccFolderRenameStatus.Succeeded,
                $"ACC Docs: שם התיקייה עודכן ל־'{newFolderName.Trim()}'");
        }
        catch (InvalidOperationException ex)
        {
            return new AccFolderRenameOutcome(AccFolderRenameStatus.Failed, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return new AccFolderRenameOutcome(AccFolderRenameStatus.Failed, ex.Message);
        }
    }

    private static string NormalizeProjectId(string projectId)
    {
        var trimmed = projectId.Trim();
        return trimmed.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"b.{trimmed}";
    }
}
