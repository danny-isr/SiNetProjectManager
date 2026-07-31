using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.ProjectTypeWorkflowPolicy;

/// <summary>DI for native ProjectType ↔ Workflow policy admin.</summary>
public static class ProjectTypeWorkflowPolicyServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetProjectTypeWorkflowPolicyAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<ProjectTypeWorkflowPolicyViewModel>();
        services.AddTransient<ProjectTypeWorkflowPolicyView>();
        services.AddTransient<ProjectTypeWorkflowPolicyWindow>();
        return services;
    }
}
