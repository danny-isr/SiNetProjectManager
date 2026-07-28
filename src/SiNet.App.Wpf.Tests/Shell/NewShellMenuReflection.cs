using System.Reflection;
using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Shared helper for menu tests that exercise <c>NewShellFactory.BuildMigratedOnlyMenuAsync</c>.
/// The method is private (the shell is built through <see cref="INewShellFactory.CreateShellAsync"/>),
/// and it became async when the shell stopped blocking on the authorization port, so the reflection
/// call has to unwrap the returned task.
/// </summary>
internal static class NewShellMenuReflection
{
    /// <summary>Top-level hierarchical menu (groups + children).</summary>
    public static IReadOnlyList<NewShellMenuItem> Build(NewShellFactory factory)
    {
        var method = typeof(NewShellFactory).GetMethod(
            "BuildMigratedOnlyMenuAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<IReadOnlyList<NewShellMenuItem>>)method!.Invoke(
            factory,
            [CancellationToken.None])!;

        return task.GetAwaiter().GetResult();
    }

    /// <summary>Leaf menu items only (hierarchical groups are expanded).</summary>
    public static IReadOnlyList<NewShellMenuItem> BuildFlattened(NewShellFactory factory)
        => Flatten(Build(factory));

    public static IReadOnlyList<NewShellMenuItem> Flatten(IEnumerable<NewShellMenuItem> items)
    {
        var leaves = new List<NewShellMenuItem>();
        foreach (var item in items)
        {
            if (item.IsGroup && item.Children is { Count: > 0 })
            {
                leaves.AddRange(Flatten(item.Children));
            }
            else
            {
                leaves.Add(item);
            }
        }

        return leaves;
    }
}
