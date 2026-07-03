namespace SiNet.App.Wpf.Autodesk;

internal static class AccResolvedDocsUrlBuilder
{
    public static string Build(string projectId, string folderId, string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var docsProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? projectId[2..]
            : projectId;

        var url = $"https://acc.autodesk.com/docs/files/projects/{docsProjectId}";
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            url += $"?folderUrn={Uri.EscapeDataString(folderId)}&entityId={Uri.EscapeDataString(itemId)}";
        }
        else
        {
            url += $"?entityId={Uri.EscapeDataString(itemId)}";
        }

        return url;
    }
}
