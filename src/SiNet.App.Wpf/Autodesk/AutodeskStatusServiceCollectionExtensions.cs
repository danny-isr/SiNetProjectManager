using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Autodesk;

public static class AutodeskStatusServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAutodeskStatusWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AccControlPlaneStatusPresenter>();
        services.AddSingleton<IAccResolvedDocsUrlLauncher, ShellExecuteAccResolvedDocsUrlLauncher>();
        services.AddSingleton<IClipboardTextWriter, WpfClipboardTextWriter>();
        services.AddTransient<AccControlPlaneStatusWindowViewModel>();
        services.AddTransient<AccControlPlaneStatusWindowView>();
        services.AddTransient<AccControlPlaneStatusWindow>();

        return services;
    }
}
