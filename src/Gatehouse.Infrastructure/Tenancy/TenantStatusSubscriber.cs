using System.Text.Json;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.Tenancy;

/// <summary>
/// Every process subscribes to <see cref="RedisTenantStatusChannel"/> and evicts the cached connection of the tenant named in a
/// message (<see cref="TenantConnectionResolver.CacheKey"/>). After the subscription connection is restored, messages may have been
/// missed, so the whole tenant cache is cleared once. Without Redis this does nothing and the 10 s cache duration is the bound.
/// </summary>
public sealed class TenantStatusSubscriber(
    IServiceProvider services,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<TenantStatusSubscriber> logger) : IHostedService
{
    private IConnectionMultiplexer? _redis;
    private ChannelMessageQueue? _queue;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _redis = services.GetService<IConnectionMultiplexer>();
        if (_redis is null)
        {
            return;
        }

        _redis.ConnectionRestored += OnConnectionRestored;
        _queue = await _redis.GetSubscriber().SubscribeAsync(RedisTenantStatusChannel.ChannelFor(configuration));
        _queue.OnMessage(message => Evict(message.Message));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_redis is not null)
        {
            _redis.ConnectionRestored -= OnConnectionRestored;
        }

        if (_queue is not null)
        {
            await _queue.UnsubscribeAsync();
        }
    }

    private void Evict(RedisValue payload)
    {
        TenantStatusMessage? message = null;
        try
        {
            message = payload.HasValue
                ? JsonSerializer.Deserialize<TenantStatusMessage>(payload.ToString(), RedisTenantStatusChannel.Json)
                : null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Ignored a malformed tenant status message");
        }

        if (message is { TenantId.Length: > 0 })
        {
            cache.Remove(TenantConnectionResolver.CacheKey(new TenantId(message.TenantId)));
        }
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        if (e.ConnectionType != ConnectionType.Subscription)
        {
            return;
        }

        // IMemoryCache holds only tenant connections (TenantConnectionResolver); missed status messages make every entry suspect.
        (cache as MemoryCache)?.Clear();
        logger.LogInformation("Tenant status subscription restored; tenant connection cache cleared");
    }
}
