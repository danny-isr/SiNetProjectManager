using System.IO;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

internal static class EmailListImplementationSource
{
    private static readonly string[] ImplementationRelativePaths =
    [
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.Design.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.CoordinatorHost.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListRowMapper.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListRowDisplayCoordinator.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListPagingCoordinator.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListFilingCoordinator.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/EmailListGroupingCoordinator.cs",
        "src/SiNet.App.Wpf/Surfaces/Email/Internal/IEmailListRowMutator.cs",
    ];

    public static string ReadCombined()
    {
        var root = FindRepoRoot();
        return string.Concat(ImplementationRelativePaths.Select(path =>
            File.ReadAllText(Path.Combine(root, path))));
    }

    public static string ReadViewModelOnly()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, ImplementationRelativePaths[0]));
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
