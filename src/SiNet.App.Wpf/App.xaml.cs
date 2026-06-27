using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Composition;
using SiNet.App.Wpf.Inbox;
using SiNet.Infrastructure.Google;

namespace SiNet.App.Wpf;

/// <summary>
/// WPF host scaffold. Demonstrates the intended startup shape for the App-startup migration
/// round: build configuration, build the service graph with the single <c>AddSiNet()</c>
/// composition call, then resolve the shell window from DI. This scaffold is NOT yet the
/// production entry point and does not replace the existing application.
/// </summary>
// Base type is fully qualified: this project references the SiNet.Application namespace, so an
// unqualified "Application" would bind to that namespace instead of System.Windows.Application.
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private IConfiguration? _configuration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configuration = BuildConfiguration();

        var services = new ServiceCollection();
        services.AddSingleton(_configuration);
        services.AddSiNet(ConfigureGmail);
        services.AddSingleton<InboxViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        // Attempt a silent (no-browser) restore of a previously authorized Gmail session so a
        // returning user is connected automatically. Runs off the UI thread and never blocks
        // startup; the provider is non-throwing for the "not signed in" case.
        var provider = _services.GetRequiredService<GmailClientProvider>();
        _ = Task.Run(() => provider.TrySignInSilentlyAsync());

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    /// <summary>
    /// Builds the host configuration from <c>appsettings.json</c> (the real config source) with
    /// environment variables layered on top. The new stack owns its own configuration and has no
    /// dependency on the legacy <c>SiNetProjectManagerV2.AppConfiguration</c>.
    /// </summary>
    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>
    /// Configures the native Gmail integration. Values are bound from the <c>Gmail</c> section of
    /// <c>appsettings.json</c>; the legacy <c>SINET_GOOGLE_*</c> environment variables, when set,
    /// override the file so existing developer setups keep working. When no client secrets are
    /// configured, <see cref="GmailOptions.ClientSecretsPath"/> stays empty and the gateway
    /// degrades gracefully (no sign-in, empty inbox) instead of throwing.
    /// </summary>
    private void ConfigureGmail(GmailOptions options)
    {
        _configuration?.GetSection("Gmail").Bind(options);

        // Back-compat overrides: explicit env vars win over the config file.
        var secretsPath = Environment.GetEnvironmentVariable("SINET_GOOGLE_CLIENT_SECRETS");
        if (!string.IsNullOrWhiteSpace(secretsPath))
        {
            options.ClientSecretsPath = secretsPath;
        }

        var tokenStore = Environment.GetEnvironmentVariable("SINET_GOOGLE_TOKEN_STORE");
        if (!string.IsNullOrWhiteSpace(tokenStore))
        {
            options.TokenStorePath = tokenStore;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
