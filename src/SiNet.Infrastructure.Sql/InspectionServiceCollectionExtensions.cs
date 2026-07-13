using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Infrastructure.Sql.Services.Ai;
using SiNet.Infrastructure.Sql.Services.Inspection;

namespace SiNet.Infrastructure.Sql;

/// <summary>Registers native Inspection SQL ports (read + write) and AI note reviewer.</summary>
public static class InspectionServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetInspectionSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IInspectionWorkspace, SqlInspectionWorkspace>();
        services.AddTransient<IInspectionNoteCommandService, SqlInspectionNoteCommandService>();
        services.AddTransient<IInspectionReportCommandService, SqlInspectionReportCommandService>();
        services.AddTransient<IInspectionDrawingCommandService, SqlInspectionDrawingCommandService>();
        services.AddTransient<IInspectionReportExportPort, UnavailableInspectionReportExportPort>();
        return services;
    }

    public static IServiceCollection AddSiNetAi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IInspectionNoteAiReviewer, OllamaInspectionNoteAiReviewer>();
        return services;
    }
}
