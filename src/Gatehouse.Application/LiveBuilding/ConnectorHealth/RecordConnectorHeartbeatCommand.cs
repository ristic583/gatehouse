using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.LiveBuilding.ConnectorHealth;

public sealed record RecordConnectorHeartbeatCommand(
    PacsInstanceId PacsInstanceId, string AgentVersion, int QueueDepth, bool PacsReachable, DateTimeOffset ReceivedAt);
