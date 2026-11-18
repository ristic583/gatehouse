using Gatehouse.Application.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.Tenancy;

public static class TenantStatusChannelSetup
{
    /// <summary>
    /// Distributed tenant status invalidation (GH-AUTH-03): Redis publisher when an <see cref="IConnectionMultiplexer"/> is registered
    /// (else <see cref="LocalTenantStatusNotifier"/>) and the <see cref="TenantStatusSubscriber"/> hosted service. Call once per host.
    /// </summary>
    public static IServiceCollection AddTenantStatusChannel(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<ITenantStatusNotifier>(sp => sp.GetService<IConnectionMultiplexer>() is { } redis
            ? new RedisTenantStatusChannel(
                redis,
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<RedisTenantStatusChannel>>())
            : new LocalTenantStatusNotifier()));
        services.AddHostedService<TenantStatusSubscriber>();
        return services;
    }
}
