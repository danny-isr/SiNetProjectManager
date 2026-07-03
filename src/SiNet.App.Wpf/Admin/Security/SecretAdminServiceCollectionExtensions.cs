using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;

namespace SiNet.App.Wpf.Admin.Security;

public static class SecretAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSecretAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AccControlPlaneStatusPresenter>();
        services.AddTransient<SecretSetupViewModel>();
        services.AddTransient<SecretSetupView>();
        services.AddTransient<SecretSetupWindow>();

        return services;
    }
}
