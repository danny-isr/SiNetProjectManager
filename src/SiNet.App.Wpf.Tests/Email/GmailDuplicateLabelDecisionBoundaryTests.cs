using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

/// <summary>Source guards for DEV-009 Layer B (duplicate Gmail leaf keep/delete).</summary>
public sealed class GmailDuplicateLabelDecisionBoundaryTests
{
    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (SiNet.sln).");
    }

    [Fact]
    public void Modify_port_exposes_DeleteLabelAsync()
    {
        var modifyPort = ReadRepoFile("src/SiNet.Application/Abstractions/Email/IEmailGmailModifyService.cs");
        Assert.Contains("DeleteLabelAsync", modifyPort, StringComparison.Ordinal);
    }

    [Fact]
    public void Label_sync_port_exposes_ResolveDuplicateLeavesAsync()
    {
        var syncPort = ReadRepoFile("src/SiNet.Application/Projects/IProjectGmailLabelSyncService.cs");
        Assert.Contains("ResolveDuplicateLeavesAsync", syncPort, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_opens_duplicate_decision_dialog_not_warn_only_MessageBox()
    {
        var listVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        Assert.Contains("GmailDuplicateLabelDecisionDialog", listVm, StringComparison.Ordinal);
        Assert.Contains("ResolveDuplicateLeavesAsync", listVm, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "נמצאו לייבלים כפולים / לא חד-משמעיים — נדרשת החלטת משתמש",
            listVm,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Modify_port_lists_message_ids_before_label_delete()
    {
        var modifyPort = ReadRepoFile("src/SiNet.Application/Abstractions/Email/IEmailGmailModifyService.cs");
        Assert.Contains("ListMessageIdsByLabelAsync", modifyPort, StringComparison.Ordinal);
        Assert.Contains("DeleteLabelAsync", modifyPort, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_resolve_journals_message_ids_before_delete()
    {
        var sync = ReadRepoFile(
            "src/SiNet.Infrastructure.Sql/Services/Projects/ProjectGmailLabelSyncService.cs");
        Assert.Contains("ListMessageIdsByLabelAsync", sync, StringComparison.Ordinal);
        Assert.Contains("GmailLabelJournalAction.Deleted", sync, StringComparison.Ordinal);
        Assert.Contains("DuplicateResolve", sync, StringComparison.Ordinal);
    }
}
