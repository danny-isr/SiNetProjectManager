using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Composition;
using SiNet.App.Wpf.Admin.FileCatalog;
using SiNet.App.Wpf.Admin.MasterPlan;
using SiNet.App.Wpf.Admin.Permissions;
using SiNet.App.Wpf.Admin.ProjectTypeWorkflowPolicy;
using SiNet.App.Wpf.Admin.Security;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Admin.SystemStatus;
using SiNet.App.Wpf.Admin.Users;
using SiNet.App.Wpf.Admin.UserGroups;
using SiNet.App.Wpf.Admin.WorkflowOps;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Projects.Dashboard;
using SiNet.App.Wpf.Runtime;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Surfaces.Workflow;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Application.ProjectWork;
using SiNet.Application.Projects;

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
        services.AddTransient<WorkflowOpsDashboardViewModel>();
        services.AddTransient<WorkflowOpsDashboardWindow>();
        services.AddTransient<ProjectsDashboardViewModel>(sp =>
            new ProjectsDashboardViewModel(
                sp.GetRequiredService<IProjectDashboardQueryService>(),
                sp.GetRequiredService<IProjectFilterOptionsService>(),
                sp.GetRequiredService<ICurrentProjectContext>(),
                sp.GetService<IProjectWorkSurfaceHost>(),
                sp.GetService<IPlaceCatalogService>()));
        services.AddTransient<ProjectsDashboardWindow>();
        services.AddSiNetAutodeskStatusWpf();
        services.AddSiNetProjectContext();
        services.AddSiNetUserAdminWpf();
        services.AddSiNetUserGroupsAdminWpf();
        services.AddSiNetPermissionAdminWpf();
        services.AddSiNetFileCatalogAdminWpf();
        services.AddSiNetProjectTypeWorkflowPolicyAdminWpf();
        services.AddSiNetSecretAdminWpf();
        services.AddSiNetSettingsAdminWpf();
        services.AddSiNetMasterPlanAdminWpf();
        services.AddSiNetShell();
        services.AddSiNetTaskPanelReadOnly();
        services.AddSiNetWorkflowClosedViewer();

        // Transient per email surface — WebView2 must not be reparented across hosts.
        services.AddTransient<IEmailBodyRenderer, WebView2EmailBodyRenderer>();

        // Hidden WebView2 → 00_Email.pdf for ACC Inbox ingest (N4). Singleton + lazy/UI init.
        services.AddSingleton<WpfEmailBodyPdfRenderer>();
        services.AddSingleton<IEmailBodyPdfRenderer>(sp => sp.GetRequiredService<WpfEmailBodyPdfRenderer>());

        // Jumbo/WeTransfer download capture → ACC (N2). Singleton: one active download window.
        services.AddSingleton<IEmailExternalDownloadBrowserHost, WpfEmailExternalDownloadBrowserHost>();

        // Embedded ACC document viewer for ProjectWork (multi-tab WebView2; shared profile).
        services.AddSingleton<IAccViewerHost, WebView2AccViewerHost>();

        return services;
    }
}