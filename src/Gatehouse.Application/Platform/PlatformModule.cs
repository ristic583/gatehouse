using FluentValidation;
using Gatehouse.Application.Platform.CreateTenant;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.GetTenant;
using Gatehouse.Application.Platform.ListPlatformAlerts;
using Gatehouse.Application.Platform.ListPlatformAudit;
using Gatehouse.Application.Platform.ListTenants;
using Gatehouse.Application.Platform.ReactivateTenant;
using Gatehouse.Application.Platform.ResendAdminInvite;
using Gatehouse.Application.Platform.RetryProvisioning;
using Gatehouse.Application.Platform.SuspendTenant;
using Gatehouse.Application.Platform.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Platform;

public static class PlatformModule
{
    /// <summary>Platform operator handlers (catalog only). Needs <see cref="IPlatformCatalogDb"/>, <see cref="IPlatformAuditLog"/> and <see
    /// cref="ITenantAccessCache"/>.</summary>
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.TryAddSingleton<ITenantStatusNotifier, LocalTenantStatusNotifier>();
        services.TryAddScoped<PlatformTenantViews>();
        services.TryAddScoped<CreateTenantHandler>();
        services.TryAddScoped<ListTenantsHandler>();
        services.TryAddScoped<GetTenantHandler>();
        services.TryAddScoped<SuspendTenantHandler>();
        services.TryAddScoped<ReactivateTenantHandler>();
        services.TryAddScoped<RetryProvisioningHandler>();
        services.TryAddScoped<ResendAdminInviteHandler>();
        services.TryAddScoped<ListPlatformAlertsHandler>();
        services.TryAddScoped<ListPlatformAuditHandler>();
        services.TryAddSingleton<IValidator<CreateTenantRequest>, CreateTenantValidator>();
        services.TryAddSingleton<IValidator<SuspendTenantRequest>, SuspendTenantValidator>();
        return services;
    }
}
