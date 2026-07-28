using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public static class MasterPlanAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetMasterPlanAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<MasterPlanMappingViewModel>();
        services.AddTransient<MasterPlanMappingWindow>();
        return services;
    }
}
