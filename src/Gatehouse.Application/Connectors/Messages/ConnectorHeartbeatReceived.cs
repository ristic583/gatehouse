using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors.Messages;

public sealed record ConnectorHeartbeatReceived(
    string TenantId,
    PacsInstanceId PacsInstanceId,
    ConnectorId ConnectorId,
    DateTimeOffset ReceivedAt,
    string AgentVersion,
    int QueueDepth,
    bool PacsReachable);
