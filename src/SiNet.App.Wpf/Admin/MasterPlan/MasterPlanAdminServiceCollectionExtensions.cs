using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.MasterPlan.Reports;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public static class MasterPlanAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetMasterPlanAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<MasterPlanMappingViewModel>();
        services.AddTransient<MasterPlanMappingWindow>();
        services.AddTransient<MasterPlanMonthlyRestoreViewModel>();
        services.AddTransient<MasterPlanMonthlyRestoreWindow>();
        services.AddTransient<R01ReportViewModel>();
        services.AddTransient<R01ReportWindow>();
        services.AddTransient<R02ReportViewModel>();
        services.AddTransient<R02ReportWindow>();
        services.AddTransient<R03ReportViewModel>();
        services.AddTransient<R03ReportWindow>();
        return services;
    }
}
