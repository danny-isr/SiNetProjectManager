using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;
using SiNet.Application.ProjectWork;
using SiNet.Application.Settings;
using Xunit;

namespace SiNet.App.Wpf.Tests.Composition;

/// <summary>
/// Offline composition smoke for <see cref="StandaloneHostServiceCollectionExtensions.AddSiNetStandaloneHost"/>.
/// Registers with a dummy SQL connection string — no live DB call. Does not open WPF windows.
/// See <c>docs/TEST_STRATEGY.md</c> L3.
/// </summary>
public sealed class StandaloneHostCompositionTests
{
    [Fact]
    public async Task WhenAddSiNetStandaloneHostThenKeyPortsResolveWithoutDuplicateImplementations()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var services = BuildStandaloneServices();

            var duplicates = services
                .Where(d => d.ImplementationType is not null)
                .GroupBy(d => (d.ServiceType, d.ImplementationType))
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key.ServiceType!.FullName} -> {g.Key.ImplementationType!.FullName} x{g.Count()}")
                .ToArray();

            Assert.True(duplicates.Length == 0, string.Join(Environment.NewLine, duplicates));

            await using var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
            });

            // Light resolves only — do not construct full WPF windows / SQL round-trips.
            Assert.NotNull(sp.GetRequiredService<INewShellFactory>());
            Assert.NotNull(sp.GetRequiredService<IEmailSurfaceHost>());
            Assert.NotNull(sp.GetRequiredService<IAccInboxBootstrapService>());
            Assert.NotNull(sp.GetRequiredService<IAccServiceModeProvider>());
            Assert.NotNull(sp.GetRequiredService<IAuthorizationQueryService>());
            Assert.NotNull(sp.GetRequiredService<ISystemSettingsQueryService>());
            Assert.NotNull(sp.GetRequiredService<IInspectionTemplateCatalog>());
            Assert.NotNull(sp.GetRequiredService<IProjectWorkSurfaceHost>());
            Assert.NotNull(sp.GetRequiredService<ILoggingRuntimeApplier>());
            Assert.NotNull(sp.GetRequiredService<IDirectoryUserLookupService>());
            Assert.NotNull(sp.GetRequiredService<IMasterPlanR01ReportService>());
            Assert.NotNull(sp.GetRequiredService<IMasterPlanR02ReportService>());
            Assert.NotNull(sp.GetRequiredService<IMasterPlanR03ReportService>());
            Assert.NotNull(sp.GetRequiredService<IAccInboxBootstrapLocalExecutor>());
        });
    }

    [Fact]
    public void WhenAddSiNetStandaloneHostThenAccInboxBootstrapLocalExecutorIsRegistered()
    {
        var services = BuildStandaloneServices();

        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IAccInboxBootstrapLocalExecutor));
    }

    private static ServiceCollection BuildStandaloneServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccService:BaseUrl"] = "https://localhost:8443",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSiNetStandaloneHost(
            configuration,
            sqlConnectionString: "Server=(localdb)\\MSSQLLocalDB;Database=SiNetCompositionSmoke;Trusted_Connection=True;TrustServerCertificate=True",
            configureGmail: static options =>
            {
                options.ApplicationName = "SiNet.CompositionSmoke";
            });

        return services;
    }

    private static Task RunOnStaThreadAsync(Func<Task> body)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                body().GetAwaiter().GetResult();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
