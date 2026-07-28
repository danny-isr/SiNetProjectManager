using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.App.Wpf.Autodesk;

public static class AutodeskStatusServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAutodeskStatusWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Shared with the Secrets and Settings admin modules, which register it too.
        services.TryAddSingleton<AccControlPlaneStatusPresenter>();
        services.AddSingleton<IAccResolvedDocsUrlLauncher, ShellExecuteAccResolvedDocsUrlLauncher>();
        services.AddSingleton<IClipboardTextWriter, WpfClipboardTextWriter>();
        services.AddTransient<AccControlPlaneStatusWindowViewModel>();
        services.AddTransient<AccControlPlaneStatusWindowView>();
        services.AddTransient<AccControlPlaneStatusWindow>();

        return services;
    }
}
