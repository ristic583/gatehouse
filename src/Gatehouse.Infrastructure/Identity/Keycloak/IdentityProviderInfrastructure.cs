using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

public static class IdentityProviderInfrastructure
{
    /// <summary>Metadata fetcher (SSRF-guarded primary handler; the caller bounds each fetch to 5 s) and the catalog writer for
    /// tenant-admin IdP management.</summary>
    public static IServiceCollection AddIdentityProviderInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<IdentityProviderOptions>().BindConfiguration(IdentityProviderOptions.SectionName);
        services.AddHttpClient<IIdpMetadataFetcher, IdpMetadataFetcher>((http, sp) =>
                new IdpMetadataFetcher(http, sp.GetRequiredService<IOptions<IdentityProviderOptions>>()))
            .ConfigurePrimaryHttpMessageHandler(
                sp => IdpMetadataFetcher.CreateHandler(sp.GetRequiredService<IOptions<IdentityProviderOptions>>().Value));
        services.AddScoped<ICatalogTenantIdentity, CatalogTenantIdentity>();
        return services;
    }
}
