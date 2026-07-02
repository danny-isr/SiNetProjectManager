using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.Security;

public static class SecretAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSecretAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<SecretSetupViewModel>();
        services.AddTransient<SecretSetupView>();
        services.AddTransient<SecretSetupWindow>();

        return services;
    }
}
