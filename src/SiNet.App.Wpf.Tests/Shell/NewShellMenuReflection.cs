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
}
