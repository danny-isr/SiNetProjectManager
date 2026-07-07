using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailEmailGatewayLabelTests
{
    [Fact]
    public void GetMailboxPage_with_label_id_uses_label_ids_not_q()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.Contains("if (!string.IsNullOrWhiteSpace(query.LabelId))", source, StringComparison.Ordinal);
        Assert.Contains("listRequest.LabelIds = labelIds.ToArray();", source, StringComparison.Ordinal);
        Assert.Contains("listRequest.Q = listQuery;", source, StringComparison.Ordinal);

        var labelIdIndex = source.IndexOf("query.LabelId", StringComparison.Ordinal);
        var labelIdsAssignmentIndex = source.IndexOf("listRequest.LabelIds = labelIds.ToArray();", StringComparison.Ordinal);
        var qAssignmentIndex = source.IndexOf("listRequest.Q = listQuery;", StringComparison.Ordinal);

        Assert.True(labelIdIndex >= 0);
        Assert.True(labelIdsAssignmentIndex > labelIdIndex);
        Assert.True(qAssignmentIndex > labelIdsAssignmentIndex);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNetProjectManager_GitHub.sln"))
                || File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_LIST_MIGRATION.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
