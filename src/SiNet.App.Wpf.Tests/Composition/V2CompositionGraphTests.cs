using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Composition;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Actions;
using SiNet.Application.Configuration;
using SiNet.Application.Identity;
using SiNet.Application.ProjectWork;
using Xunit;

namespace SiNet.App.Wpf.Tests.Composition;

/// <summary>
/// Guards the V2 hybrid service graph after it converged on
/// <c>AddSiNet(<see cref="SiNetHostMode.V2Hybrid"/>)</c>. The shared root and the V2 host both reach
/// several modules, so these tests pin the two properties that convergence can silently break:
/// enumerable services must not gain duplicate entries, and the V2 adapters must still be the
/// effective (last) registration over the shared no-op defaults.
/// </summary>
public sealed class V2CompositionGraphTests
{
    [Fact]
    public void WhenBuildingTheV2GraphThenEnumerableServicesHaveNoDuplicateImplementations()
    {
        var services = BuildGraph();

        var duplicates = services
            .Where(d => d.ImplementationType is not null)
            .GroupBy(d => (d.ServiceType, d.ImplementationType))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.ServiceType!.FullName} -> {g.Key.ImplementationType!.FullName} x{g.Count()}")
            .ToArray();

        Assert.True(duplicates.Length == 0, string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void WhenBuildingTheV2GraphThenEveryProcessActionHandlerIsRegisteredExactlyOnce()
    {
        var services = BuildGraph();

        var handlers = services
            .Where(d => d.ServiceType == typeof(IProcessActionHandler))
            .ToArray();

        Assert.Equal(handlers.Select(d => d.ImplementationType).Distinct().Count(), handlers.Length);
    }

    [Fact]
    public void WhenBuildingTheV2GraphThenEachFileStoreBackendIsRegisteredExactlyOnce()
    {
        var services = BuildGraph();

        var stores = services
            .Where(d => d.ServiceType == typeof(IFileStore))
            .ToArray();

        Assert.Equal(stores.Select(d => d.ImplementationType).Distinct().Count(), stores.Length);
    }

    [Theory]
    [InlineData(typeof(IInspectionFileTreePickerHost), "V2InspectionFileTreePickerHost")]
    [InlineData(typeof(IInspectionReportEmailHost), "V2InspectionReportEmailHost")]
    [InlineData(typeof(IInspectionNoteScreenshotHost), "V2InspectionNoteScreenshotHost")]
    [InlineData(typeof(IInspectionNoteLinkedFileHost), "V2InspectionNoteLinkedFileHost")]
    [InlineData(typeof(IInspectionTemplateCatalog), "V2InspectionTemplateCatalog")]
    [InlineData(typeof(IInspectionReportExportPort), "V2InspectionReportExportPort")]
    [InlineData(typeof(ISecretSetupHostConfiguration), "LegacySecretSetupHostConfiguration")]
    [InlineData(typeof(IDirectoryUserConnectionProvider), "LegacyDirectoryUserConnectionProvider")]
    [InlineData(typeof(IMasterPlanEmployeeConnectionProvider), "LegacyMasterPlanEmployeeConnectionProvider")]
    [InlineData(typeof(IAppLogger), "SerilogAppLogger")]
    public void WhenBuildingTheV2GraphThenTheHostAdapterIsTheEffectiveRegistration(
        Type serviceType,
        string expectedImplementationName)
    {
        var services = BuildGraph();

        var effective = services.Last(d => d.ServiceType == serviceType);

        Assert.Equal(expectedImplementationName, effective.ImplementationType?.Name);
    }

    private static ServiceCollection BuildGraph()
    {
        var services = new ServiceCollection();
        SiNetProjectManagerV2.Services.Composition.NewSystemServiceCollectionExtensions
            .AddSiNetNewSystemGraph(services);
        return services;
    }
}
