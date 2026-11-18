using System.Text.Json;
using System.Text.Json.Serialization;
using Gatehouse.Application.Platform;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.Tenancy;

/// <summary>
/// Publishes tenant status changes on the Redis channel <c>Tenancy:StatusChannel</c> (default <see cref="DefaultChannel"/>).
/// A catalog-level channel, not tenant data, so it carries no <c>tenant:</c> prefix. Subscribers: <see cref="TenantStatusSubscriber"/>.
/// </summary>
public sealed class RedisTenantStatusChannel(
    IConnectionMultiplexer redis,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<RedisTenantStatusChannel> logger) : ITenantStatusNotifier
{
    public const string ChannelSetting = "Tenancy:StatusChannel";
    public const string DefaultChannel = "platform:tenant-status";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static RedisChannel ChannelFor(IConfiguration configuration) =>
        RedisChannel.Literal(configuration?[ChannelSetting] is { Length: > 0 } name ? name : DefaultChannel);

    public async Task PublishAsync(TenantId tenantId, TenantStatus status, CancellationToken ct)
    {
        var message = new TenantStatusMessage(tenantId.Value, status, time.GetUtcNow().ToUnixTimeMilliseconds());
        try
        {
            await redis.GetSubscriber().PublishAsync(ChannelFor(configuration), JsonSerializer.Serialize(message, Json));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // The catalog change is committed; other processes pick it up when their cache entry expires (10 s).
            logger.LogWarning(ex, "Tenant status change for {StatusTenantId} could not be published", tenantId.Value);
        }
    }
}
