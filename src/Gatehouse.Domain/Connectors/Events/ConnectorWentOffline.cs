using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Domain.Connectors.Events;

public sealed record ConnectorWentOffline(
    PacsInstanceId PacsInstanceId,
    DateTimeOffset LastHeartbeatAt,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
