using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors.Messages;

public sealed record DoorEventsReceived(
    string TenantId,
    PacsInstanceId PacsInstanceId,
    ConnectorId ConnectorId,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<NormalizedDoorEvent> Events);
