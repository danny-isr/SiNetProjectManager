using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Composition;
using SiNet.App.Wpf.Admin.Permissions;
using SiNet.App.Wpf.Admin.Security;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Admin.SystemStatus;
using SiNet.App.Wpf.Admin.Users;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Runtime;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Surfaces.Workflow;

namespace SiNet.App.Wpf;

/// <summary>
/// Registers the native New System WPF surfaces that belong to <c>src/SiNet.App.Wpf</c>.
/// Hosts may call this from temporary composition glue, but the feature registrations live here.
/// </summary>
public static class NewSystemWpfServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetNewSystemWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ProjectWork file-index runtime + hubs (FileServer/ACC; Drive via AddSiNetGoogle).
        // Must precede work-surface VM registration so ProjectWorkTreeViewModel can resolve them.
        services.AddSiNetProjectWorkRuntime();

        services.AddSiNetRuntimeStatus();
        services.AddTransient<SystemStatusViewModel>();
        services.AddTransient<SystemStatusWindow>();
        services.AddSiNetAutodeskStatusWpf();
        services.AddSiNetProjectContext();
        services.AddSiNetUserAdminWpf();
        services.AddSiNetPermissionAdminWpf();
        services.AddSiNetSecretAdminWpf();
        services.AddSiNetSettingsAdminWpf();
        services.AddSiNetShell();
        services.AddSiNetTaskPanelReadOnly();
        services.AddSiNetWorkflowClosedViewer();

        return services;
    }
}