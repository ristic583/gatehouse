using System.Text.Json;
using Gatehouse.Application.LiveBuilding.Dtos;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>Redis pub/sub: publishes to <c>tenant:{tenantId}:live</c>, subscribes to pattern <c>tenant:*:live</c>.</summary>
public sealed class RedisLiveUpdateBus(IConnectionMultiplexer redis, ILogger<RedisLiveUpdateBus> logger) : ILiveUpdateBus
{
    private static readonly RedisChannel Pattern = RedisChannel.Pattern("tenant:*:live");

    public async Task PublishAsync(string tenantId, LiveUpdateDto update, CancellationToken ct) =>
        await redis.GetSubscriber()
            .PublishAsync(RedisChannel.Literal(DoorStateKeys.LiveChannel(tenantId)), JsonSerializer.Serialize(update, LiveJson.Options));

    public async Task SubscribeAsync(Func<string, LiveUpdateDto, Task> handler, CancellationToken ct)
    {
        var queue = await redis.GetSubscriber().SubscribeAsync(Pattern);
        queue.OnMessage(async message =>
        {
            var channel = message.Channel.ToString();
            var parts = channel.Split(':');
            if (parts.Length != 3 || !message.Message.HasValue)
            {
                return;
            }

            try
            {
                if (JsonSerializer.Deserialize<LiveUpdateDto>(message.Message.ToString(), LiveJson.Options) is { } update)
                {
                    await handler(parts[1], update);
                }
            }
            catch (Exception ex)
            {
                // host boundary: one bad message or handler failure must not end the subscription
                logger.LogWarning(ex, "Live update on {Channel} could not be delivered", channel);
            }
        });
    }
}
