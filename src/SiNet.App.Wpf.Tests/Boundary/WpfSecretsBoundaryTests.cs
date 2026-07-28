using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards the Credential Vault boundary in <c>SiNet.App.Wpf</c>.
/// <para>
/// The plan for this round was to drop the <c>SiNet.Infrastructure.Secrets</c> project reference
/// entirely and let <c>SiNet.App.Composition</c> own the registration. That is not possible: the
/// Secrets module targets <c>net10.0-windows</c> (Windows Credential Manager) while
/// <c>SiNet.App.Composition</c> is platform-neutral <c>net10.0</c>, so moving the reference would
/// force every Composition consumer onto a Windows-only TFM.
/// </para>
/// <para>
/// What is enforced instead is the property the boundary exists for: only this project's own
/// composition roots (<c>App.xaml.cs</c>, <c>StandaloneHostServiceCollectionExtensions.cs</c>)
/// may touch the infrastructure namespace. Every UI surface consumes the vault through
/// <c>SiNet.Application.Configuration</c> abstractions.
/// </para>
/// </summary>
public sealed class WpfSecretsBoundaryTests
{
    private const string SecretsInfrastructureNamespace = "SiNet.Infrastructure.Secrets";

    private static readonly HashSet<string> AllowedCompositionRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "App.xaml.cs",
        "StandaloneHostServiceCollectionExtensions.cs",
    };

    [Fact]
    public void WhenScanningWpfSourcesThenOnlyTheCompositionRootReferencesTheSecretsInfrastructure()
    {
        var projectRoot = Path.Combine(RepoPaths.RepoRoot, "src", "SiNet.App.Wpf");

        var offenders = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !AllowedCompositionRootFiles.Contains(Path.GetFileName(path)))
            .Where(path => File.ReadAllText(path)
                .Contains(SecretsInfrastructureNamespace, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(projectRoot, path))
            .ToArray();

        Assert.True(offenders.Length == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void WhenReadingTheWpfProjectThenTheSecretsReferenceCarriesItsJustification()
    {
        var projectFile = File.ReadAllText(
            Path.Combine(RepoPaths.RepoRoot, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj"));

        Assert.Contains("net10.0-windows", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenReadingTheCompositionProjectThenItDoesNotReferenceTheSecretsInfrastructure()
    {
        var projectFile = File.ReadAllText(
            Path.Combine(RepoPaths.RepoRoot, "src", "SiNet.App.Composition", "SiNet.App.Composition.csproj"));

        Assert.DoesNotContain("SiNet.Infrastructure.Secrets.csproj", projectFile, StringComparison.Ordinal);
    }
}
