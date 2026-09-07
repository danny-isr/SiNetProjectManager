using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Billing;
using SiNet.Infrastructure.Sql.Services.Billing;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Registers Replica-first billing dashboard services and SiNet local review-decision writes (B5).
/// </summary>
public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetBillingSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<ReplicaBillingDataSource>();
        services.AddTransient<IReplicaBillingDataSource>(sp => sp.GetRequiredService<ReplicaBillingDataSource>());
        services.AddTransient<IMonthlyBillingEnrichmentDataSource, MonthlyBillingEnrichmentDataSource>();
        services.AddTransient<SqlBillingReviewDecisionService>();
        services.AddTransient<IBillingReviewDecisionService>(sp => sp.GetRequiredService<SqlBillingReviewDecisionService>());
        services.AddTransient<IBillingReviewDecisionStore>(sp => sp.GetRequiredService<SqlBillingReviewDecisionService>());
        services.AddTransient<IBillingDashboardReadService, SqlBillingDashboardReadService>();
        return services;
    }
}
