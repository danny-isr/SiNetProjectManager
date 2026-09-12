using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailDispatcherOwnershipSourceGuardTests
{
    [Fact]
    public void ReplaceRows_has_dispatcher_boundary()
    {
        var source = Read("src/SiNet.App.Wpf/Surfaces/Email/EmailListRowDisplayCoordinator.cs");
        Assert.Contains("UiThread.Run(() => ReplaceRowsCore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableCollectionSynchronization", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentProjectChanged_marshals_before_collection_mutation()
    {
        var source = Read("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        Assert.Contains("CurrentProjectChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("UiThread.Run(() =>", source, StringComparison.Ordinal);
        Assert.Contains("_display.RefreshRowBackgrounds();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Fire_and_forget_startup_uses_observed_task()
    {
        var window = Read("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.Contains("ObservedTask.Run(", window, StringComparison.Ordinal);
        Assert.Contains("AutoRefreshOnOpenAsync()", window, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = AutoRefreshOnOpenAsync()", window, StringComparison.Ordinal);

        var host = Read("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceHost.cs");
        Assert.Contains("ObservedTask.Run(", host, StringComparison.Ordinal);
        Assert.Contains("ResetToDefaultBrowseAsync()", host, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), "Missing " + path);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("SiNet.sln not found from test working directory.");
    }
}
