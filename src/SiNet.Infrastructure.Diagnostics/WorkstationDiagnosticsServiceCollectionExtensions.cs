using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Diagnostics;

namespace SiNet.Infrastructure.Diagnostics;

/// <summary>
/// Registers the workstation crash report adapters (DEV-010). Hosts call this themselves: the module
/// targets <c>net10.0-windows</c> (Event Log, WMI) while <c>SiNet.App.Composition</c> is
/// platform-neutral <c>net10.0</c> — the same constraint documented for the Secrets module.
/// Idempotent.
/// </summary>
public static class WorkstationDiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetWorkstationDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IWorkstationEventLogReader, WindowsEventLogCrashReader>();
        services.TryAddSingleton<IMachineProfileProvider, WmiMachineProfileProvider>();
        services.TryAddSingleton<IWorkstationCrashReportStore, FileSystemCrashReportStore>();

        return services;
    }
}
