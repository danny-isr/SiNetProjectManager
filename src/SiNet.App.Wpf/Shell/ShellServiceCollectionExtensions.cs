using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// DI registration for the clean New System shell (see <c>docs/APP_SHELL.md</c>).
/// <para>
/// Registers <see cref="INewShellFactory"/> so a host can open <see cref="NewShellWindow"/> in New
/// system mode instead of the legacy main window. The factory resolves migrated surfaces lazily from
/// the application <see cref="IServiceProvider"/>, so registering the shell does not pull in any legacy
/// window or menu. Call this <b>after</b> the surfaces the shell opens are registered (Project Context
/// via <c>AddSiNetProjectContext</c>, and the Inspection shell view).
/// </para>
/// </summary>
public static class ShellServiceCollectionExtensions
{
    /// <summary>
    /// Registers the New System shell factory. Idempotent-friendly: uses <c>TryAdd</c> semantics via
    /// a singleton factory so repeated calls do not duplicate the registration.
    /// </summary>
    public static IServiceCollection AddSiNetShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INewShellFactory, NewShellFactory>();
        services.AddSingleton<IShellContentHost, ShellContentHost>();

        return services;
    }
}
