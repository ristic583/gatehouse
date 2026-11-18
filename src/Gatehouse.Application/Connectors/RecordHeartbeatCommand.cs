using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Application.Connectors;

public sealed record RecordHeartbeatCommand(Heartbeat Heartbeat);
