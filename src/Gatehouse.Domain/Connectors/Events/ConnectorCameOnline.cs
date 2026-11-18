using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Domain.Connectors.Events;

public sealed record ConnectorCameOnline(
    PacsInstanceId PacsInstanceId,
    DateTimeOffset? OfflineSince,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
