using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Identity.IdentityProviders;

public static class IdentityProvidersModule
{
    /// <summary>Tenant-admin identity provider handlers; needs <see cref="IIdpMetadataFetcher"/> and <see
    /// cref="ICatalogTenantIdentity"/>.</summary>
    public static IServiceCollection AddIdentityProviderHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<IdentityProviderStore>();
        services.TryAddScoped<IdentityProviderChecks>();
        services.TryAddScoped<IdentityProviderViews>();
        services.TryAddScoped<GetIdentityProviderHandler>();
        services.TryAddScoped<TestIdentityProviderHandler>();
        services.TryAddScoped<SaveIdentityProviderHandler>();
        services.TryAddScoped<RemoveIdentityProviderHandler>();
        services.TryAddSingleton<IValidator<SaveIdentityProviderRequest>, SaveIdentityProviderValidator>();
        return services;
    }
}
