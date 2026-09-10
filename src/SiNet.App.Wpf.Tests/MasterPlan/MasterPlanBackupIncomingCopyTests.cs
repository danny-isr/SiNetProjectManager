using System.IO;
using SiNet.Application.MasterPlanBackup;
using Xunit;

namespace SiNet.App.Wpf.Tests.MasterPlan;

public sealed class MasterPlanBackupIncomingCopyTests
{
    [Fact]
    public async Task Copy_uses_partial_then_rename_and_keeps_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "mp-inbox-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(Path.GetTempPath(), "src-" + Guid.NewGuid().ToString("N") + ".bak");
        try
        {
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
            var (dest, sha) = await MasterPlanBackupIncomingCopy.CopyAtomicallyAsync(
                source,
                root,
                "copy.bak");

            Assert.True(File.Exists(source));
            Assert.True(File.Exists(dest));
            Assert.False(File.Exists(dest + MasterPlanBackupIncomingCopy.PartialExtension));
            Assert.Equal(Path.Combine(root, "Incoming", "copy.bak"), dest);
            Assert.False(string.IsNullOrWhiteSpace(sha));
            Assert.Equal(4, new FileInfo(dest).Length);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Partial_files_are_not_completed_baks()
    {
        Assert.True(MasterPlanBackupIncomingCopy.IsPartialFile(@"D:\Incoming\x.bak.partial"));
        Assert.False(MasterPlanBackupIncomingCopy.IsCompletedBak(@"D:\Incoming\x.bak.partial"));
        Assert.True(MasterPlanBackupIncomingCopy.IsCompletedBak(@"D:\Incoming\x.bak"));
        Assert.Equal(@"D:\SharedFolder\ProjectsData\MasterPlanBakup", MasterPlanBackupIncomingCopy.DefaultInboxRoot);
        Assert.DoesNotContain("N:\\", MasterPlanBackupIncomingCopy.DefaultInboxRoot, StringComparison.OrdinalIgnoreCase);
    }
}
