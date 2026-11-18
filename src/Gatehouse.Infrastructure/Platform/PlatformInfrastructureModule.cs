using Gatehouse.Application.Platform;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Infrastructure.Platform;

public static class PlatformInfrastructureModule
{
    /// <summary>Catalog-only platform services (ADR-009 §2). Requires <c>AddGatehouseCatalog</c>.</summary>
    public static IServiceCollection AddGatehousePlatform(this IServiceCollection services)
    {
        services.AddScoped<PlatformOperatorAccessor>();
        services.AddScoped<IPlatformOperator>(sp => sp.GetRequiredService<PlatformOperatorAccessor>());
        services.AddScoped<IPlatformOperatorResolver>(sp => sp.GetRequiredService<PlatformOperatorAccessor>());
        services.AddScoped<IPlatformAuditLog, PlatformAuditLog>();
        services.AddScoped<PlatformOperatorBootstrapper>();
        services.AddScoped<TenantBaselineWriter>();
        services.AddScoped<IPlatformCatalogDb, PlatformCatalogDb>();
        services.AddSingleton<ITenantAccessCache, TenantAccessCache>();
        services.AddSingleton<TenantStatisticsCollector>();
        services.AddPlatformModule();
        services.AddSingleton<ITenantProvisioningRunner, TenantProvisioningRunner>();
        return services;
    }
}
