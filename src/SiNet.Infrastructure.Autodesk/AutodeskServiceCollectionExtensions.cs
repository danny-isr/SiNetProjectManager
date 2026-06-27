using Microsoft.Extensions.DependencyInjection;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Modular DI registration for the Autodesk/ACC module. Real implementations of
/// <c>IAccProjectService</c> and <c>IAccDocumentService</c> are wired here during the
/// ACC migration round (or temporarily via <c>SiNet.LegacyBridge</c>).
/// </summary>
public static class AutodeskServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAutodesk(this IServiceCollection services)
    {
        // TODO (ACC/Autodesk migration round): register IAccProjectService / IAccDocumentService.
        return services;
    }
}
