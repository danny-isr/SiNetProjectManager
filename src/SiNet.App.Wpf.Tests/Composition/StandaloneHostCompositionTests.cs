using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;
using SiNet.Application.ProjectWork;
using SiNet.Application.Runtime;
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

            // Startup path: App.xaml.cs restores the Gmail session before the shell opens,
            // so a missing logging/secrets registration here is a startup crash, not a lazy failure.
            Assert.NotNull(sp.GetRequiredService<IAppLogger>());
            Assert.NotNull(sp.GetRequiredService<IConnectorAuthService>());
            Assert.NotNull(sp.GetRequiredService<IEmailGateway>());
            Assert.NotNull(sp.GetRequiredService<IEmailSender>());
        });
    }

    /// <summary>
    /// Resolves every non-generic registered service so a broken constructor or a missing
    /// transitive registration surfaces here instead of as a runtime crash in the pilot.
    /// </summary>
    [Fact]
    public async Task WhenAddSiNetStandaloneHostThenEveryRegisteredServiceResolves()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var services = BuildStandaloneServices();
            var serviceTypes = services
                .Select(d => d.ServiceType)
                .Where(t => !t.IsGenericTypeDefinition)
                .Distinct()
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToArray();

            // Guards the sweep itself: if the graph stops being enumerable the test must fail
            // rather than pass vacuously. 313 registrations at the time of writing.
            Assert.True(
                serviceTypes.Length >= 250,
                $"Expected the standalone graph to expose 250+ service types, found {serviceTypes.Length}.");

            await using var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
            });
            using var scope = sp.CreateScope();

            var failures = new List<string>();
            foreach (var serviceType in serviceTypes)
            {
                try
                {
                    scope.ServiceProvider.GetService(serviceType);
                }
                catch (Exception ex)
                {
                    failures.Add($"{serviceType.FullName}: {Innermost(ex).Message}");
                }
            }

            Assert.True(
                failures.Count == 0,
                $"{failures.Count}/{serviceTypes.Length} services failed to resolve:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, failures));
        });
    }

    private static Exception Innermost(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }

    [Fact]
    public void WhenAddSiNetStandaloneHostThenAccInboxBootstrapLocalExecutorIsRegistered()
    {
        var services = BuildStandaloneServices();

        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IAccInboxBootstrapLocalExecutor));
    }

    /// <summary>
    /// The pilot host lost the eleven legacy health rows because it never registered the legacy
    /// bridge (docs/SYSTEM_HEALTH.md §1.3). These are the ported replacements, and a silent
    /// registration gap here shows up in production as a status panel that is quietly short of rows.
    /// </summary>
    [Fact]
    public async Task WhenAddSiNetStandaloneHostThenEverySystemHealthContributorResolvesWithAUniqueKey()
    {
        await RunOnStaThreadAsync(async () =>
        {
            await using var sp = BuildStandaloneServices().BuildServiceProvider();

            var contributors = sp.GetServices<ISubsystemStatusContributor>().ToList();
            var keys = contributors
                .Select(c => c.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                [
                    "InspectionReportsFolderId",
                    "InspectionTemplatesFolderId",
                    "acc-service",
                    "autodesk-acc",
                    "database",
                    "file-server",
                    "google",
                    "google_account",
                    "google_config",
                    "masterplan-reports-drive",
                    "ollama",
                    "workflow",
                ],
                keys);
        });
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
