namespace Gatehouse.Application.LiveBuilding.ConnectorHealth;

/// <summary>Last heartbeat of a connector, cached under <c>tenant:{tenantId}:connector:{pacsInstanceId}:health</c>.</summary>
public sealed record ConnectorHeartbeatSnapshot(string AgentVersion, int QueueDepth, bool PacsReachable, DateTimeOffset ReceivedAt);
