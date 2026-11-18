using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.LiveBuilding;

public static class LiveBuildingInfrastructure
{
    /// <summary>Door state store, connector health cache and live update bus: Redis when an <see cref="IConnectionMultiplexer"/> is
    /// registered, else in-memory.</summary>
    public static IServiceCollection AddLiveBuildingInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryDoorStateCache>();
        services.TryAddSingleton<ILiveUpdateBus>(sp => sp.GetService<IConnectionMultiplexer>() is { } mux
            ? new RedisLiveUpdateBus(mux, sp.GetRequiredService<ILogger<RedisLiveUpdateBus>>())
            : new InMemoryLiveUpdateBus());
        services.TryAddSingleton<ILiveUpdateSubscriber>(sp => sp.GetRequiredService<ILiveUpdateBus>());
        services.TryAddScoped<ILiveUpdatePublisher, TenantLiveUpdatePublisher>();
        services.TryAddScoped<IDoorStateStore>(sp => sp.GetService<IConnectionMultiplexer>() is { } mux
            ? new RedisDoorStateStore(mux, sp.GetRequiredService<Application.Tenancy.ITenantContext>())
            : new InMemoryDoorStateStore(
                sp.GetRequiredService<InMemoryDoorStateCache>(), sp.GetRequiredService<Application.Tenancy.ITenantContext>()));
        services.TryAddSingleton<InMemoryOccupancyStore.Counters>();
        services.TryAddScoped<IOccupancyStore>(sp => sp.GetService<IConnectionMultiplexer>() is { } mux
            ? new RedisOccupancyStore(mux, sp.GetRequiredService<Application.Tenancy.ITenantContext>())
            : new InMemoryOccupancyStore(
                sp.GetRequiredService<InMemoryOccupancyStore.Counters>(), sp.GetRequiredService<Application.Tenancy.ITenantContext>()));
        services.TryAddSingleton<InMemoryConnectorHealthCache.Store>();
        services.TryAddScoped<IConnectorHealthCache>(sp => sp.GetService<IConnectionMultiplexer>() is { } mux
            ? new RedisConnectorHealthCache(mux, sp.GetRequiredService<Application.Tenancy.ITenantContext>())
            : new InMemoryConnectorHealthCache(
                sp.GetRequiredService<InMemoryConnectorHealthCache.Store>(), sp.GetRequiredService<Application.Tenancy.ITenantContext>()));
        return services;
    }
}
