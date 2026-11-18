using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

/// <summary>Caches each heartbeat and pushes connector health to the live view (no DB writes, so no dedup needed).</summary>
public sealed class ConnectorHealthConsumer(RecordConnectorHeartbeatHandler handler) : IConsumer<ConnectorHeartbeatReceived>
{
    public async Task Consume(ConsumeContext<ConnectorHeartbeatReceived> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;
        await handler.HandleAsync(
            new RecordConnectorHeartbeatCommand(m.PacsInstanceId, m.AgentVersion, m.QueueDepth, m.PacsReachable, m.ReceivedAt),
            context.CancellationToken);
    }
}
