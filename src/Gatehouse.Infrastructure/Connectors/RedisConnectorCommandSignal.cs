using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.Connectors;

public sealed class RedisConnectorCommandSignal(IConnectionMultiplexer redis) : IConnectorCommandSignal
{
    public async Task NotifyAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, CancellationToken ct) =>
        await redis.GetSubscriber().PublishAsync(Channel(tenantId, pacsInstanceId), "1");

    public async Task WaitAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = redis.GetSubscriber();
        var channel = Channel(tenantId, pacsInstanceId);
        await subscriber.SubscribeAsync(channel, (_, _) => tcs.TrySetResult());
        try
        {
            await tcs.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            // Nothing queued within the window.
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    private static RedisChannel Channel(TenantId tenantId, PacsInstanceId id) =>
        RedisChannel.Literal($"tenant:{tenantId.Value}:connector:{id.Value}:commands");
}
