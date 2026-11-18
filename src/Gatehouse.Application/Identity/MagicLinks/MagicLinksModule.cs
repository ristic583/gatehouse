using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Identity.MagicLinks;

public static class MagicLinksModule
{
    public static IServiceCollection AddMagicLinkHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<IssueMagicLinkHandler>();
        services.TryAddScoped<RedeemGuestSessionHandler>();
        return services;
    }
}
